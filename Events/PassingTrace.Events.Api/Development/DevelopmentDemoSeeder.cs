using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Storylines;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Development;

public sealed class DevelopmentDemoOptions
{
    public const string SectionName = "DevelopmentDemo";
    public bool Enabled { get; set; }
    public string Username { get; set; } = "dev";
}

public sealed record DevelopmentDemoResult(
    int CreatedRecords, int ExistingRecords, int CreatedStorylines,
    int ExistingStorylines, int SkippedStorylines);

/// <summary>
/// Only fills missing, versioned local fixtures. Existing fixtures (including soft-deleted
/// ones) are intentionally never reset. Each record and storyline uses normal domain services.
/// </summary>
public sealed class DevelopmentDemoSeeder(
    TraceDbContext db,
    EventService events,
    StorylineService storylines,
    TimeProvider clock,
    IHostEnvironment environment,
    IOptions<DevelopmentDemoOptions> options,
    SocialDemoSeeder? social = null)
{
    // AppHost runs one local API. Serialize concurrent startup/manual requests within it;
    // persistent user-scoped idempotency keys also protect retries across process restarts.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static string Key(string kind, string key) =>
        $"development-demo-{DevelopmentDemoCatalog.Version}-{kind}-{key}";

    public async Task<DevelopmentDemoResult> SeedAsync(long userId, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !options.Value.Enabled || userId <= 0)
            throw new InvalidOperationException("示例数据仅允许在本地开发环境准备。");

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var createdRecords = 0;
            var existingRecords = 0;
            var createdStorylines = 0;
            var existingStorylines = 0;
            var records = new Dictionary<string, Event>(StringComparer.Ordinal);

            foreach (var fixture in DevelopmentDemoCatalog.Events(clock.GetUtcNow()))
            {
                var key = Key("event", fixture.Key);
                // Do not call EventService's content-comparison idempotency path for an
                // existing fixture: users may have edited it since the first startup.
                var record = await db.Events.SingleOrDefaultAsync(
                    x => x.UserId == userId && x.IdempotencyKey == key, cancellationToken);
                if (record is not null)
                {
                    existingRecords++;
                }
                else
                {
                    record = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                    {
                        db.ChangeTracker.Clear();
                        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                        // A connection failure after commit may replay the delegate.
                        var committed = await db.Events.SingleOrDefaultAsync(
                            x => x.UserId == userId && x.IdempotencyKey == key, cancellationToken);
                        if (committed is not null) return committed;

                        var input = fixture.Request;
                        var added = await events.CreateAsync(new CreateEventCommand(
                            userId, input.Kind, input.Title, input.RawContent,
                            input.HappenedAt, input.PlannedAt, input.Timezone ?? "Asia/Shanghai",
                            key, input.MediaIds, input.Classification, input.Locations), cancellationToken);
                        if (fixture.PlanStatus is { } status && added.EventKind == EventKind.Plan)
                        {
                            added.Status = status;
                            added.CompletedAt = status == EventStatus.Completed ? added.PlannedAt : null;
                            await db.SaveChangesAsync(cancellationToken);
                        }
                        await transaction.CommitAsync(cancellationToken);
                        return added;
                    });
                    createdRecords++;
                }
                records.Add(fixture.Key, record);
            }

            var fixtures = DevelopmentDemoCatalog.Storylines(records);
            foreach (var fixture in fixtures)
            {
                var key = Key("storyline", fixture.Key);
                if (await db.Storylines.AnyAsync(
                    x => x.UserId == userId && x.CreationIdempotencyKey == key, cancellationToken))
                {
                    existingStorylines++;
                    continue;
                }

                await storylines.CreateAsync(userId, fixture.Request, key, cancellationToken);
                createdStorylines++;
            }

            if (social is not null) await social.SeedAsync(userId, cancellationToken);
            return new DevelopmentDemoResult(createdRecords, existingRecords, createdStorylines,
                existingStorylines, DevelopmentDemoCatalog.StorylineCount - fixtures.Count);
        }
        finally
        {
            Gate.Release();
        }
    }
}
