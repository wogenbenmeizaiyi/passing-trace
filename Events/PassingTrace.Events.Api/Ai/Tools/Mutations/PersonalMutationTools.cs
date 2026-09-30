using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Ai.Assistant;
using PassingTrace.Events.Api.Ai.Assistant.Context;
using PassingTrace.Events.Api.Ai.Evidence;
using PassingTrace.Events.Api.Ai.Mutations;
using PassingTrace.Events.Api.Common;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Storylines;

namespace PassingTrace.Events.Api.Ai.Tools.Mutations;

/// <summary>Request-bound write tools. Delete tools only request a separate human authorization.</summary>
public sealed partial class PersonalMutationTools(AiMutationService mutations, EventService eventService,
    StorylineService storylines, CurrentUserContext user)
{
    private Guid _conversationId;
    private long _messageId;
    private AssistantCalendarContext? _calendar;
    private string _question = "";
    private readonly Queue<AssistantStreamEvent> _events = new();
    private readonly HashSet<Guid> _emitted = [];
    private readonly List<AiMutationResult> _receipts = [];
    public bool HasOperations { get; private set; }

    public void Configure(Guid conversationId, long messageId, AssistantCalendarContext calendar, string question)
    {
        _conversationId = conversationId;
        _messageId = messageId;
        _calendar = calendar;
        _question = question;
        _events.Clear();
        _emitted.Clear();
        _receipts.Clear();
        HasOperations = false;
    }

    public IReadOnlyList<AssistantStreamEvent> DrainEvents()
    {
        var result = _events.ToArray();
        _events.Clear();
        return result;
    }

    public static bool MayWrite(string question) => MutationIntentPolicy.MayWrite(question);

    public EvidenceBundle Merge(EvidenceBundle evidence)
    {
        var receipts = _receipts.AsEnumerable().Reverse().Select(x => x.Message.Evidence).OfType<EvidenceBundle>().ToArray();
        return evidence with
        {
            Records = receipts.SelectMany(x => x.Records).Concat(evidence.Records).DistinctBy(x => x.EventId).ToArray(),
            Storylines = receipts.SelectMany(x => x.Storylines ?? []).Concat(evidence.Storylines ?? []).DistinctBy(x => x.StorylineId).ToArray(),
        };
    }

    private void RequireIntent(string operation)
    {
        if (_messageId <= 0 || _calendar is null || !MutationIntentPolicy.HasIntent(_question, operation))
            throw new DomainValidationException("尚未获得本轮明确的创建、编辑或删除请求；普通聊天和总结不会写入。");
    }

    private AiMutationResult Publish(AiMutationResult result)
    {
        HasOperations = true;
        if (_emitted.Add(result.OperationId))
        {
            _receipts.Add(result);
            _events.Enqueue(new("mutation-result", result));
        }
        return result;
    }
}
