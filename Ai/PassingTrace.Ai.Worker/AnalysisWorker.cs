using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Media;
using PassingTrace.Events.Api.Media;
using PassingTrace.Infrastructure;

namespace PassingTrace.Ai.Worker;

public sealed class AnalysisWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AnalysisWorker> logger) : BackgroundService
{
    private readonly string _leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private DateTimeOffset _nextMaintenanceAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await BackfillAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var id = await ClaimAsync(stoppingToken);
                if (id is null)
                {
                    if (DateTimeOffset.UtcNow >= _nextMaintenanceAt)
                    {
                        await MaintainAsync(stoppingToken);
                        _nextMaintenanceAt = DateTimeOffset.UtcNow.AddHours(1);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                await ProcessAsync(id.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "AI Worker 主循环发生错误，将继续重试。");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task MaintainAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TraceDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-24);
        var orphans = await db.MediaAssets
            .Include(x => x.EventLinks)
            .Include(x => x.RevisionLinks)
            .Where(x => x.DeletedAt == null && x.CreatedAt < cutoff &&
                !x.EventLinks.Any() && !x.RevisionLinks.Any())
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var asset in orphans)
        {
            try
            {
                if (asset.UploadMode == MediaUploadMode.Multipart &&
                    asset.Status == MediaAssetStatus.PendingUpload &&
                    !string.IsNullOrWhiteSpace(asset.MultipartUploadId))
                {
                    await storage.AbortMultipartUploadAsync(asset.ObjectKey, asset.MultipartUploadId, cancellationToken);
                }
                else
                {
                    await storage.DeleteAsync(asset.ObjectKey, cancellationToken);
                }
                if (!string.IsNullOrWhiteSpace(asset.AiObjectKey))
                    await storage.DeleteAsync(asset.AiObjectKey, cancellationToken);
                if (!string.IsNullOrWhiteSpace(asset.ThumbnailObjectKey))
                    await storage.DeleteAsync(asset.ThumbnailObjectKey, cancellationToken);
                asset.Status = MediaAssetStatus.Deleted;
                asset.DeletedAt = now;
                asset.UpdatedAt = now;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "清理孤立附件 {MediaId} 失败，下轮重试。", asset.Id);
            }
        }

        await db.AiMessages.Where(x => x.ExpiresAt != null && x.ExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);
        await db.OutboxMessages.Where(x => x.Status == OutboxStatus.Completed && x.CompletedAt < now.AddDays(-30))
            .ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TraceDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<Guid?>(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var message = await db.OutboxMessages
                .FromSqlInterpolated($$"""
                    SELECT * FROM outbox_message
                    WHERE status = 1
                      AND available_at <= {{now}}
                      AND (lease_expires_at IS NULL OR lease_expires_at < {{now}})
                    ORDER BY priority DESC, created_at
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                    """)
                .FirstOrDefaultAsync(cancellationToken);
            if (message is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return (Guid?)null;
            }

            message.Status = OutboxStatus.Processing;
            message.LeaseOwner = _leaseOwner;
            message.LeaseExpiresAt = now.AddMinutes(10);
            message.Attempts++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return message.Id;
        });
    }

    private async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TraceDbContext>();
        var message = await db.OutboxMessages.FirstAsync(x => x.Id == id, cancellationToken);
        try
        {
            var pipeline = scope.ServiceProvider.GetRequiredService<SemanticPipeline>();
            switch (message.MessageType)
            {
                case "media.process":
                    await pipeline.ProcessMediaAsync(message, cancellationToken);
                    break;
                case "event.analyze":
                    await pipeline.AnalyzeEventAsync(message, cancellationToken);
                    break;
                case "event.deleted":
                    await pipeline.RemoveEventFromSearchAsync(message, cancellationToken);
                    break;
                case "storyline.index":
                    await pipeline.IndexStorylineAsync(message, cancellationToken);
                    break;
                case "storyline.removed":
                    await pipeline.RemoveStorylineFromSearchAsync(message, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"未知 Outbox 类型：{message.MessageType}");
            }

            message.Status = OutboxStatus.Completed;
            message.CompletedAt = DateTimeOffset.UtcNow;
            message.LeaseOwner = null;
            message.LeaseExpiresAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "处理 Outbox {OutboxId} ({MessageType}) 失败。", id, message.MessageType);
            message.LastError = exception.Message.Length > 4096 ? exception.Message[..4096] : exception.Message;
            message.LeaseOwner = null;
            message.LeaseExpiresAt = null;
            if (message.Attempts >= 5)
            {
                message.Status = OutboxStatus.DeadLetter;
            }
            else
            {
                message.Status = OutboxStatus.Pending;
                message.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(3, message.Attempts));
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task BackfillAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TraceDbContext>();
            var existing = await db.OutboxMessages
                .Where(x => x.EventId != null && x.MessageType == "event.analyze")
                .Select(x => new { x.EventId, x.SourceRevision })
                .ToListAsync(cancellationToken);
            var keys = existing.Select(x => (x.EventId!.Value, x.SourceRevision!.Value)).ToHashSet();
            var events = await db.Events.AsNoTracking().Where(x => x.DeletedAt == null).ToListAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var evt in events.Where(x => !keys.Contains((x.Id, x.CurrentSourceRevision))))
            {
                db.OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    UserId = evt.UserId,
                    MessageType = "event.analyze",
                    EventId = evt.Id,
                    SourceRevision = evt.CurrentSourceRevision,
                    Priority = 10,
                    Status = OutboxStatus.Pending,
                    AvailableAt = now,
                    CreatedAt = now,
                });
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "历史记录补算排队失败；主循环启动后仍会处理新任务。");
        }
    }
}
