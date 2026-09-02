using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PassingTrace.Events.Api.Media;
using PassingTrace.Infrastructure;
using Pgvector.EntityFrameworkCore;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

/// <summary>验证生产所用 PostgreSQL 扩展/迁移与 S3 兼容层，而不是 SQLite 替身。</summary>
public sealed class InfrastructureContainerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18-trixie")
        .WithDatabase("trace_tests")
        .WithUsername("postgres")
        .WithPassword("passingtrace-test-postgres")
        .Build();

    private readonly MinioContainer _minio = new MinioBuilder("minio/minio:RELEASE.2025-09-07T16-13-09Z")
        .WithUsername("passingtrace-test")
        .WithPassword("passingtrace-test-secret")
        .Build();

    public Task InitializeAsync() => Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _minio.DisposeAsync();
    }

    [Fact]
    public async Task PostgreSql18AndMinio_SupportMigrationsVectorAndPrivateObjects()
    {
        var dbOptions = new DbContextOptionsBuilder<TraceDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options;
        await using (var db = new TraceDbContext(dbOptions))
        {
            await db.Database.MigrateAsync();
            await using var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pg_extension WHERE extname IN ('vector', 'pg_trgm')";
            Assert.Equal(2L, Convert.ToInt64(await command.ExecuteScalarAsync()));
            command.CommandText = "SELECT format_type(atttypid, atttypmod) FROM pg_attribute " +
                "WHERE attrelid = 'event_search_index'::regclass AND attname = 'embedding'";
            Assert.Equal("vector(1024)", await command.ExecuteScalarAsync());
        }

        var storageOptions = Options.Create(new ObjectStorageOptions
        {
            Endpoint = _minio.GetConnectionString(),
            PublicEndpoint = _minio.GetConnectionString(),
            AccessKey = _minio.GetAccessKey(),
            SecretKey = _minio.GetSecretKey(),
            Bucket = $"passingtrace-test-{Guid.NewGuid():N}",
            Region = "us-east-1",
            ForcePathStyle = true,
        });
        using var storage = new S3ObjectStorage(storageOptions);
        await storage.EnsureBucketAsync(CancellationToken.None);
        const string key = "users/1/media/private.txt";
        var expected = "private PassingTrace object"u8.ToArray();
        await using (var source = new MemoryStream(expected, writable: false))
        await using (var upload = new NonSeekableReadStream(source))
        {
            await storage.PutAsync(key, upload, "text/plain", expected.Length, CancellationToken.None);
        }

        var info = await storage.GetInfoAsync(key, CancellationToken.None);
        Assert.Equal(expected.Length, info.Size);
        await using var stored = await storage.OpenReadAsync(key, CancellationToken.None);
        await using var copy = new MemoryStream();
        await stored.CopyToAsync(copy);
        Assert.Equal(expected, copy.ToArray());
        await storage.DeleteAsync(key, CancellationToken.None);
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
