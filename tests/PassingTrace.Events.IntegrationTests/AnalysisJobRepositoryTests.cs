using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence.Ai;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AnalysisJobRepositoryTests(StorylinePostgresFixture fixture) : IClassFixture<StorylinePostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Concurrent_workers_claim_distinct_due_jobs_and_persist_leases()
    {
        await using var db = new TraceDbContext(fixture.Options);
        await db.OutboxMessages.ExecuteDeleteAsync();
        var high = Job(100); var low = Job(50);
        var future = Job(200); future.AvailableAt = Now.AddHours(1);
        var leased = Job(300); leased.LeaseExpiresAt = Now.AddMinutes(1);
        db.OutboxMessages.AddRange(high, low, future, leased);
        await db.SaveChangesAsync();
        await using var first = new TraceDbContext(fixture.Options);
        await using var second = new TraceDbContext(fixture.Options);
        var claims = await Task.WhenAll(
            new AnalysisJobRepository(first).ClaimAsync("worker-a", Now, TimeSpan.FromMinutes(10), default),
            new AnalysisJobRepository(second).ClaimAsync("worker-b", Now, TimeSpan.FromMinutes(10), default));
        Assert.Equal(new[] { high.Id, low.Id }.Order(), claims.Select(x => x!.Value).Order());
        Assert.Null(await new AnalysisJobRepository(db).ClaimAsync("worker-c", Now, TimeSpan.FromMinutes(10), default));
        db.ChangeTracker.Clear();
        var claimed = await db.OutboxMessages.Where(x => x.Status == OutboxStatus.Processing).ToArrayAsync();
        Assert.Equal(2, claimed.Length);
        Assert.All(claimed, job =>
        {
            Assert.Equal(1, job.Attempts);
            Assert.Equal(Now.AddMinutes(10), job.LeaseExpiresAt);
            Assert.StartsWith("worker-", job.LeaseOwner);
        });
    }

    [Fact]
    public async Task Backfill_only_enqueues_missing_current_revisions_and_is_repeatable()
    {
        await using var db = new TraceDbContext(fixture.Options);
        var existing = Record(982001); var missing = Record(982001); var deleted = Record(982001);
        deleted.DeletedAt = Now;
        missing.CurrentSourceRevision = 2;
        missing.SourceRevisions.Add(SourceRevision.Create(0, 2, missing.Title, missing.RawContent, Now, null, Now));
        db.Events.AddRange(existing, missing, deleted);
        await db.SaveChangesAsync();
        var currentJob = Job(10); currentJob.EventId = existing.Id; currentJob.SourceRevision = 1;
        var oldJob = Job(10); oldJob.EventId = missing.Id; oldJob.SourceRevision = 1;
        db.OutboxMessages.AddRange(currentJob, oldJob);
        await db.SaveChangesAsync();
        var repository = new AnalysisJobRepository(db);
        await repository.BackfillAsync(Now, default);
        await repository.BackfillAsync(Now, default);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EventId == existing.Id));
        Assert.Equal(2, await db.OutboxMessages.CountAsync(x => x.EventId == missing.Id));
        var job = await db.OutboxMessages.SingleAsync(x => x.EventId == missing.Id && x.SourceRevision == 2);
        Assert.Equal(missing.UserId, job.UserId);
        Assert.Equal(OutboxStatus.Pending, job.Status);
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.EventId == deleted.Id));
    }

    [Fact]
    public async Task Maintenance_only_removes_expired_messages_and_old_completed_jobs()
    {
        await using var db = new TraceDbContext(fixture.Options);
        var conversation = new AiConversation { Id = Guid.NewGuid(), UserId = 982011, Title = "保留", CreatedAt = Now, UpdatedAt = Now };
        conversation.Messages.Add(new AiMessage { UserId = 982011, Content = "已过期", ExpiresAt = Now.AddSeconds(-1), CreatedAt = Now });
        conversation.Messages.Add(new AiMessage { UserId = 982011, Content = "仍有效", ExpiresAt = Now.AddDays(1), CreatedAt = Now });
        conversation.Messages.Add(new AiMessage { UserId = 982011, Content = "永久", CreatedAt = Now });
        var old = Job(10); old.Status = OutboxStatus.Completed; old.CompletedAt = Now.AddDays(-31);
        var recent = Job(10); recent.Status = OutboxStatus.Completed; recent.CompletedAt = Now;
        var pending = Job(10); pending.CreatedAt = Now.AddDays(-31);
        db.AiConversations.Add(conversation);
        db.OutboxMessages.AddRange(old, recent, pending);
        await db.SaveChangesAsync();
        await new AnalysisJobRepository(db).PruneAsync(Now, default);
        Assert.Equal(2, await db.AiMessages.CountAsync(x => x.ConversationId == conversation.Id));
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.Id == old.Id));
        Assert.Equal(2, await db.OutboxMessages.CountAsync(x => x.Id == recent.Id || x.Id == pending.Id));
    }

    private static OutboxMessage Job(int priority) => new()
    {
        Id = Guid.NewGuid(),
        UserId = 982001,
        MessageType = "event.analyze",
        Priority = priority,
        Status = OutboxStatus.Pending,
        AvailableAt = Now,
        CreatedAt = Now,
    };

    private static Event Record(long userId)
    {
        var evt = Event.Create(userId, EventKind.Trace, "补算", "测试", Now, null, "UTC", Guid.NewGuid().ToString("N"), Now);
        evt.SourceRevisions.Add(SourceRevision.Create(0, 1, evt.Title, evt.RawContent, Now, null, Now));
        return evt;
    }
}
