using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Microsoft.Extensions.Options;
using PassingTrace.Events.Api.Media;
using PassingTrace.Events.Api.Updates;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task NewerRelease_ReturnsShortLivedSignedDownload()
    {
        var manifest = new AndroidReleaseManifest(
            "1.2.0", 12, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
            "releases/android/PassingTrace-1.2.0-12.apk",
            new string('a', 64), 1024, "新增更新检查", 10);
        var storage = new ManifestStorage(manifest);
        var now = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var service = new AppUpdateService(
            storage,
            Options.Create(new AppUpdateOptions()),
            new FixedTimeProvider(now));

        var response = await service.GetAndroidUpdateAsync(9, CancellationToken.None);

        Assert.True(response.UpdateAvailable);
        Assert.True(response.Required);
        Assert.Equal(12, response.VersionCode);
        Assert.Equal(new Uri("https://passingtrace.cn-nb1.rains3.com/signed.apk"), response.DownloadUrl);
        Assert.Equal(now.AddMinutes(30), response.DownloadUrlExpiresAt);
        Assert.Equal(manifest.ObjectKey, storage.DownloadedObjectKey);
    }

    [Fact]
    public async Task CurrentRelease_DoesNotCreateDownloadUrl()
    {
        var storage = new ManifestStorage(new AndroidReleaseManifest(
            "1.0.0", 3, DateTimeOffset.UtcNow,
            "releases/android/PassingTrace-1.0.0-3.apk",
            new string('b', 64), 2048, null));
        var service = new AppUpdateService(
            storage,
            Options.Create(new AppUpdateOptions()),
            TimeProvider.System);

        var response = await service.GetAndroidUpdateAsync(3, CancellationToken.None);

        Assert.False(response.UpdateAvailable);
        Assert.Null(response.DownloadUrl);
        Assert.Null(storage.DownloadedObjectKey);
    }

    [Fact]
    public async Task BrowserWithoutInstalledVersion_ReturnsLatestDownload()
    {
        var manifest = new AndroidReleaseManifest(
            "1.0.0", 3, DateTimeOffset.UtcNow,
            "releases/android/PassingTrace-1.0.0-3.apk",
            new string('d', 64), 2048, null);
        var storage = new ManifestStorage(manifest);
        var service = new AppUpdateService(
            storage,
            Options.Create(new AppUpdateOptions()),
            TimeProvider.System);

        var response = await service.GetAndroidUpdateAsync(0, CancellationToken.None);

        Assert.True(response.UpdateAvailable);
        Assert.NotNull(response.DownloadUrl);
        Assert.Equal(manifest.ObjectKey, storage.DownloadedObjectKey);
    }

    [Fact]
    public async Task LatestDownload_AlwaysCreatesShortLivedSignedUrl()
    {
        var manifest = new AndroidReleaseManifest(
            "1.0.0", 3, DateTimeOffset.UtcNow,
            "releases/android/PassingTrace-1.0.0-3.apk",
            new string('c', 64), 2048, null);
        var storage = new ManifestStorage(manifest);
        var service = new AppUpdateService(
            storage,
            Options.Create(new AppUpdateOptions()),
            TimeProvider.System);

        var url = await service.GetLatestAndroidDownloadAsync(CancellationToken.None);

        Assert.Equal(new Uri("https://passingtrace.cn-nb1.rains3.com/signed.apk"), url);
        Assert.Equal(manifest.ObjectKey, storage.DownloadedObjectKey);
    }

    [Fact]
    public async Task MissingReleaseManifest_ReturnsFriendlyNotFound()
    {
        var service = new AppUpdateService(
            new MissingManifestStorage(),
            Options.Create(new AppUpdateOptions()),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GetLatestAndroidDownloadAsync(CancellationToken.None));

        Assert.Equal("当前暂无可下载的 Android 安装包。", exception.Message);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ManifestStorage(AndroidReleaseManifest manifest) : IObjectStorage
    {
        public string? DownloadedObjectKey { get; private set; }

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
        {
            Assert.Equal("releases/android/latest.json", objectKey);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public Task<Uri> CreateDownloadUrlAsync(string objectKey, string fileName, string contentType, bool inline, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            DownloadedObjectKey = objectKey;
            return Task.FromResult(new Uri("https://passingtrace.cn-nb1.rains3.com/signed.apk"));
        }

        public Task EnsureBucketAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> CreateMultipartUploadAsync(string objectKey, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Uri> CreateUploadUrlAsync(string objectKey, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Uri> CreatePartUploadUrlAsync(string objectKey, string uploadId, int partNumber, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteMultipartUploadAsync(string objectKey, string uploadId, IReadOnlyList<CompletedPart> parts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AbortMultipartUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredObjectInfo> GetInfoAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MissingManifestStorage : IObjectStorage
    {
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            throw new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound };

        public Task EnsureBucketAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CreateMultipartUploadAsync(string objectKey, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Uri> CreateUploadUrlAsync(string objectKey, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Uri> CreatePartUploadUrlAsync(string objectKey, string uploadId, int partNumber, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteMultipartUploadAsync(string objectKey, string uploadId, IReadOnlyList<CompletedPart> parts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AbortMultipartUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredObjectInfo> GetInfoAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Uri> CreateDownloadUrlAsync(string objectKey, string fileName, string contentType, bool inline, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
