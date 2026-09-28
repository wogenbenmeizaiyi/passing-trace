using PassingTrace.Core.Ai;
using PassingTrace.Core.Media;
using PassingTrace.Events.Api.Media;

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
        var repository = scope.ServiceProvider.GetRequiredService<IAnalysisJobRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-24);
        var orphans = await repository.FindOrphanMediaAsync(cutoff, 100, cancellationToken);
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

        await repository.PruneAsync(now, cancellationToken);
    }

    private async Task<Guid?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnalysisJobRepository>();
        return await repository.ClaimAsync(_leaseOwner, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10), cancellationToken);
    }

    private async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnalysisJobRepository>();
        var message = await repository.FindAsync(id, cancellationToken);
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
            await repository.SaveChangesAsync(cancellationToken);
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
            await repository.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task BackfillAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAnalysisJobRepository>();
            await repository.BackfillAsync(DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "历史记录补算排队失败；主循环启动后仍会处理新任务。");
        }
    }
}
