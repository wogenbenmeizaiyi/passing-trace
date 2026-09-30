using PassingTrace.Events.Api.Ai.Assistant;
using PassingTrace.Events.Api.Ai.Assistant.Presentation;
using PassingTrace.Events.Api.Ai.Evidence;
using PassingTrace.Events.Api.Ai.Models;
using StackExchange.Redis;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AssistantEmptyAnswerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public void Empty_or_whitespace_final_answer_is_incomplete_and_not_successful(string? answer)
    {
        var exception = Assert.Throws<IncompleteAiResponseException>(() => AssistantCompletionGuard.ThrowIfEmpty(answer));
        var presented = AssistantErrorPresenter.Present(exception);
        Assert.Equal("incomplete_ai_response", presented.Code);
        Assert.True(presented.Retryable);
    }

    [Theory]
    [InlineData("{\"answer\":\"\",\"evidence\":{\"records\":[],\"memories\":[]}}")]
    [InlineData("{\"answer\":\"  \\n\\t\",\"evidence\":{\"records\":[],\"memories\":[]}}")]
    [InlineData("{\"answer\":\"已完成\",\"evidence\":null}")]
    [InlineData("{\"answer\":\"已完成\",\"evidence\":{}}")]
    [InlineData("null")]
    [InlineData("{partial")]
    public void Empty_or_invalid_cached_answer_is_a_cache_miss(string cached) =>
        Assert.Null(ReadCached(cached));

    [Fact]
    public void Existing_nonempty_cached_answer_remains_usable() =>
        Assert.NotNull(ReadCached("{\"answer\":\"消费合计120元。\",\"evidence\":{\"records\":[],\"memories\":[]}}"));

    [Fact]
    public void Serialized_cached_answer_preserves_evidence_and_navigation_action()
    {
        var action = new AssistantAction("amap-navigation", "amap", "导航到人民广场", "人民广场",
            "人民大道", 31.23m, 121.47m, "GCJ02", null, "personal-record", 12, 34);
        var evidence = new EvidenceBundle([
            new RecordEvidence(12, 2, "散步", "到人民广场散步", null, null, DateTimeOffset.UnixEpoch, 1),
        ], [], Actions: [action]);

        var cached = AssistantAnswerCache.Read(AssistantAnswerCache.Serialize("已找到散步记录。", evidence));

        Assert.NotNull(cached);
        Assert.Equal("已找到散步记录。", cached.Answer);
        Assert.Equal(evidence.Records[0], Assert.Single(cached.Evidence.Records));
        Assert.Equal(action, Assert.Single(cached.Evidence.Actions!));
    }

    [Fact]
    public void Navigation_action_can_produce_valid_final_text_without_model_text()
    {
        var action = new AssistantAction("amap-navigation", "amap", "导航到人民广场", "人民广场",
            "人民大道", 31.23m, 121.47m, "GCJ02", null, "amap-live");
        var answer = AssistantAnswerPresenter.Present(string.Empty, "导航到人民广场",
            new EvidenceBundle([], [], Actions: [action]), [action]);
        AssistantCompletionGuard.ThrowIfEmpty(answer);
        Assert.Contains("人民广场", answer);
    }

    private static object? ReadCached(string cached) => AssistantAnswerCache.Read((RedisValue)cached);
}
