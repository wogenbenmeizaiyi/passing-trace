namespace PassingTrace.Core.Ai;

public enum SemanticRunStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Stale = 5,
    Cancelled = 6,
}

public enum SemanticAssertion
{
    Observed = 1,
    UserStated = 2,
    Inferred = 3,
}

/// <summary>针对一个不可变 SourceRevision 的一次可审计分析。</summary>
public sealed class EventSemanticRun
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long EventId { get; set; }
    public int SourceRevision { get; set; }
    public string PipelineVersion { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public SemanticRunStatus Status { get; set; }
    public string? SemanticEnvelopeJson { get; set; }
    public string? Summary { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public long? DurationMilliseconds { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Events.Event Event { get; set; } = null!;

    public List<SemanticMention> Mentions { get; set; } = [];
    public List<ExpenseFact> Expenses { get; set; } = [];
}

/// <summary>从 SemanticEnvelope 展开的可过滤事实。</summary>
public sealed class SemanticMention
{
    public long Id { get; set; }
    public long SemanticRunId { get; set; }
    public long UserId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public string OriginalValue { get; set; } = string.Empty;
    public SemanticAssertion Assertion { get; set; }
    public decimal Confidence { get; set; }
    public int? TextStart { get; set; }
    public int? TextLength { get; set; }
    public Guid? MediaAssetId { get; set; }
    public EventSemanticRun SemanticRun { get; set; } = null!;
}

/// <summary>金额事实单独建模，保证统计通过参数化 SQL 完成。</summary>
public sealed class ExpenseFact
{
    public long Id { get; set; }
    public long SemanticRunId { get; set; }
    public long UserId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public EventSemanticRun SemanticRun { get; set; } = null!;
}

/// <summary>当前修订的混合检索文档；向量与 tsvector 是 EF shadow properties。</summary>
public sealed class EventSearchIndex
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long EventId { get; set; }
    public int SourceRevision { get; set; }
    public long? SemanticRunId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RawContent { get; set; } = string.Empty;
    public string AiSummary { get; set; } = string.Empty;
    public string ImageDescriptions { get; set; } = string.Empty;
    public string RetrievalText { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Events.Event Event { get; set; } = null!;
}
