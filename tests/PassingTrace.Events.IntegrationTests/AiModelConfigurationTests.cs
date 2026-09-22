using PassingTrace.Events.Api.Ai;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AiModelConfigurationTests
{
    [Fact]
    public void Defaults_UseDeepSeekFlashWithoutExpiredFallbackAndKeepQwenEmbedding()
    {
        var options = new AiModelOptions();

        Assert.Equal("DeepSeek", options.Assistant.Provider);
        Assert.Equal("deepseek-flash", options.Assistant.PrimaryModel);
        Assert.Null(options.Assistant.FallbackModel);
        Assert.Equal("DeepSeek", options.Semantic.Provider);
        Assert.Equal("deepseek-flash", options.Semantic.PrimaryModel);
        Assert.Null(options.Semantic.FallbackModel);
        Assert.Equal("https://api.deepseek.com/v1", options.Providers["DeepSeek"].Endpoint);
        Assert.False(options.Providers["DeepSeek"].UseRemoteMediaUrls);
        Assert.Equal("Qwen", options.Embedding.Provider);
        Assert.Equal("text-embedding-v4", options.Embedding.Model);
        Assert.Equal(1024, options.Embedding.Dimensions);
    }

    [Fact]
    public void RemoveThinking_RemovesMiniMaxReasoningEnvelope()
    {
        var result = AiTextSanitizer.RemoveThinking("<think>内部推理</think>\n\n最终回答");

        Assert.Equal("最终回答", result);
    }

    [Fact]
    public void StreamingThinkingFilter_HandlesTagsSplitAcrossChunks()
    {
        var filter = new StreamingThinkingFilter();

        Assert.Equal(string.Empty, filter.Push("<thi"));
        Assert.Equal(string.Empty, filter.Push("nk>内部"));
        Assert.Equal("最终", filter.Push("推理</think>\n最终"));
        Assert.Equal("回答", filter.Push("回答"));
    }
}
