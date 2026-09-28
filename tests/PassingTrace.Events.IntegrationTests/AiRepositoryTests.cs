using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Social;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence.Ai;
using Pgvector;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AiRepositoryTests(StorylinePostgresFixture fixture) : IClassFixture<StorylinePostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Record_reads_recheck_ownership_and_revocation_even_for_known_ids()
    {
        await using var db = new TraceDbContext(fixture.Options);
        const long owner = 981001, reader = 981002, stranger = 981003;
        var record = Record(owner);
        var privateRecord = Record(owner);
        var friendship = new Friendship { FirstUserId = owner, SecondUserId = reader, CreatedAt = Now };
        db.Friendships.Add(friendship);
        record.Participants.Add(new EventParticipant { UserId = reader, FriendshipId = friendship.Id });
        db.Events.AddRange(record, privateRecord);
        await db.SaveChangesAsync();
        var queries = new PersonalRecordQueries(db);
        var ids = new[] { record.Id, privateRecord.Id };
        Assert.Single(await queries.ReadRecordsAsync(reader, ids, null, default));
        Assert.Empty(await queries.ReadRecordDetailsAsync(stranger, ids, default));
        friendship.Active = false;
        await db.SaveChangesAsync();
        Assert.Empty(await queries.ReadRecordsAsync(reader, ids, null, default));
        Assert.Empty(await queries.ReadRecordDetailsAsync(reader, ids, default));
        Assert.Equal(2, (await queries.ReadRecordDetailsAsync(owner, ids, default)).Count);
    }

    [Fact]
    public async Task Vector_queries_keep_user_scope_for_records_and_memories()
    {
        await using var db = new TraceDbContext(fixture.Options);
        const long owner = 981011;
        var vector = new float[1024]; vector[0] = 1;
        var own = Record(owner); var foreign = Record(owner + 1);
        db.Events.AddRange(own, foreign);
        foreach (var record in new[] { own, foreign })
            db.Entry(record.SearchIndexes[0]).Property<Vector>("Embedding").CurrentValue = new Vector(vector);
        var memory = Memory(owner); var foreignMemory = Memory(owner + 1);
        db.UserMemories.AddRange(memory, foreignMemory);
        foreach (var item in new[] { memory, foreignMemory })
            db.Entry(item).Property<Vector>("Embedding").CurrentValue = new Vector(vector);
        await db.SaveChangesAsync();
        var queries = new PersonalRecordQueries(db);
        var filter = new RecordSearchFilter("记录", null, null, null, null, null, null, null, null, null, null, null, null);
        Assert.Equal(own.Id, Assert.Single(await queries.RankRecordsAsync(owner, filter, AiSearchOrder.Vector, vector, default)));
        Assert.Equal(memory.Id, Assert.Single(await queries.SearchMemoriesAsync(owner, "记录", 10, vector, default)).Id);
    }

    [Fact]
    public async Task Conversation_reads_reject_foreign_and_deleted_threads()
    {
        await using var db = new TraceDbContext(fixture.Options);
        const long owner = 981021;
        var conversation = new AiConversation { Id = Guid.NewGuid(), UserId = owner, Title = "测试", CreatedAt = Now, UpdatedAt = Now };
        conversation.Messages.Add(new AiMessage { UserId = owner, Role = AiMessageRole.User, Content = "私有正文", CreatedAt = Now });
        conversation.Summary = new ConversationSummary { UserId = owner, Content = "私有摘要", UpdatedAt = Now };
        db.AiConversations.Add(conversation);
        await db.SaveChangesAsync();
        var repository = new AiConversationRepository(db);
        Assert.Null(await repository.ReadHeaderAsync(owner + 1, conversation.Id, default));
        Assert.Null(await repository.FindSummaryAsync(owner + 1, conversation.Id, default));
        Assert.Empty(await repository.ReadMessagesAsync(owner + 1, conversation.Id, new(), default));
        Assert.Single(await repository.ReadMessagesAsync(owner, conversation.Id, new(), default));
        conversation.DeletedAt = Now;
        await db.SaveChangesAsync();
        Assert.Empty(await repository.ReadMessagesAsync(owner, conversation.Id, new(), default));
        Assert.Null(await repository.FindSummaryAsync(owner, conversation.Id, default));
    }

    [Fact]
    public async Task Reparse_enqueues_the_current_revision_only_for_the_owner()
    {
        await using var db = new TraceDbContext(fixture.Options);
        const long owner = 981031;
        var record = Record(owner);
        record.CurrentSourceRevision = 2;
        record.SourceRevisions.Add(SourceRevision.Create(0, 2, record.Title, record.RawContent, Now, null, Now));
        db.Events.Add(record);
        await db.SaveChangesAsync();
        var repository = new EventSemanticRepository(db);
        Assert.Null(await repository.ReadAsync(owner + 1, record.Id, default));
        Assert.False(await repository.RequestReparseAsync(owner + 1, record.Id, Now, default));
        Assert.True(await repository.RequestReparseAsync(owner, record.Id, Now, default));
        var job = await db.OutboxMessages.SingleAsync(x => x.EventId == record.Id);
        Assert.Equal(owner, job.UserId);
        Assert.Equal(2, job.SourceRevision);
        Assert.Equal("event.analyze", job.MessageType);
        Assert.Equal("{\"force\":true}", job.PayloadJson);
        record.DeletedAt = Now;
        await db.SaveChangesAsync();
        Assert.False(await repository.RequestReparseAsync(owner, record.Id, Now, default));
        Assert.Null(await repository.ReadAsync(owner, record.Id, default));
    }

    [Fact]
    public async Task Rejecting_memories_changes_only_the_owner_and_updates_the_watermark()
    {
        await using var db = new TraceDbContext(fixture.Options);
        const long owner = 981041;
        var own = Memory(owner); var foreign = Memory(owner + 1);
        db.UserMemories.AddRange(own, foreign);
        await db.SaveChangesAsync();
        var repository = new UserMemoryRepository(db);
        await repository.RejectAllAsync(owner, Now, default);
        db.ChangeTracker.Clear();
        Assert.Empty(await repository.ListAsync(owner, default));
        Assert.Single(await repository.ListAsync(owner + 1, default));
        Assert.Equal(1, await new AiConversationRepository(db).ReadWatermarkAsync(owner, default));
        Assert.Equal(0, await new AiConversationRepository(db).ReadWatermarkAsync(owner + 1, default));
    }

    private static Event Record(long userId)
    {
        var record = Event.Create(userId, EventKind.Trace, "仓储测试", "记录正文", Now, null, "UTC", Guid.NewGuid().ToString("N"), Now);
        record.SourceRevisions.Add(SourceRevision.Create(0, 1, record.Title, record.RawContent, Now, null, Now));
        record.SearchIndexes.Add(new EventSearchIndex { UserId = userId, SourceRevision = 1, Title = record.Title!, RawContent = record.RawContent!, RetrievalText = "记录正文", IsCurrent = true, UpdatedAt = Now });
        return record;
    }

    private static UserMemory Memory(long userId) => new()
    {
        UserId = userId,
        Content = "有据可查的记忆",
        Fingerprint = Guid.NewGuid().ToString("N"),
        Type = UserMemoryType.Preference,
        Status = UserMemoryStatus.Confirmed,
        CreatedAt = Now,
        UpdatedAt = Now
    };
}
