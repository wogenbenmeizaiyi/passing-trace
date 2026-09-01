using System.Net;
using System.Text.Json;
using Amazon.S3;
using Microsoft.Extensions.Options;
using PassingTrace.Events.Api.Media;

namespace PassingTrace.Events.Api.Updates;

public sealed class AppUpdateService(
    IObjectStorage storage,
    IOptions<AppUpdateOptions> options,
    TimeProvider timeProvider)
{
    private const int MaxManifestBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppUpdateOptions _options = options.Value;

    public async Task<AndroidUpdateResponse> GetAndroidUpdateAsync(
        int currentVersionCode,
        CancellationToken cancellationToken)
    {
        if (currentVersionCode < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentVersionCode));
        }

        var manifest = await ReadManifestAsync(cancellationToken);

        var updateAvailable = manifest.VersionCode > currentVersionCode;
        if (!updateAvailable)
        {
            return ToResponse(manifest, currentVersionCode, null, null);
        }

        var (downloadUrl, expiresAt) = await CreateDownloadAsync(manifest, cancellationToken);
        return ToResponse(manifest, currentVersionCode, downloadUrl, expiresAt);
    }

    public async Task<Uri> GetLatestAndroidDownloadAsync(CancellationToken cancellationToken)
    {
        var manifest = await ReadManifestAsync(cancellationToken);
        var (downloadUrl, _) = await CreateDownloadAsync(manifest, cancellationToken);
        return downloadUrl;
    }

    private async Task<AndroidReleaseManifest> ReadManifestAsync(CancellationToken cancellationToken)
    {
        Stream source;
        try
        {
            source = await storage.OpenReadAsync(
                _options.AndroidManifestKey,
                cancellationToken);
        }
        catch (AmazonS3Exception exception) when (
            exception.StatusCode == HttpStatusCode.NotFound ||
            string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyNotFoundException("当前暂无可下载的 Android 安装包。", exception);
        }

        await using (source)
        {
            await using var manifestBuffer = new MemoryStream();
            await source.CopyToAsync(manifestBuffer, cancellationToken);
            if (manifestBuffer.Length is 0 or > MaxManifestBytes)
            {
                throw new InvalidDataException("安卓更新清单无效。");
            }

            var manifest = JsonSerializer.Deserialize<AndroidReleaseManifest>(
                manifestBuffer.ToArray(),
                JsonOptions) ?? throw new InvalidDataException("安卓更新清单无效。");
            Validate(manifest);
            return manifest;
        }
    }

    private async Task<(Uri Url, DateTimeOffset ExpiresAt)> CreateDownloadAsync(
        AndroidReleaseManifest manifest,
        CancellationToken cancellationToken)
    {
        var expiresAt = timeProvider.GetUtcNow().AddMinutes(
            Math.Clamp(_options.DownloadUrlLifetimeMinutes, 5, 60));
        var downloadUrl = await storage.CreateDownloadUrlAsync(
            manifest.ObjectKey,
            $"星期八-{manifest.VersionName}-{manifest.VersionCode}.apk",
            "application/vnd.android.package-archive",
            inline: false,
            expiresAt,
            cancellationToken);
        return (downloadUrl, expiresAt);
    }

    private static AndroidUpdateResponse ToResponse(
        AndroidReleaseManifest manifest,
        int currentVersionCode,
        Uri? downloadUrl,
        DateTimeOffset? expiresAt) => new(
            manifest.VersionCode > currentVersionCode,
            currentVersionCode < manifest.MinimumSupportedVersionCode,
            manifest.VersionName,
            manifest.VersionCode,
            manifest.PublishedAt,
            manifest.Sha256,
            manifest.Size,
            manifest.Notes,
            downloadUrl,
            expiresAt);

    private static void Validate(AndroidReleaseManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.VersionName) ||
            manifest.VersionCode < 1 ||
            manifest.Size < 1 ||
            manifest.Sha256.Length != 64 ||
            !manifest.Sha256.All(Uri.IsHexDigit) ||
            !manifest.ObjectKey.StartsWith("releases/android/", StringComparison.Ordinal) ||
            !manifest.ObjectKey.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
            manifest.ObjectKey.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("安卓更新清单无效。");
        }
    }
}
