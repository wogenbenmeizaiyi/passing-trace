namespace PassingTrace.Events.Api.Ai.Assistant;

internal static class AssistantNavigationPolicy
{
    public static bool LooksLikeLiveAmapQuestion(string text) => new[]
        {
            "高德", "地图", "导航", "定位", "地址", "坐标", "经纬度", "天气", "路线", "怎么走", "在哪",
            "附近", "周边", "地铁", "车站", "机场", "景点", "餐厅", "饭店", "商场", "距离", "步行", "骑行", "公交", "驾车",
        }
        .Any(text.Contains);
    public static bool LooksLikeNavigationActionRequest(string text) =>
        new[] { "导航到", "导航去", "定位到", "定位出", "帮我定位", "打开高德", "打开地图", "带我去", "navigate to", "navigation to" }
            .Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    public static bool LooksLikePersonalHistoryPlaceRequest(string text) =>
        new[]
        {
            "我最近", "我上次", "我去过", "我吃过", "我的记录", "记录里", "曾经去", "曾经吃",
            "my latest", "my last", "i visited", "i ate", "my record", "from my record",
        }
            .Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    public static bool LooksLikeContextualFollowUp(string text) =>
        new[] { "再试", "重新", "刚才", "那个", "上一个", "第二个", "继续", "还是不行", "try again" }
            .Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
}
