namespace PassingTrace.Events.Api.Media;

public sealed record StoredObjectInfo(long Size, string? ContentType);
public sealed record CompletedPart(int PartNumber, string ETag);

public interface IObjectStorage
{
    Task EnsureBucketAsync(CancellationToken cancellationToken);
    Task<string> CreateMultipartUploadAsync(string objectKey, string contentType, CancellationToken cancellationToken);
    Task<Uri> CreateUploadUrlAsync(string objectKey, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<Uri> CreatePartUploadUrlAsync(string objectKey, string uploadId, int partNumber, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<string> UploadPartAsync(string objectKey, string uploadId, int partNumber, Stream content, long contentLength, CancellationToken cancellationToken);
    Task CompleteMultipartUploadAsync(string objectKey, string uploadId, IReadOnlyList<CompletedPart> parts, CancellationToken cancellationToken);
    Task AbortMultipartUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken);
    Task<StoredObjectInfo> GetInfoAsync(string objectKey, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
    Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Uri> CreateDownloadUrlAsync(string objectKey, string fileName, string contentType, bool inline, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}
