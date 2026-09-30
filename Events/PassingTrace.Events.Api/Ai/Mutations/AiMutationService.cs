using PassingTrace.Events.Api.Ai.Evidence;
using PassingTrace.Events.Api.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Storylines;

namespace PassingTrace.Events.Api.Ai.Mutations;

/// <summary>Application orchestration. Transactions, locks and durable state live behind a Core port.</summary>
public sealed class AiMutationService(
    IAiMutationRepository repository, IEventRepository events, EventService eventService,
    StorylineService storylineService, IAiConversationRepository conversations,
    CurrentUserContext currentUser, TimeProvider clock)
{
    public const string ReceiptPromptVersion = "mutation-receipt-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AiMutationResult> WriteAsync(Guid conversationId, long messageId, string operation,
        object arguments, Func<string, CancellationToken, Task<IReadOnlyList<AiMutationTarget>>> write, CancellationToken ct)
    {
        var key = OperationKey(conversationId, messageId, operation, arguments);
        return await repository.ExecuteAsync(key, async token =>
        {
            await RequireSourceAsync(conversationId, messageId, token);
            var prior = await repository.FindByKeyAsync(currentUser.UserId, key, token);
            if (prior is not null) return ReadResult(prior);
            var targets = await write(key, token);
            var entry = NewOperation(conversationId, messageId, key, operation, targets[0]);
            entry.State = AiMutationState.Succeeded;
            entry.ResolvedAt = clock.GetUtcNow();
            repository.Add(entry);
            var result = await SaveReceiptAsync(entry, targets, operation.StartsWith("Create", StringComparison.Ordinal) ? "已创建" : "已更新", token);
            return result;
        }, ct);
    }

    public async Task<AiApprovalRequest> RequestDeleteAsync(Guid conversationId, long messageId, string type, string id, CancellationToken ct)
    {
        var key = OperationKey(conversationId, messageId, "RequestDelete", new { type, id });
        return await repository.ExecuteAsync(key, async token =>
        {
            await RequireSourceAsync(conversationId, messageId, token);
            var prior = await repository.FindByKeyAsync(currentUser.UserId, key, token);
            if (prior is not null && (prior.State != AiMutationState.Pending || prior.ExpiresAt <= clock.GetUtcNow()))
                throw new DomainValidationException("这次删除请求已处理，请重新提出删除请求。");
            if (prior is not null) return Approval(prior);
            var (target, version) = await ReadTargetAsync(type, id, token);
            var entry = NewOperation(conversationId, messageId, key, "Delete", target);
            entry.ExpectedVersion = version;
            entry.State = AiMutationState.Pending;
            entry.ExpiresAt = clock.GetUtcNow().AddMinutes(15);
            repository.Add(entry);
            return Approval(entry);
        }, ct);
    }

    public async Task<IReadOnlyList<AiApprovalRequest>> ListApprovalsAsync(Guid conversationId, CancellationToken ct)
    {
        _ = await conversations.ReadHeaderAsync(currentUser.UserId, conversationId, ct)
            ?? throw new KeyNotFoundException("对话不存在。");
        return (await repository.ListPendingAsync(currentUser.UserId, conversationId, clock.GetUtcNow(), ct)).Select(Approval).ToArray();
    }

    public Task<AiApprovalDecisionResponse> DecideAsync(Guid conversationId, Guid approvalId, string decision, CancellationToken ct)
    {
        if (decision is not ("confirm" or "cancel")) throw new DomainValidationException("请选择确定或取消。");
        return repository.ExecuteAsync($"approval:{approvalId:N}", async token =>
        {
            var entry = await repository.FindAsync(currentUser.UserId, conversationId, approvalId, token)
                ?? throw new KeyNotFoundException("删除请求不存在。");
            if (entry.Operation != "Delete") throw new KeyNotFoundException("删除请求不存在。");
            if (entry.State != AiMutationState.Pending)
                return new AiApprovalDecisionResponse(entry.Id, entry.State.ToString(), ReadResult(entry));
            var now = clock.GetUtcNow();
            var description = "已取消删除";
            entry.State = AiMutationState.Cancelled;
            if (entry.ExpiresAt <= now)
            {
                entry.State = AiMutationState.Expired;
                description = "删除授权已过期，请重新申请";
            }
            else if (decision == "confirm")
            {
                try
                {
                    await repository.LockTargetAsync(currentUser.UserId, entry.TargetType, entry.TargetId!, token);
                    if (entry.ExpiresAt <= clock.GetUtcNow())
                    {
                        entry.State = AiMutationState.Expired;
                        description = "删除授权已过期，请重新申请";
                    }
                    else
                    {
                        var (_, version) = await ReadTargetAsync(entry.TargetType, entry.TargetId!, token);
                        if (version != entry.ExpectedVersion) throw new ConcurrencyException("内容已更新，请重新申请删除。");
                        if (entry.TargetType == "Storyline")
                            await storylineService.DeleteAsync(currentUser.UserId, Guid.Parse(entry.TargetId!), version, token);
                        else
                            await eventService.SoftDeleteAsync(currentUser.UserId, long.Parse(entry.TargetId!, CultureInfo.InvariantCulture), version, token);
                        entry.State = AiMutationState.Succeeded;
                        description = "已删除";
                    }
                }
                catch (Exception exception) when (exception is ConcurrencyException or EventNotFoundException or KeyNotFoundException)
                {
                    // Version checks happen before staging deletion changes. A changed target requires a new grant.
                    entry.State = AiMutationState.Conflict;
                    description = "内容已变更或已删除，请重新核实后申请";
                }
            }
            entry.ResolvedAt = clock.GetUtcNow();
            var target = new AiMutationTarget(entry.TargetType, entry.TargetId!, entry.Title, 0);
            var result = await SaveReceiptAsync(entry, [target], description, token, linkTargets: false);
            return new AiApprovalDecisionResponse(entry.Id, entry.State.ToString(), result);
        }, ct);
    }

    public async Task<Event> ReadRecordAsync(long id, CancellationToken ct)
    {
        var record = await events.FindAsync(currentUser.UserId, id, ct);
        if (record is null || record.DeletedAt is not null) throw new EventNotFoundException(currentUser.UserId, id);
        return record;
    }

    private async Task<(AiMutationTarget Target, uint Version)> ReadTargetAsync(string type, string id, CancellationToken ct)
    {
        if (type == "Storyline")
        {
            var story = await storylineService.GetAsync(currentUser.UserId, Guid.Parse(id), null, ct);
            return (new(type, id, story.Title, story.Revision), story.Version);
        }
        var evt = await ReadRecordAsync(long.Parse(id, CultureInfo.InvariantCulture), ct);
        return (RecordTarget(evt), evt.RowVersion);
    }

    public static AiMutationTarget RecordTarget(Event evt) =>
        new(evt.EventKind == EventKind.Plan ? "Plan" : "Record", evt.Id.ToString(CultureInfo.InvariantCulture), evt.Title ?? "无标题记录", evt.CurrentSourceRevision);

    private async Task RequireSourceAsync(Guid conversationId, long messageId, CancellationToken ct)
    {
        if (!await repository.HasSourceMessageAsync(currentUser.UserId, conversationId, messageId, ct))
            throw new KeyNotFoundException("对话消息不存在。");
    }

    private AiMutationOperation NewOperation(Guid conversationId, long messageId, string key, string operation, AiMutationTarget target) => new()
    {
        Id = Guid.NewGuid(),
        UserId = currentUser.UserId,
        ConversationId = conversationId,
        SourceMessageId = messageId,
        OperationKey = key,
        Operation = operation,
        TargetType = target.Type,
        TargetId = target.Id,
        Title = target.Title,
        CreatedAt = clock.GetUtcNow(),
    };

    private async Task<AiMutationResult> SaveReceiptAsync(AiMutationOperation entry, IReadOnlyList<AiMutationTarget> targets,
        string description, CancellationToken ct, bool linkTargets = true)
    {
        var records = targets.Where(x => x.Type != "Storyline" && linkTargets)
            .Select(x => new RecordEvidence(long.Parse(x.Id, CultureInfo.InvariantCulture), x.Revision, x.Title, "", null, null, clock.GetUtcNow(), 0)).ToArray();
        var stories = targets.Where(x => x.Type == "Storyline" && linkTargets)
            .Select(x => new StorylineEvidence(Guid.Parse(x.Id), x.Revision, x.Title, "", "", "", null, null, [], [], 0)).ToArray();
        var evidence = new EvidenceBundle(records, [], Storylines: stories);
        var conversation = await conversations.FindAsync(currentUser.UserId, entry.ConversationId, ct)
            ?? throw new KeyNotFoundException("对话不存在。");
        conversation.UpdatedAt = clock.GetUtcNow();
        var text = description + "：" + string.Join("、", targets.Select(x => linkTargets
            ? x.Type == "Storyline" ? $"[Storyline #{x.Id}]" : $"[Event #{x.Id}]" : x.Title));
        var message = new AiMessage
        {
            ConversationId = entry.ConversationId,
            UserId = currentUser.UserId,
            Role = AiMessageRole.Assistant,
            Content = text,
            EvidenceSnapshotJson = JsonSerializer.Serialize(evidence, Json),
            PromptVersion = ReceiptPromptVersion,
            CreatedAt = clock.GetUtcNow(),
            ExpiresAt = clock.GetUtcNow().AddDays(30),
            DataWatermark = await conversations.ReadWatermarkAsync(currentUser.UserId, ct),
        };
        repository.Add(message);
        await repository.SaveChangesAsync(ct);
        var result = new AiMutationResult(entry.Id, entry.Operation, entry.State.ToString(), targets,
            new(message.Id, "Assistant", text, message.CreatedAt, evidence));
        entry.ResultJson = JsonSerializer.Serialize(result, Json);
        return result;
    }

    private string OperationKey(Guid conversationId, long messageId, string operation, object arguments) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{currentUser.UserId}:{conversationId:N}:{messageId}:{operation}:{JsonSerializer.Serialize(arguments, Json)}"))).ToLowerInvariant();

    private static AiMutationResult ReadResult(AiMutationOperation entry) =>
        JsonSerializer.Deserialize<AiMutationResult>(entry.ResultJson!, Json) ?? throw new InvalidOperationException("缺少操作回执。");

    private static AiApprovalRequest Approval(AiMutationOperation entry) => new(entry.Id, entry.ConversationId,
        entry.TargetType, entry.TargetId!, entry.Title,
        entry.TargetType == "Storyline" ? "删除故事线；其中的记录和计划会保留。" : "删除这条记录或计划。",
        entry.ExpiresAt!.Value);
}
