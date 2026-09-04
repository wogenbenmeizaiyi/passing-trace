namespace PassingTrace.Events.Api.Media;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    public string Endpoint { get; set; } = "http://localhost:9000";
    public string PublicEndpoint { get; set; } = "http://localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = "passingtrace-private";
    public string Region { get; set; } = "us-east-1";
    public bool ForcePathStyle { get; set; } = true;
    public bool CreateBucketIfMissing { get; set; } = true;
    public bool ConfigureCors { get; set; } = true;
}
