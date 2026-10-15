namespace PassingTrace.Events.Api.Ai.Tools.Mutations;

/// <summary>A safe refusal code; it contains no tool arguments or private content.</summary>
public sealed class MutationIntentRequiredException()
    : Exception("尚未获得明确的写入请求；普通聊天和总结不会写入。");
