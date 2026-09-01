namespace PassingTrace.Events.Api.Ai;

/// <summary>
/// Selects independently configurable providers for interactive chat, semantic analysis and embeddings.
/// OpenAI-compatible providers can be added through configuration without changing application services.
/// </summary>
public sealed class AiModelOptions
{
    public const string SectionName = "AiModels";

    public ChatModelSelection Assistant { get; set; } = new()
    {
        Provider = "MiniMax",
        PrimaryModel = "MiniMax-M3",
        FallbackModel = "MiniMax-M2.7",
    };

    public ChatModelSelection Semantic { get; set; } = new()
    {
        Provider = "MiniMax",
        PrimaryModel = "MiniMax-M3",
        FallbackModel = "MiniMax-M2.7",
    };

    public EmbeddingModelSelection Embedding { get; set; } = new()
    {
        Provider = "Qwen",
        Model = "text-embedding-v4",
        Dimensions = 1024,
    };

    public Dictionary<string, OpenAiCompatibleProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["MiniMax"] = new()
            {
                Endpoint = "https://api.minimaxi.com/v1",
                StripThinkingTags = true,
                UseRemoteMediaUrls = true,
            },
            ["Qwen"] = new()
            {
                Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            },
        };

    public string PipelineVersion { get; set; } = "semantic-v2";
    public string PromptVersion { get; set; } = "passingtrace-zh-v2";
}

public sealed class ChatModelSelection
{
    public string Provider { get; set; } = string.Empty;
    public string PrimaryModel { get; set; } = string.Empty;
    public string? FallbackModel { get; set; }
}

public sealed class EmbeddingModelSelection
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Dimensions { get; set; } = 1024;
}

public sealed class OpenAiCompatibleProviderOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool StripThinkingTags { get; set; }
    public bool UseRemoteMediaUrls { get; set; }
}
