namespace PassingTrace.Events.Api.Ai;

public sealed class QwenAiOptions
{
    public const string SectionName = "Qwen";
    public string Endpoint { get; set; } = "https://dashscope.aliyuncs.com/compatible-mode/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string PrimaryModel { get; set; } = "qwen3.8-max";
    public string FallbackModel { get; set; } = "qwen3.7-plus";
    public string EmbeddingModel { get; set; } = "text-embedding-v4";
    public int EmbeddingDimensions { get; set; } = 1024;
    public string PipelineVersion { get; set; } = "semantic-v1";
    public string PromptVersion { get; set; } = "passingtrace-zh-v1";
}
