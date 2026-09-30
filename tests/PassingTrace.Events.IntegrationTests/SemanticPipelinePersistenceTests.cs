using PassingTrace.Events.Api.Ai.Models;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PassingTrace.Ai.Worker;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Events.Api.Media;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence.Ai;
using Pgvector;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class SemanticPipelinePersistenceTests(StorylinePostgresFixture fixture) : IClassFixture<StorylinePostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly SemanticEnvelope Envelope = new("午餐花费 32 元，喜欢清淡口味。", [], [],
        [new(32, "CNY", "午餐", "本人", 0.95m, "午餐32元")],
        [new("preference", "喜欢清淡口味", 0.9m, "用户正文")], new("food", 0.95m, null, null, null), []);

    [Fact]
    public async Task Pipeline_publishes_results_once_and_respects_manual_labels_and_rejected_memories()
    {
        await using var db = new TraceDbContext(fixture.Options);
        const long userId = 983001;
        var evt = Record(userId);
        var source = evt.SourceRevisions[0];
        source.Locations.Add(new EventLocation
        {
            UserId = userId,
            Event = evt,
            SourceRevision = 1,
            Name = "食堂",
            ProviderPoiId = "test-poi",
            UserConfirmed = true,
            CreatedAt = Now
        });
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        db.EventLabelIndexes.Add(new EventLabelIndex
        {
            UserId = userId,
            EventId = evt.Id,
            SourceRevision = 1,
            Type = EventLabelType.PrimaryCategory,
            Origin = EventLabelOrigin.Manual,
            TaxonomyKey = "social",
            DisplayName = "社交",
            NormalizedValue = "社交",
            IsCurrent = true,
            CreatedAt = Now
        });
        await db.SaveChangesAsync();
        var chat = new SemanticClient();
        var pipeline = Pipeline(db, chat);
        var message = new OutboxMessage { UserId = userId, EventId = evt.Id, SourceRevision = 1 };
        await pipeline.AnalyzeEventAsync(message, default);
        await pipeline.AnalyzeEventAsync(message, default);
        Assert.Equal(1, chat.Calls);
        var run = await db.EventSemanticRuns.SingleAsync(x => x.EventId == evt.Id);
        Assert.Equal(SemanticRunStatus.Completed, run.Status);
        Assert.Equal(32, (await db.ExpenseFacts.SingleAsync(x => x.SemanticRunId == run.Id)).Amount);
        Assert.True(await db.EventLabelIndexes.AnyAsync(x => x.EventId == evt.Id && x.IsCurrent && x.TaxonomyKey == "social" && x.Origin == EventLabelOrigin.Manual));
        Assert.True(await db.EventLabelIndexes.AnyAsync(x => x.EventId == evt.Id && x.IsCurrent && x.TaxonomyKey == "amount"));
        var index = await db.EventSearchIndexes.SingleAsync(x => x.EventId == evt.Id && x.IsCurrent);
        Assert.Contains("社交", index.RetrievalText);
        Assert.NotNull(db.Entry(index).Property<Vector?>("Embedding").CurrentValue);
        Assert.Equal(1, (await db.UserPlaces.SingleAsync(x => x.UserId == userId)).VisitCount);
        Assert.Equal(1, await new AiConversationRepository(db).ReadWatermarkAsync(userId, default));
        var memory = await db.UserMemories.Include(x => x.Evidence).SingleAsync(x => x.UserId == userId);
        Assert.Equal(evt.Id, Assert.Single(memory.Evidence).EventId);
        memory.Status = UserMemoryStatus.Rejected;
        await db.SaveChangesAsync();
        message.PayloadJson = "{\"force\":true}";
        await pipeline.AnalyzeEventAsync(message, default);
        Assert.Equal(2, chat.Calls);
        Assert.Equal(UserMemoryStatus.Rejected, (await db.UserMemories.SingleAsync(x => x.UserId == userId)).Status);
        Assert.NotEqual(run.Id, (await db.EventSearchIndexes.SingleAsync(x => x.EventId == evt.Id && x.IsCurrent)).SemanticRunId);
    }

    [Fact]
    public async Task Pipeline_discards_results_when_source_changes_during_model_call()
    {
        await using var db = new TraceDbContext(fixture.Options);
        var evt = Record(983011);
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        var chat = new SemanticClient(async () =>
        {
            await using var editor = new TraceDbContext(fixture.Options);
            var current = await editor.Events.SingleAsync(x => x.Id == evt.Id);
            current.CurrentSourceRevision = 2;
            current.SourceRevisions.Add(SourceRevision.Create(current.Id, 2, current.Title, "已编辑", Now, null, Now));
            await editor.SaveChangesAsync();
        });
        await Pipeline(db, chat).AnalyzeEventAsync(new OutboxMessage { UserId = evt.UserId, EventId = evt.Id, SourceRevision = 1 }, default);
        Assert.Equal(SemanticRunStatus.Stale, (await db.EventSemanticRuns.SingleAsync(x => x.EventId == evt.Id)).Status);
        Assert.False(await db.EventSearchIndexes.AnyAsync(x => x.EventId == evt.Id));
        Assert.False(await db.UserMemories.AnyAsync(x => x.UserId == evt.UserId));
    }

    [Fact]
    public async Task Pipeline_does_not_process_another_users_media()
    {
        await using var db = new TraceDbContext(fixture.Options);
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            UserId = 983021,
            Kind = MediaKind.Image,
            ObjectKey = "private-test-image",
            Status = MediaAssetStatus.Uploaded,
            CreatedAt = Now,
            UpdatedAt = Now,
            UploadExpiresAt = Now.AddHours(1)
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        await Pipeline(db, new SemanticClient()).ProcessMediaAsync(new OutboxMessage { UserId = asset.UserId + 1, MediaAssetId = asset.Id }, default);
        await db.Entry(asset).ReloadAsync();
        Assert.Equal(MediaAssetStatus.Uploaded, asset.Status);
    }

    private static SemanticPipeline Pipeline(TraceDbContext db, SemanticClient client) =>
        new(new SemanticPipelineRepository(db), new AnalysisOutbox(db), new UnusedStorage(), new ImageDerivativeProcessor(),
            client, new UnitEmbeddings(), Options.Create(new AiModelOptions()));

    private static Event Record(long userId)
    {
        var evt = Event.Create(userId, EventKind.Trace, "午餐", "午餐32元，喜欢清淡口味", Now, null, "UTC", Guid.NewGuid().ToString("N"), Now);
        evt.SourceRevisions.Add(SourceRevision.Create(0, 1, evt.Title, evt.RawContent, Now, null, Now));
        return evt;
    }

    private sealed class SemanticClient(Func<Task>? beforeResponse = null) : IChatClient
    {
        public int Calls { get; private set; }
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (beforeResponse is not null) await beforeResponse();
            return new(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(Envelope)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class UnitEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var vector = new float[1024]; vector[0] = 1;
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => new Embedding<float>(vector)).ToArray()));
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class UnusedStorage : IObjectStorage
    {
        public Task EnsureBucketAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CreateMultipartUploadAsync(string key, string type, CancellationToken ct) => throw new NotSupportedException();
        public Task<Uri> CreateUploadUrlAsync(string key, string type, DateTimeOffset expires, CancellationToken ct) => throw new NotSupportedException();
        public Task<Uri> CreatePartUploadUrlAsync(string key, string id, int part, DateTimeOffset expires, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> UploadPartAsync(string key, string id, int part, Stream content, long length, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteMultipartUploadAsync(string key, string id, IReadOnlyList<CompletedPart> parts, CancellationToken ct) => throw new NotSupportedException();
        public Task AbortMultipartUploadAsync(string key, string id, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo> GetInfoAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task PutAsync(string key, Stream content, string type, long length, CancellationToken ct) => throw new NotSupportedException();
        public Task<Uri> CreateDownloadUrlAsync(string key, string name, string type, bool inline, DateTimeOffset expires, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
