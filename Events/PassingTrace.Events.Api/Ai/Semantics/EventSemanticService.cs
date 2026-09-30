using System.Text.Json;
using PassingTrace.Core.Ai;

namespace PassingTrace.Events.Api.Ai.Semantics;

public sealed class EventSemanticService(IEventSemanticRepository repository, TimeProvider clock)
{
    public async Task<EventSemanticResponse?> GetAsync(long userId, long eventId, CancellationToken cancellationToken)
    {
        var data = await repository.ReadAsync(userId, eventId, cancellationToken);
        if (data is null) return null;
        var (evt, run) = data;
        if (run is null) return new(eventId, evt.CurrentSourceRevision, "Pending", null, null, string.Empty, string.Empty, evt.UpdatedAt, null, null);
        object? semantic = string.IsNullOrWhiteSpace(run.SemanticEnvelopeJson) ? null : JsonSerializer.Deserialize<object>(run.SemanticEnvelopeJson);
        return new(eventId, run.SourceRevision, run.Status.ToString(), run.Summary, semantic, run.Model,
            run.PipelineVersion, run.CreatedAt, run.CompletedAt, run.ErrorMessage);
    }

    public Task<bool> ReparseAsync(long userId, long eventId, CancellationToken cancellationToken) =>
        repository.RequestReparseAsync(userId, eventId, clock.GetUtcNow(), cancellationToken);
}
