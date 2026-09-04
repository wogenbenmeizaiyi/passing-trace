using PassingTrace.Core.Events;

namespace PassingTrace.Core.Storylines;

public enum StorylineStatus
{
    Ongoing = 1,
    Completed = 2,
}

public enum StorylineRelationType
{
    Sequence = 1,
    Branch = 2,
    Parallel = 3,
    Related = 4,
}

public enum StorylineNodeEmphasis
{
    Normal = 1,
    Important = 2,
}

public enum StorylineLayoutState
{
    Arranged = 1,
    NeedsArrangement = 2,
}

public enum StorylineTagOrigin
{
    Manual = 1,
    Derived = 2,
}

/// <summary>平台无关的故事线聚合根；画布坐标单独保存在 Web 布局投影中。</summary>
public sealed class Storyline
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CategoryKey { get; set; } = StorylineTaxonomy.Other;
    public StorylineStatus Status { get; set; }
    public int CurrentRevision { get; set; }
    public string? CreationIdempotencyKey { get; set; }
    public Guid? CoverMediaAssetId { get; set; }
    public DateTimeOffset? RangeStart { get; set; }
    public DateTimeOffset? RangeEnd { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint RowVersion { get; set; }

    public List<StorylineRevision> Revisions { get; set; } = [];
    public List<StorylineSearchIndex> SearchIndexes { get; set; } = [];
}

/// <summary>一次不可变语义图修订。Web 保存和手机快捷操作都只追加新修订。</summary>
public sealed class StorylineRevision
{
    public long Id { get; set; }
    public Guid StorylineId { get; set; }
    public int Revision { get; set; }
    public string? IdempotencyKey { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CategoryKey { get; set; } = StorylineTaxonomy.Other;
    public StorylineStatus Status { get; set; }
    public Guid? CoverMediaAssetId { get; set; }
    public DateTimeOffset? RangeStart { get; set; }
    public DateTimeOffset? RangeEnd { get; set; }
    public StorylineLayoutState LayoutState { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Storyline Storyline { get; set; } = null!;
    public List<StorylineStage> Stages { get; set; } = [];
    public List<StorylineNode> Nodes { get; set; } = [];
    public List<StorylineEdge> Edges { get; set; } = [];
    public List<StorylineRevisionTag> Tags { get; set; } = [];
    public StorylineWebLayout? WebLayout { get; set; }
}

public sealed class StorylineStage
{
    public long Id { get; set; }
    public long StorylineRevisionId { get; set; }
    public Guid Key { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SemanticOrder { get; set; }
    public StorylineRevision Revision { get; set; } = null!;
}

/// <summary>节点固定引用一个 Event SourceRevision，保证历史故事不会被后来编辑悄悄改写。</summary>
public sealed class StorylineNode
{
    public long Id { get; set; }
    public long StorylineRevisionId { get; set; }
    public Guid Key { get; set; }
    public long EventId { get; set; }
    public int SourceRevision { get; set; }
    public Guid? StageKey { get; set; }
    public int SemanticOrder { get; set; }
    public StorylineNodeEmphasis Emphasis { get; set; }
    public StorylineRevision Revision { get; set; } = null!;
    public Event Event { get; set; } = null!;
}

public sealed class StorylineEdge
{
    public long Id { get; set; }
    public long StorylineRevisionId { get; set; }
    public Guid Key { get; set; }
    public Guid SourceNodeKey { get; set; }
    public Guid TargetNodeKey { get; set; }
    public StorylineRelationType RelationType { get; set; }
    public string? Label { get; set; }
    public StorylineRevision Revision { get; set; } = null!;
}

public sealed class StorylineRevisionTag
{
    public long Id { get; set; }
    public long StorylineRevisionId { get; set; }
    public StorylineTagOrigin Origin { get; set; }
    public string? TaxonomyKey { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public StorylineRevision Revision { get; set; } = null!;
}

/// <summary>Web 专用布局投影。手机端永远不读取或写入这里的坐标。</summary>
public sealed class StorylineWebLayout
{
    public long StorylineRevisionId { get; set; }
    public string Direction { get; set; } = "LR";
    public decimal ViewportX { get; set; }
    public decimal ViewportY { get; set; }
    public decimal Zoom { get; set; } = 1;
    public StorylineRevision Revision { get; set; } = null!;
    public List<StorylineWebNodeLayout> Nodes { get; set; } = [];
    public List<StorylineWebStageLayout> Stages { get; set; } = [];
}

public sealed class StorylineWebNodeLayout
{
    public long Id { get; set; }
    public long StorylineRevisionId { get; set; }
    public Guid NodeKey { get; set; }
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public StorylineWebLayout Layout { get; set; } = null!;
}

public sealed class StorylineWebStageLayout
{
    public long Id { get; set; }
    public long StorylineRevisionId { get; set; }
    public Guid StageKey { get; set; }
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public StorylineWebLayout Layout { get; set; } = null!;
}

/// <summary>当前故事线修订的混合搜索文档；Embedding 是 EF shadow property。</summary>
public sealed class StorylineSearchIndex
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public Guid StorylineId { get; set; }
    public int Revision { get; set; }
    public string RetrievalText { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Storyline Storyline { get; set; } = null!;
}

public static class StorylineTaxonomy
{
    public const string Version = "storyline-v1";
    public const string Other = "other";
    private static readonly IReadOnlyDictionary<string, string> Categories = new Dictionary<string, string>
    {
        ["trip"] = "行程旅行",
        ["activity"] = "活动纪实",
        ["project"] = "项目过程",
        ["challenge"] = "目标挑战",
        ["lifecycle"] = "成长陪伴",
        ["series"] = "主题系列",
        ["life-period"] = "生活阶段",
        [Other] = "其他",
    };

    public static IReadOnlyDictionary<string, string> All => Categories;
    public static bool IsCategory(string key) => Categories.ContainsKey(key);
    public static string Label(string key) => Categories.TryGetValue(key, out var value) ? value : Categories[Other];
}
