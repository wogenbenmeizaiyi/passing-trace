using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Core.Storylines;

namespace PassingTrace.Core.Ai;

/// <summary>Tracked data and persistence operations used by the semantic pipeline.</summary>
public interface ISemanticPipelineRepository
{
    Task<StorylineSearchIndex?> FindCurrentStorylineIndexAsync(long userId, Guid id, int revision, CancellationToken cancellationToken);
    Task DisableStorylineIndexesAsync(long userId, Guid id, CancellationToken cancellationToken);
    Task<MediaAsset?> FindMediaAsync(long userId, Guid id, CancellationToken cancellationToken);
    Task<Event?> FindEventAsync(long userId, long id, CancellationToken cancellationToken);
    Task ReloadEventAsync(Event evt, CancellationToken cancellationToken);
    Task<bool> HasCompletedRunAsync(long userId, long eventId, int revision, string pipelineVersion, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventLabelIndex>> ReadCurrentLabelsAsync(long userId, long eventId, int revision, CancellationToken cancellationToken);
    Task DisableOlderLabelsAsync(long userId, long eventId, int currentRevision, CancellationToken cancellationToken);
    Task<EventSearchIndex?> FindEventIndexAsync(long userId, long eventId, int revision, CancellationToken cancellationToken);
    Task DisableOlderEventIndexesAsync(long userId, long eventId, int currentRevision, CancellationToken cancellationToken);
    Task DisableEventIndexesAsync(long userId, long eventId, CancellationToken cancellationToken);
    Task MarkStaleAsync(long userId, long eventId, int revision, CancellationToken cancellationToken);
    Task<UserPlace?> FindPlaceAsync(long userId, string canonicalKey, CancellationToken cancellationToken);
    Task<int> CountPlaceVisitsAsync(long userId, EventLocation location, CancellationToken cancellationToken);
    Task<UserMemory?> FindMemoryAsync(long userId, string fingerprint, CancellationToken cancellationToken);
    void Add(EventSemanticRun run);
    void Add(EventLabelIndex label);
    void Add(EventSearchIndex index);
    void Add(UserPlace place);
    void Add(UserMemory memory);
    void SetEmbedding(StorylineSearchIndex index, float[] embedding);
    void SetEmbedding(EventSearchIndex index, float[] embedding);
    void SetEmbedding(UserPlace place, float[] embedding);
    void SetEmbedding(UserMemory memory, float[] embedding);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
