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
    StorylineService storylines, CurrentUserContext user, Subjects.SubjectService? subjects = null)
{
    private Guid _conversationId;
    private long _messageId;
    private AssistantCalendarContext? _calendar;
    private IReadOnlySet<string> _authorizedOperations = new HashSet<string>();
    private readonly Queue<AssistantStreamEvent> _events = new();
    private readonly HashSet<Guid> _emitted = [];
    private readonly List<AiMutationResult> _receipts = [];
    private readonly List<SubjectEvidence> _subjectEvidence = [];
    private readonly List<SubjectEntryEvidence> _entryEvidence = [];
    private readonly List<RecordEvidence> _timelineRecordEvidence = [];
    public bool HasOperations { get; private set; }

    public void Configure(Guid conversationId, long messageId, AssistantCalendarContext calendar, string question,
        IReadOnlyList<ConversationContextMessage>? history = null)
    {
        _conversationId = conversationId;
        _messageId = messageId;
        _calendar = calendar;
        _authorizedOperations = MutationIntentPolicy.Resolve(question, history);
        _events.Clear();
        _emitted.Clear();
        _receipts.Clear();
        _subjectEvidence.Clear();
        _entryEvidence.Clear();
        _timelineRecordEvidence.Clear();
        HasOperations = false;
    }

    public IReadOnlyList<AssistantStreamEvent> DrainEvents()
    {
        var result = _events.ToArray();
        _events.Clear();
        return result;
    }

    public static bool MayWrite(string question, IReadOnlyList<ConversationContextMessage>? history = null) =>
        MutationIntentPolicy.Resolve(question, history).Count > 0;

    public EvidenceBundle Merge(EvidenceBundle evidence)
    {
        var receipts = _receipts.AsEnumerable().Reverse().Select(x => x.Message.Evidence).OfType<EvidenceBundle>().ToArray();
        return evidence with
        {
            Records = receipts.SelectMany(x => x.Records).Concat(_timelineRecordEvidence).Concat(evidence.Records).DistinctBy(x => x.EventId).ToArray(),
            Storylines = receipts.SelectMany(x => x.Storylines ?? []).Concat(evidence.Storylines ?? []).DistinctBy(x => x.StorylineId).ToArray(),
            Subjects = receipts.SelectMany(x => x.Subjects ?? []).Concat(_subjectEvidence).Concat(evidence.Subjects ?? []).DistinctBy(x => x.SubjectId).ToArray(),
            SubjectEntries = receipts.SelectMany(x => x.SubjectEntries ?? []).Concat(_entryEvidence).Concat(evidence.SubjectEntries ?? []).DistinctBy(x => x.EntryId).ToArray(),
        };
    }

    private void RequireIntent(string operation)
    {
        if (_messageId <= 0 || _calendar is null || !_authorizedOperations.Contains(operation))
            throw new MutationIntentRequiredException();
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
