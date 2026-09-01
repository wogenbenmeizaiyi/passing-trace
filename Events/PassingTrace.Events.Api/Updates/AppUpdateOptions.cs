namespace PassingTrace.Events.Api.Updates;

public sealed class AppUpdateOptions
{
    public const string SectionName = "AppUpdates";
    public string AndroidManifestKey { get; set; } = "releases/android/latest.json";
    public int DownloadUrlLifetimeMinutes { get; set; } = 30;
}
