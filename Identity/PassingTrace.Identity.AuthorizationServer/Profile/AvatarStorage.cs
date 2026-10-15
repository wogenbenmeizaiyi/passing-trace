using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;

namespace PassingTrace.Identity.AuthorizationServer.Profile;

public interface IAvatarStorage
{
    Task PutAsync(string key, byte[] image, CancellationToken ct);
    Task<byte[]> ReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>与记录复用 S3 配置，但头像不创建 MediaAsset 或 AI 分析任务。</summary>
public sealed class AvatarStorage(IConfiguration configuration) : IAvatarStorage
{
    private string Bucket => configuration["ObjectStorage:Bucket"] ?? "passingtrace-private";

    private AmazonS3Client CreateClient()
    {
        var access = configuration["ObjectStorage:AccessKey"];
        var secret = configuration["ObjectStorage:SecretKey"];
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("头像存储尚未配置。");
        var endpoint = new Uri(configuration["ObjectStorage:Endpoint"] ?? "http://localhost:9000");
        return new AmazonS3Client(new BasicAWSCredentials(access, secret), new AmazonS3Config
        {
            ServiceURL = endpoint.ToString().TrimEnd('/'),
            UseHttp = endpoint.Scheme == "http",
            ForcePathStyle = configuration.GetValue("ObjectStorage:ForcePathStyle", true),
            AuthenticationRegion = configuration["ObjectStorage:Region"] ?? "us-east-1",
        });
    }

    public async Task PutAsync(string key, byte[] image, CancellationToken ct)
    {
        using var client = CreateClient();
        if (configuration.GetValue("ObjectStorage:CreateBucketIfMissing", true) &&
            !await AmazonS3Util.DoesS3BucketExistV2Async(client, Bucket))
            await client.PutBucketAsync(new PutBucketRequest { BucketName = Bucket }, ct);
        using var stream = new MemoryStream(image);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            InputStream = stream,
            ContentType = "image/png",
        }, ct);
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken ct)
    {
        using var client = CreateClient();
        using var response = await client.GetObjectAsync(Bucket, key, ct);
        // Only generated 512px PNG avatars are read, with a hard upper bound.
        if (response.ContentLength > 5 * 1024 * 1024) throw new InvalidDataException();
        using var output = new MemoryStream();
        await response.ResponseStream.CopyToAsync(output, ct);
        return output.ToArray();
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        using var client = CreateClient();
        await client.DeleteObjectAsync(Bucket, key, ct);
    }
}
