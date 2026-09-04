using Microsoft.Extensions.Options;
using PassingTrace.Events.Api.Media;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class S3ObjectStorageTests
{
    [Fact]
    public async Task PresignedUpload_UsesVirtualHostedS3Endpoint()
    {
        var options = Options.Create(new ObjectStorageOptions
        {
            Endpoint = "https://cn-nb1.rains3.com",
            PublicEndpoint = "https://cn-nb1.rains3.com",
            AccessKey = "EXAMPLE",
            SecretKey = "secret-example",
            Bucket = "passingtrace",
            Region = "us-east-1",
            ForcePathStyle = false,
            CreateBucketIfMissing = false,
            ConfigureCors = false,
        });

        using var storage = new S3ObjectStorage(options);
        var uri = await storage.CreateUploadUrlAsync(
            "users/1/media/photo.jpg",
            "image/jpeg",
            DateTimeOffset.UtcNow.AddMinutes(10),
            CancellationToken.None);

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("passingtrace.cn-nb1.rains3.com", uri.Host);
        Assert.Equal("/users/1/media/photo.jpg", uri.AbsolutePath);
        Assert.Contains("X-Amz-Signature=", uri.Query, StringComparison.OrdinalIgnoreCase);
    }
}
