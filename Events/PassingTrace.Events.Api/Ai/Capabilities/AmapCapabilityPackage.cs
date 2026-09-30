using Microsoft.Extensions.AI;
using PassingTrace.Events.Api.Ai.Amap;

namespace PassingTrace.Events.Api.Ai.Capabilities;

public sealed class AmapCapabilityPackage(AmapAiTools tools) : IAiCapabilityPackage
{
    // This adapter already calls the external Amap MCP server and enforces its quota policy.
    public bool UsesInternalMcp => false;
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
