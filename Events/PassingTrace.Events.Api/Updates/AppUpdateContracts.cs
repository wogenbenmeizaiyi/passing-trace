namespace PassingTrace.Events.Api.Updates;

public sealed record AndroidReleaseManifest(
    string VersionName,
    int VersionCode,
    DateTimeOffset PublishedAt,
    string ObjectKey,
    string Sha256,
    long Size,
    string? Notes,
    int MinimumSupportedVersionCode = 1);

public sealed record AndroidUpdateResponse(
    bool UpdateAvailable,
    bool Required,
    string VersionName,
    int VersionCode,
    DateTimeOffset PublishedAt,
    string Sha256,
    long Size,
    string? Notes,
    Uri? DownloadUrl,
    DateTimeOffset? DownloadUrlExpiresAt);
