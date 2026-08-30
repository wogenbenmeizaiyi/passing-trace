using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Ai;

public interface IAnalysisOutbox
{
    void EnqueueEvent(Event evt, int sourceRevision, DateTimeOffset now, int priority = 100, string messageType = "event.analyze");
    void EnqueueMedia(long userId, Guid mediaAssetId, DateTimeOffset now, int priority = 100);
    Task IncrementWatermarkAsync(long userId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>只向当前 EF 工作单元追加任务，提交由调用方统一完成。</summary>
public sealed class AnalysisOutbox(TraceDbContext dbContext) : IAnalysisOutbox
{
    public void EnqueueEvent(Event evt, int sourceRevision, DateTimeOffset now, int priority = 100, string messageType = "event.analyze")
    {
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            UserId = evt.UserId,
            MessageType = messageType,
            Event = evt,
            SourceRevision = sourceRevision,
            Priority = priority,
            Status = OutboxStatus.Pending,
            AvailableAt = now,
            CreatedAt = now,
        });
    }

    public void EnqueueMedia(long userId, Guid mediaAssetId, DateTimeOffset now, int priority = 100)
    {
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MessageType = "media.process",
            MediaAssetId = mediaAssetId,
            Priority = priority,
            Status = OutboxStatus.Pending,
            AvailableAt = now,
            CreatedAt = now,
        });
    }

    public async Task IncrementWatermarkAsync(long userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var watermark = await dbContext.UserDataWatermarks.FindAsync([userId], cancellationToken);
        if (watermark is null)
        {
            dbContext.UserDataWatermarks.Add(new UserDataWatermark
            {
                UserId = userId,
                Version = 1,
                UpdatedAt = now,
            });
            return;
        }

        watermark.Version++;
        watermark.UpdatedAt = now;
    }
}
