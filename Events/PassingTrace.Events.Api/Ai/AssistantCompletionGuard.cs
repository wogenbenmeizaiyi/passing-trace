using Microsoft.Extensions.AI;

namespace PassingTrace.Events.Api.Ai;

/// <summary>Do not persist a truncated or provider-blocked stream as a completed answer.</summary>
public static class AssistantCompletionGuard
{
    public static void ThrowIfEmpty(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) throw new IncompleteAiResponseException();
    }

    public static void ThrowIfUnsuccessful(ChatFinishReason? reason)
    {
        if (reason == ChatFinishReason.Length) throw new AssistantResponseLimitException();
        if (reason == ChatFinishReason.ContentFilter) throw new AssistantResponseBlockedException();
    }
}

public sealed class AssistantResponseLimitException() : Exception("AI response exceeded its generation limit.");
public sealed class AssistantResponseBlockedException() : Exception("AI provider withheld the response.");
