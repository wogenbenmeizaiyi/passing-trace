namespace PassingTrace.Events.Api.Ai;

// Only validation of the user's message may ask them to edit their input.
public sealed class AssistantMessageValidationException()
    : ArgumentException("消息长度必须在 1 到 8000 字符之间。", "content");
