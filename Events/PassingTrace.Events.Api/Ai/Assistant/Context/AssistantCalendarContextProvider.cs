using Microsoft.Agents.AI;

namespace PassingTrace.Events.Api.Ai.Assistant.Context;

public sealed class AssistantCalendarContextProvider(AssistantCalendarContext calendar) : AIContextProvider
{
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AIContext { Instructions = calendar.Instructions });
}
