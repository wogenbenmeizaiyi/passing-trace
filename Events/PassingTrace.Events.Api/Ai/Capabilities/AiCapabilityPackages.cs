using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using PassingTrace.Events.Api.Ai.Amap;

namespace PassingTrace.Events.Api.Ai.Capabilities;

public interface IAiCapabilityPackage
{
    string Key { get; }
    bool IsAvailable { get; }
    IReadOnlyList<string> Capabilities { get; }
    IReadOnlyList<AITool> CreateTools();
}

internal static class AiFunctionToolFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static AIFunction Create<T>(T target, string methodName, string name, string description)
        where T : class =>
        AIFunctionFactory.Create(typeof(T).GetMethod(methodName)!, target, name, description, JsonOptions);
}

public sealed class PersonalRecordsCapabilityPackage(PersonalRecordTools tools) : IAiCapabilityPackage
{
    public string Key => "personal-records";
    public bool IsAvailable => true;
    public IReadOnlyList<string> Capabilities { get; } =
        ["records", "statistics", "memories", "saved-places", "storylines"];

    public IReadOnlyList<AITool> CreateTools() =>
    [
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.SearchMyRecordsAsync), "SearchMyRecords",
            "搜索当前用户自己的记录，返回按 RRF 排序的记录及其已确认地点；适合‘我最近吃过/去过’等语义查询。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.AggregateMyRecordsAsync), "AggregateMyRecords",
            "执行白名单次数、金额、趋势、完成率统计。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.GetMyRecordEvidenceAsync), "GetMyRecordEvidence",
            "获取已检索记录的原文和语义证据。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.SearchMyMemoriesAsync), "SearchMyMemories",
            "搜索当前用户有证据的长期记忆。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.SearchMyPlacesAsync), "SearchMyPlaces",
            "搜索当前用户已确认的历史地点。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.GetMyPlaceEvidenceAsync), "GetMyPlaceEvidence",
            "读取已检索历史地点的记录证据。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.GetNavigationTargetAsync), "GetNavigationTarget",
            "为 SearchMyRecords 或 SearchMyPlaces 已检索且有可信坐标的历史地点生成结构化导航动作。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.SearchMyStorylinesAsync), "SearchMyStorylines",
            "搜索当前用户自己的故事线。"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalRecordTools.GetMyStorylineEvidenceAsync), "GetMyStorylineEvidence",
            "读取已检索故事线的阶段、关系和固定记录修订证据。"),
    ];
}

public sealed class AmapCapabilityPackage(AmapAiTools tools) : IAiCapabilityPackage
{
    public string Key => "amap";
    public bool IsAvailable => tools.IsAvailable;
    public IReadOnlyList<string> Capabilities { get; } =
    [
        "place-search", "nearby-search", "place-detail", "geocode", "reverse-geocode",
        "weather", "walking-route", "bicycling-route", "transit-route", "driving-route",
        "distance", "navigation", "trip-map",
    ];

    public IReadOnlyList<AITool> CreateTools()
    {
        if (!IsAvailable) return [];
        return
        [
            AiFunctionToolFactory.Create(tools, nameof(AmapAiTools.SearchAmapPlacesAsync), "SearchAmapPlaces",
                "用高德地图搜索任意地点或以明确坐标为中心执行周边搜索。"),
            AiFunctionToolFactory.Create(tools, nameof(AmapAiTools.GetAmapPlaceDetailsAsync), "GetAmapPlaceDetails",
                "读取已检索高德 POI 的详情。"),
            AiFunctionToolFactory.Create(tools, nameof(AmapAiTools.GeocodeAmapAddressAsync), "GeocodeAmapAddress",
                "把地址或地标临时解析为高德 GCJ02 坐标，不修改个人记录。"),
            AiFunctionToolFactory.Create(tools, nameof(AmapAiTools.ReverseGeocodeAmapLocationAsync), "ReverseGeocodeAmapLocation",
                "把高德 GCJ02 坐标转换为地址。"),
            AiFunctionToolFactory.Create(tools, nameof(AmapAiTools.GetAmapWeatherAsync), "GetAmapWeather",
                "查询高德实时天气与预报。"),
            AiFunctionToolFactory.Create(tools, nameof(AmapAiTools.PlanAmapRouteAsync), "PlanAmapRoute",
                "规划高德步行、骑行、公交或驾车路线。"),
            AiFunctionToolFactory.Create(tools, nameof(AmapAiTools.MeasureAmapDistanceAsync), "MeasureAmapDistance",
                "测量两个高德坐标之间的距离。"),
            AiFunctionToolFactory.Create(tools, nameof(AmapAiTools.CreateAmapNavigationAsync), "CreateAmapNavigation",
                "为已检索候选创建客户端可展示的安全高德目的地导航动作；无需起点，唯一候选时直接调用。"),
            AiFunctionToolFactory.Create(tools, nameof(AmapAiTools.CreateAmapTripMapAsync), "CreateAmapTripMap",
                "让高德为已整理的行程生成专属地图动作；若服务端不支持则安全降级。"),
        ];
    }
}
