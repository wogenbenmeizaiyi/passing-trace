using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PassingTrace.Core.Events;

public enum EventLabelType
{
    PrimaryCategory = 1,
    BehaviorTag = 2,
}

public enum EventLabelOrigin
{
    Manual = 1,
    Ai = 2,
}

public enum SourceLabelDecision
{
    Include = 1,
    Exclude = 2,
}

public static partial class EventTaxonomy
{
    public const string Version = "life-v1";

    public static readonly IReadOnlyDictionary<string, string> Categories =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["food"] = "美食",
            ["shopping"] = "购物",
            ["travel"] = "旅行",
            ["scenery"] = "美景",
            ["entertainment"] = "娱乐",
            ["exercise"] = "运动",
            ["work"] = "工作",
            ["study"] = "学习",
            ["social"] = "社交",
            ["home"] = "居家",
            ["health"] = "健康",
            ["transport"] = "交通",
            ["other"] = "其他",
        };

    public static readonly IReadOnlyDictionary<string, string> BehaviorTags =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dining"] = "聚餐",
            ["restaurant"] = "探店",
            ["cooking"] = "做饭",
            ["takeout"] = "外卖",
            ["coffee"] = "咖啡",
            ["baking"] = "烘焙",
            ["daily-goods"] = "日用品",
            ["clothing"] = "服饰",
            ["digital"] = "数码",
            ["home-goods"] = "家居",
            ["gift"] = "礼物",
            ["city-walk"] = "城市漫步",
            ["attraction"] = "景点",
            ["museum"] = "博物馆",
            ["photography"] = "拍照",
            ["camping"] = "露营",
            ["business-trip"] = "出差",
            ["movie"] = "电影",
            ["music"] = "音乐",
            ["ktv"] = "KTV",
            ["gaming"] = "游戏",
            ["show"] = "演出",
            ["fitness"] = "健身",
            ["running"] = "跑步",
            ["walking"] = "步行",
            ["cycling"] = "骑行",
            ["hiking"] = "徒步",
            ["swimming"] = "游泳",
            ["meeting"] = "会议",
            ["coding"] = "编程",
            ["writing"] = "写作",
            ["reading"] = "阅读",
            ["course"] = "课程",
            ["friends"] = "朋友",
            ["family"] = "家庭",
            ["date"] = "约会",
            ["cleaning"] = "清洁",
            ["repair"] = "维修",
            ["sleep"] = "睡眠",
            ["medical"] = "就医",
            ["commute"] = "通勤",
            ["driving"] = "驾车",
            ["public-transit"] = "公交",
        };

    public static bool IsCategory(string? key) => key is not null && Categories.ContainsKey(key);
    public static bool IsBehaviorTag(string? key) => key is not null && BehaviorTags.ContainsKey(key);
    public static string CategoryLabel(string key) => Categories[key];
    public static string BehaviorTagLabel(string key) => BehaviorTags[key];

    public static string NormalizeCustomTag(string value)
    {
        var normalized = Whitespace().Replace(value.Normalize(NormalizationForm.FormKC).Trim(), " ");
        if (new StringInfo(normalized).LengthInTextElements is < 1 or > 24)
        {
            throw new DomainValidationException("自定义标签长度必须为 1 到 24 个字符。");
        }
        return normalized;
    }

    public static string NormalizedValue(string value) => NormalizeCustomTag(value).ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>用户针对一个不可变 SourceRevision 作出的标签决定。</summary>
public sealed class SourceRevisionLabel
{
    public long Id { get; set; }
    public long SourceRevisionId { get; set; }
    public long UserId { get; set; }
    public EventLabelType Type { get; set; }
    public SourceLabelDecision Decision { get; set; }
    public string? TaxonomyKey { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public SourceRevision SourceRevision { get; set; } = null!;
}

/// <summary>当前修订最终生效的分类与标签投影。</summary>
public sealed class EventLabelIndex
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long EventId { get; set; }
    public int SourceRevision { get; set; }
    public long? SemanticRunId { get; set; }
    public EventLabelType Type { get; set; }
    public EventLabelOrigin Origin { get; set; }
    public string? TaxonomyKey { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public decimal? Confidence { get; set; }
    public bool IsCurrent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Event Event { get; set; } = null!;
}
