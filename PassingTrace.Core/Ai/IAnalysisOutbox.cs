using PassingTrace.Core.Events;

namespace PassingTrace.Core.Ai;

public interface IAnalysisOutbox
{
    void EnqueueEvent(Event evt, int sourceRevision, DateTimeOffset now, int priority = 100, string messageType = "event.analyze");
    void EnqueueMedia(long userId, Guid mediaAssetId, DateTimeOffset now, int priority = 100);
    Task IncrementWatermarkAsync(long userId, DateTimeOffset now, CancellationToken cancellationToken);
    void EnqueueStoryline(long userId, Guid storylineId, int revision, DateTimeOffset now,
        string messageType = "storyline.index", int priority = 110);
}
