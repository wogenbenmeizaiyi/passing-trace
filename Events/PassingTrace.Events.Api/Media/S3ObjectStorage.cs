using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;
using System.Net;

namespace PassingTrace.Events.Api.Media;

/// <summary>兼容 MinIO 和云厂商 S3 的私有对象存储适配器。</summary>
public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly ObjectStorageOptions _options;
    private readonly AmazonS3Client _internalClient;
    private readonly AmazonS3Client _publicPresigner;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketReady;

    public S3ObjectStorage(IOptions<ObjectStorageOptions> options)
    {
        _options = options.Value;
        var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
        _internalClient = new AmazonS3Client(credentials, CreateConfig(_options.Endpoint));
        _publicPresigner = new AmazonS3Client(credentials, CreateConfig(_options.PublicEndpoint));
    }

    public async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (_bucketReady)
        {
            return;
        }

        await _bucketLock.WaitAsync(cancellationToken);
        try
        {
            if (_bucketReady)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_options.AccessKey) || string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                throw new InvalidOperationException("对象存储凭据未配置。请设置 ObjectStorage:AccessKey 与 SecretKey。");
            }

            if (_options.CreateBucketIfMissing &&
                !await AmazonS3Util.DoesS3BucketExistV2Async(_internalClient, _options.Bucket))
            {
                await _internalClient.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, cancellationToken);
            }

            if (_options.ConfigureCors)
            {
                try
                {
                    await _internalClient.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
                    {
                        BucketName = _options.Bucket,
                        Configuration = new CORSConfiguration
                        {
                            Rules =
                            [
                                new CORSRule
                                {
                                    AllowedOrigins = ["*"],
                                    AllowedMethods = ["GET", "HEAD", "PUT"],
                                    AllowedHeaders = ["*"],
                                    ExposeHeaders = ["ETag"],
                                    MaxAgeSeconds = 3600,
                                },
                            ],
                        },
                    }, cancellationToken);
                }
                catch (AmazonS3Exception exception) when (
                    exception.StatusCode == HttpStatusCode.NotImplemented ||
                    string.Equals(exception.ErrorCode, "NotImplemented", StringComparison.OrdinalIgnoreCase))
                {
                    // MinIO 可通过容器环境配置 CORS，部分版本不实现 S3 PutBucketCors。
                }
            }

            _bucketReady = true;
        }
        finally
        {
            _bucketLock.Release();
        }
    }

    public async Task<string> CreateMultipartUploadAsync(string objectKey, string contentType, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        var response = await _internalClient.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest
            {
                BucketName = _options.Bucket,
                Key = objectKey,
                ContentType = contentType,
            },
            cancellationToken);
        return response.UploadId;
    }

    public async Task<Uri> CreateUploadUrlAsync(string objectKey, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        var url = await _publicPresigner.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = expiresAt.UtcDateTime,
        });
        return NormalizePublicUrl(url);
    }

    public async Task<Uri> CreatePartUploadUrlAsync(string objectKey, string uploadId, int partNumber, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        var url = await _publicPresigner.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            UploadId = uploadId,
            PartNumber = partNumber,
            Expires = expiresAt.UtcDateTime,
        });
        return NormalizePublicUrl(url);
    }

    public async Task<string> UploadPartAsync(
        string objectKey,
        string uploadId,
        int partNumber,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken)
    {
        var response = await _internalClient.UploadPartAsync(new UploadPartRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            UploadId = uploadId,
            PartNumber = partNumber,
            InputStream = content,
            PartSize = contentLength,
        }, cancellationToken);
        return response.ETag;
    }

    public async Task CompleteMultipartUploadAsync(string objectKey, string uploadId, IReadOnlyList<CompletedPart> parts, CancellationToken cancellationToken)
    {
        await _internalClient.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            UploadId = uploadId,
            PartETags = parts.OrderBy(x => x.PartNumber)
                .Select(x => new PartETag(x.PartNumber, x.ETag.Trim('"')))
                .ToList(),
        }, cancellationToken);
    }

    public Task AbortMultipartUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken) =>
        _internalClient.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            UploadId = uploadId,
        }, cancellationToken);

    public async Task<StoredObjectInfo> GetInfoAsync(string objectKey, CancellationToken cancellationToken)
    {
        var response = await _internalClient.GetObjectMetadataAsync(_options.Bucket, objectKey, cancellationToken);
        return new StoredObjectInfo(response.Headers.ContentLength, response.Headers.ContentType);
    }

    public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        var response = await _internalClient.GetObjectAsync(_options.Bucket, objectKey, cancellationToken);
        return new ResponseOwnedStream(response);
    }

    public async Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        await _internalClient.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        }, cancellationToken);
    }

    public async Task<Uri> CreateDownloadUrlAsync(string objectKey, string fileName, string contentType, bool inline, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken);
        var safeName = fileName.Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
        var url = await _publicPresigner.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentType = contentType,
                ContentDisposition = $"{(inline ? "inline" : "attachment")}; filename=\"{safeName}\"",
            },
        });
        return NormalizePublicUrl(url);
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
        _internalClient.DeleteObjectAsync(_options.Bucket, objectKey, cancellationToken);

    public void Dispose()
    {
        _internalClient.Dispose();
        _publicPresigner.Dispose();
        _bucketLock.Dispose();
    }

    private AmazonS3Config CreateConfig(string endpoint)
    {
        var endpointUri = new Uri(endpoint.TrimEnd('/'));
        return new AmazonS3Config
        {
            ServiceURL = endpointUri.ToString().TrimEnd('/'),
            UseHttp = endpointUri.Scheme == Uri.UriSchemeHttp,
            ForcePathStyle = _options.ForcePathStyle,
            AuthenticationRegion = _options.Region,
        };
    }

    private Uri NormalizePublicUrl(string generatedUrl)
    {
        // URL 由使用 PublicEndpoint 创建的独立签名客户端生成。不能在签名后替换 Host，
        // 否则会破坏 SigV4；对 COS 还会丢失 <bucket>.cos.<region> 虚拟主机前缀。
        return new Uri(generatedUrl);
    }

    private sealed class ResponseOwnedStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                response.Dispose();
            }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            response.Dispose();
            await base.DisposeAsync();
        }
    }
}
