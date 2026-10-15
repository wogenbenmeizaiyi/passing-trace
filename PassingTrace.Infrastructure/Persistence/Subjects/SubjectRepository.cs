using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Events;
using PassingTrace.Core.Subjects;

namespace PassingTrace.Infrastructure.Persistence.Subjects;

public sealed class SubjectRepository(TraceDbContext db) : ISubjectRepository, ISubjectMediaQueries
{
    public async Task<T> ExecuteAsync<T>(long userId, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await LockAsync(userId, ct);
            return await action(ct);
        }
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await LockAsync(userId, ct);
                var result = await action(ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                throw;
            }
        });
    }

    private async Task LockAsync(long userId, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({'s' + userId.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 0))", ct);
        // Queries performed before acquiring the lock may have observed an older graph.
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State == EntityState.Unchanged &&
            (x.Entity is Subject or SubjectRelation or SubjectEntry or SubjectTimelineReference or SubjectMilestone)).ToArray())
            await entry.ReloadAsync(ct);
    }

    public async Task<IReadOnlyList<Subject>> SubjectsAsync(long userId, bool includeDeleted, CancellationToken ct) =>
        await db.Subjects.Where(x => x.UserId == userId && (includeDeleted || x.DeletedAt == null)).OrderByDescending(x => x.IsSelf).ThenBy(x => x.Name).ToArrayAsync(ct);
    public Task<Subject?> SelfAsync(long userId, CancellationToken ct) => db.Subjects.SingleOrDefaultAsync(x => x.UserId == userId && x.IsSelf, ct);
    public Task<Subject?> SubjectAsync(long userId, Guid id, bool includeDeleted, CancellationToken ct) =>
        db.Subjects.FirstOrDefaultAsync(x => x.UserId == userId && x.Id == id && (includeDeleted || x.DeletedAt == null), ct);
    public async Task<IReadOnlyList<SubjectRelation>> RelationsAsync(long userId, CancellationToken ct) =>
        await db.SubjectRelations.Where(x => x.UserId == userId).ToArrayAsync(ct);
    public Task<SubjectRelation?> RelationAsync(long userId, Guid id, CancellationToken ct) => db.SubjectRelations.FirstOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public async Task<IReadOnlyList<SubjectEntry>> EntriesAsync(long userId, Guid? subjectId, CancellationToken ct) =>
        await db.SubjectEntries.Where(x => x.UserId == userId && (subjectId == null || x.SubjectId == subjectId)).ToArrayAsync(ct);
    public Task<SubjectEntry?> EntryAsync(long userId, Guid id, CancellationToken ct) => db.SubjectEntries.FirstOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);
    public async Task<IReadOnlyList<SubjectEntry>> TimelineEntriesAsync(long userId, Guid subjectId, CancellationToken ct) =>
        await db.SubjectEntries.AsNoTracking().Where(x => x.UserId == userId && (x.SubjectId == subjectId ||
            db.SubjectTimelineReferences.Any(r => r.UserId == userId && r.SubjectId == subjectId && r.EntryId == x.Id && r.RemovedAt == null))).ToArrayAsync(ct);
    public async Task<IReadOnlyList<SubjectTimelineReference>> ReferencesAsync(long userId, CancellationToken ct) =>
        await db.SubjectTimelineReferences.Where(x => x.UserId == userId).ToArrayAsync(ct);
    public async Task<IReadOnlyList<Event>> EventsAsync(long userId, Guid subjectId, bool isSelf, CancellationToken ct) =>
        await db.Events.AsNoTracking().Include(x => x.MediaAssets).Where(x => x.UserId == userId && ((isSelf && x.DeletedAt == null) ||
            db.SubjectTimelineReferences.Any(r => r.UserId == userId && r.SubjectId == subjectId && r.EventId == x.Id && r.RemovedAt == null))).ToArrayAsync(ct);
    public async Task<IReadOnlyList<SubjectMilestone>> MilestonesAsync(long userId, Guid subjectId, CancellationToken ct) =>
        await db.SubjectMilestones.Where(x => x.UserId == userId && x.SubjectId == subjectId).OrderBy(x => x.EffectiveAt).ThenBy(x => x.CreatedAt).ToArrayAsync(ct);
    public async Task<IReadOnlyList<SubjectHistory>> HistoryAsync(long userId, string type, Guid id, CancellationToken ct) =>
        await db.SubjectHistories.AsNoTracking().Where(x => x.UserId == userId && x.TargetType == type && x.TargetId == id).OrderByDescending(x => x.Revision).ToArrayAsync(ct);
    public Task<Subject?> FindSubjectKeyAsync(long userId, string key, CancellationToken ct) => db.Subjects.FirstOrDefaultAsync(x => x.UserId == userId && x.IdempotencyKey == key, ct);
    public Task<SubjectEntry?> FindEntryKeyAsync(long userId, string key, CancellationToken ct) => db.SubjectEntries.FirstOrDefaultAsync(x => x.UserId == userId && x.IdempotencyKey == key, ct);
    public void Add(Subject value) => db.Subjects.Add(value);
    public void Add(SubjectRelation value) => db.SubjectRelations.Add(value);
    public void Add(SubjectEntry value) => db.SubjectEntries.Add(value);
    public void Add(SubjectTimelineReference value) => db.SubjectTimelineReferences.Add(value);
    public void Add(SubjectMilestone value) => db.SubjectMilestones.Add(value);
    public void Add(SubjectHistory value) => db.SubjectHistories.Add(value);
    public void Add(SubjectMediaReference value) => db.SubjectMediaReferences.Add(value);
    public Task<bool> HasReferenceAsync(long userId, Guid mediaId, CancellationToken ct) =>
        db.SubjectMediaReferences.AnyAsync(x => x.UserId == userId && x.MediaId == mediaId, ct);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyException("内容已更新，请刷新后重试。"); }
    }
}
