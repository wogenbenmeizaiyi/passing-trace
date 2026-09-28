using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Core.Storylines;
using Pgvector;

namespace PassingTrace.Infrastructure.Persistence.Ai;

public sealed class SemanticPipelineRepository(TraceDbContext db) : ISemanticPipelineRepository
{
    public Task<StorylineSearchIndex?> FindCurrentStorylineIndexAsync(long userId, Guid id, int revision, CancellationToken cancellationToken) =>
        db.StorylineSearchIndexes.FirstOrDefaultAsync(x => x.UserId == userId && x.StorylineId == id && x.Revision == revision && x.IsCurrent &&
            db.Storylines.Any(s => s.Id == id && s.UserId == userId && s.DeletedAt == null && s.CurrentRevision == revision), cancellationToken);

    public async Task DisableStorylineIndexesAsync(long userId, Guid id, CancellationToken cancellationToken)
    {
        var indexes = await db.StorylineSearchIndexes.Where(x => x.StorylineId == id && x.UserId == userId).ToListAsync(cancellationToken);
        foreach (var index in indexes) index.IsCurrent = false;
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<MediaAsset?> FindMediaAsync(long userId, Guid id, CancellationToken cancellationToken) =>
        db.MediaAssets.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

    public Task<Event?> FindEventAsync(long userId, long id, CancellationToken cancellationToken) =>
        db.Events.Include(x => x.SourceRevisions).ThenInclude(x => x.MediaAssets).ThenInclude(x => x.MediaAsset)
            .Include(x => x.SourceRevisions).ThenInclude(x => x.Labels)
            .Include(x => x.SourceRevisions).ThenInclude(x => x.Locations)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);

    public Task ReloadEventAsync(Event evt, CancellationToken cancellationToken) => db.Entry(evt).ReloadAsync(cancellationToken);

    public Task<bool> HasCompletedRunAsync(long userId, long eventId, int revision, string pipelineVersion, CancellationToken cancellationToken) =>
        db.EventSemanticRuns.AnyAsync(x => x.UserId == userId && x.EventId == eventId && x.SourceRevision == revision &&
            x.PipelineVersion == pipelineVersion && x.Status == SemanticRunStatus.Completed, cancellationToken);

    public async Task<IReadOnlyList<EventLabelIndex>> ReadCurrentLabelsAsync(long userId, long eventId, int revision, CancellationToken cancellationToken) =>
        await db.EventLabelIndexes.Where(x => x.UserId == userId && x.EventId == eventId && x.SourceRevision == revision && x.IsCurrent).ToListAsync(cancellationToken);

    public Task DisableOlderLabelsAsync(long userId, long eventId, int currentRevision, CancellationToken cancellationToken) =>
        db.EventLabelIndexes.Where(x => x.UserId == userId && x.EventId == eventId && x.IsCurrent && x.SourceRevision != currentRevision)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsCurrent, false), cancellationToken);

    public Task<EventSearchIndex?> FindEventIndexAsync(long userId, long eventId, int revision, CancellationToken cancellationToken) =>
        db.EventSearchIndexes.FirstOrDefaultAsync(x => x.UserId == userId && x.EventId == eventId && x.SourceRevision == revision, cancellationToken);

    public Task DisableOlderEventIndexesAsync(long userId, long eventId, int currentRevision, CancellationToken cancellationToken) =>
        db.EventSearchIndexes.Where(x => x.UserId == userId && x.EventId == eventId && x.IsCurrent && x.SourceRevision != currentRevision)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsCurrent, false), cancellationToken);

    public Task DisableEventIndexesAsync(long userId, long eventId, CancellationToken cancellationToken) =>
        db.EventSearchIndexes.Where(x => x.UserId == userId && x.EventId == eventId && x.IsCurrent)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsCurrent, false), cancellationToken);

    public async Task MarkStaleAsync(long userId, long eventId, int revision, CancellationToken cancellationToken)
    {
        await db.EventSemanticRuns.Where(x => x.UserId == userId && x.EventId == eventId && x.SourceRevision == revision &&
            (x.Status == SemanticRunStatus.Pending || x.Status == SemanticRunStatus.Running || x.Status == SemanticRunStatus.Completed))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, SemanticRunStatus.Stale), cancellationToken);
        await db.EventSearchIndexes.Where(x => x.UserId == userId && x.EventId == eventId && x.SourceRevision == revision)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.IsCurrent, false), cancellationToken);
    }

    public Task<UserPlace?> FindPlaceAsync(long userId, string canonicalKey, CancellationToken cancellationToken) =>
        db.UserPlaces.FirstOrDefaultAsync(x => x.UserId == userId && x.CanonicalKey == canonicalKey, cancellationToken);

    public Task<int> CountPlaceVisitsAsync(long userId, EventLocation location, CancellationToken cancellationToken) =>
        (from candidate in db.EventLocations
         join evt in db.Events on candidate.EventId equals evt.Id
         where candidate.UserId == userId && evt.UserId == userId && evt.DeletedAt == null && candidate.UserConfirmed && candidate.SourceRevision == evt.CurrentSourceRevision &&
             ((location.ProviderPoiId != null && candidate.ProviderPoiId == location.ProviderPoiId) ||
              (location.ProviderPoiId == null && candidate.Name == location.Name && candidate.AdCode == location.AdCode))
         select candidate.Id).CountAsync(cancellationToken);

    public Task<UserMemory?> FindMemoryAsync(long userId, string fingerprint, CancellationToken cancellationToken) =>
        db.UserMemories.Include(x => x.Evidence).FirstOrDefaultAsync(x => x.UserId == userId && x.Fingerprint == fingerprint, cancellationToken);

    public void Add(EventSemanticRun run) => db.EventSemanticRuns.Add(run);
    public void Add(EventLabelIndex label) => db.EventLabelIndexes.Add(label);
    public void Add(EventSearchIndex index) => db.EventSearchIndexes.Add(index);
    public void Add(UserPlace place) => db.UserPlaces.Add(place);
    public void Add(UserMemory memory) => db.UserMemories.Add(memory);
    public void SetEmbedding(StorylineSearchIndex index, float[] embedding) => SetVector(index, embedding);
    public void SetEmbedding(EventSearchIndex index, float[] embedding) => SetVector(index, embedding);
    public void SetEmbedding(UserPlace place, float[] embedding) => SetVector(place, embedding);
    public void SetEmbedding(UserMemory memory, float[] embedding) => SetVector(memory, embedding);
    private void SetVector<TEntity>(TEntity entity, float[] embedding) where TEntity : class =>
        db.Entry(entity).Property<Vector?>("Embedding").CurrentValue = new Vector(embedding);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
