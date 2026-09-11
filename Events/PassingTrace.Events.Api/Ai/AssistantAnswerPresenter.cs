using System.Text.RegularExpressions;

namespace PassingTrace.Events.Api.Ai;

public static partial class AssistantAnswerPresenter
{
    public static string Present(
        string generatedAnswer,
        string question,
        EvidenceBundle evidence,
        IReadOnlyList<AssistantAction> actions)
    {
        var technicalDetailsRequested = RequestsTechnicalDetails(question);
        var navigation = actions.FirstOrDefault(action => action.Type == "amap-navigation");
        if (navigation is not null && !technicalDetailsRequested)
            return BuildNavigationAnswer(navigation, evidence);

        return RemoveInternalDetails(generatedAnswer, technicalDetailsRequested);
    }

    private static string BuildNavigationAnswer(AssistantAction navigation, EvidenceBundle evidence)
    {
        var place = evidence.Places?.FirstOrDefault(item =>
            navigation.LocationId.HasValue && item.LocationId == navigation.LocationId.Value) ??
            evidence.Places?.FirstOrDefault(item =>
                navigation.EventId.HasValue && item.EventId == navigation.EventId.Value);
        var placeName = EscapeMarkdown(place?.Name ?? navigation.PlaceName);
        var address = EscapeMarkdown(place?.Address ?? navigation.Address ?? string.Empty);
        string introduction;

        if (navigation.Source == "personal-record")
        {
            var eventId = navigation.EventId ?? place?.EventId;
            var eventTitle = EscapeMarkdown(place?.EventTitle ?? string.Empty);
            var origin = eventTitle.Length > 0 ? $"，来自“{eventTitle}”" : string.Empty;
            var citation = eventId.HasValue ? $" [Event #{eventId.Value}]" : string.Empty;
            introduction = $"已经找到你记录里的地点：**{placeName}**{origin}{citation}。";
        }
        else
        {
            introduction = $"已经找到 **{placeName}**（来自高德地图）。";
        }

        var addressLine = address.Length > 0 ? $"\n\n地址：{address}" : string.Empty;
        return $"{introduction}{addressLine}\n\n导航已经准备好，点击下方的“高德导航”即可打开。";
    }

    private static string RemoveInternalDetails(string answer, bool technicalDetailsRequested)
    {
        if (string.IsNullOrWhiteSpace(answer)) return answer;

        var result = InternalNavigationMarkdownRegex().Replace(answer, match => match.Groups["label"].Value);
        result = InternalNavigationUriRegex().Replace(result, string.Empty);
        result = BareEventIdentifierRegex().Replace(result, "这条记录");
        if (!technicalDetailsRequested)
        {
            result = InternalFieldRegex().Replace(result, "地点信息");
            result = PoiIdentifierRegex().Replace(result, "地点信息");
            result = CoordinateSystemRegex().Replace(result, "高德地图坐标");
        }
        result = ChineseWhitespaceRegex().Replace(result, string.Empty);
        return result.Trim();
    }

    private static bool RequestsTechnicalDetails(string question) => new[]
        {
            "字段", "locationId", "candidateId", "poiId", "POI ID", "GCJ02", "坐标系", "经纬度", "接口", "调试",
        }
        .Any(term => question.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string EscapeMarkdown(string value) => MarkdownCharacterRegex().Replace(value, @"\$1");

    [GeneratedRegex(@"\[(?<label>[^\]\r\n]{1,160})\]\((?:amap|amap-navigation)://[^)\r\n]+\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InternalNavigationMarkdownRegex();

    [GeneratedRegex(@"(?:amap|amap-navigation)://[^\s)\]}]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InternalNavigationUriRegex();

    [GeneratedRegex(@"(?<!\[)\bEvent\s*#\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareEventIdentifierRegex();

    [GeneratedRegex(@"\b(?:locationId|candidateId|poiId|ProviderPoiId|sourceRevision|adCode)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InternalFieldRegex();

    [GeneratedRegex(@"\bPOI(?:\s*ID)?\s*[（(]?[A-Z0-9_-]{5,}[）)]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PoiIdentifierRegex();

    [GeneratedRegex(@"\bGCJ02\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CoordinateSystemRegex();

    [GeneratedRegex(@"(?<=[一-龥])\s+(?=[一-龥])", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseWhitespaceRegex();

    [GeneratedRegex(@"([\\`*{}\[\]()#+.!|_-])", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownCharacterRegex();
}
