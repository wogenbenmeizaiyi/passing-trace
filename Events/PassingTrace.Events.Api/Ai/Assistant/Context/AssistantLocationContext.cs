using Microsoft.Agents.AI;
using PassingTrace.Events.Api.Ai.Amap;

namespace PassingTrace.Events.Api.Ai.Assistant.Context;

/// <summary>Explicit, single-message device location; never stored as conversation or record metadata.</summary>
public sealed record AssistantLocationRequest(
    decimal Latitude, decimal Longitude, double AccuracyMeters, DateTimeOffset CapturedAt, string CoordinateSystem);

public sealed class AssistantLocationContext(AssistantLocationRequest? location) : AIContextProvider
{
    public AssistantLocationRequest? Location { get; } = location;
    public static async Task<AssistantLocationContext> CreateAsync(AssistantLocationRequest? location,
        DateTimeOffset now, AmapCoordinateConverter? converter, CancellationToken cancellationToken)
    {
        if (location is null) return new(null);
        if (location.Latitude is < -90 or > 90 || location.Longitude is < -180 or > 180 ||
            !double.IsFinite(location.AccuracyMeters) || location.AccuracyMeters is <= 0 or > 100_000 ||
            location.CoordinateSystem is not ("WGS84" or "GCJ02"))
            throw new AssistantLocationException("invalid_location", "位置信息无效，请重新获取当前位置。");
        if (location.CapturedAt < now.AddMinutes(-5) || location.CapturedAt > now.AddMinutes(1))
            throw new AssistantLocationException("expired_location", "位置已过期，请点击“使用当前位置”重新获取后发送。");
        if (location.CoordinateSystem == "WGS84")
        {
            if (converter is null) throw AssistantLocationException.ConversionUnavailable();
            var coordinates = await converter.ConvertAsync(location.Latitude, location.Longitude, cancellationToken);
            location = location with { Latitude = coordinates.Latitude, Longitude = coordinates.Longitude, CoordinateSystem = "GCJ02" };
        }
        return new(location);
    }

    public string Instructions => Location is null
        ? "本条消息未附加设备定位。不能自动读取设备位置，也不能把历史聊天、个人记录中的地点或服务器 IP 当作当前位置。需要当前位置时，请用户点击输入框上方的“使用当前位置”，或提供出发地。"
        : FormattableString.Invariant($"""
            用户主动附加了本条消息的单次设备定位：纬度 {Location.Latitude:0.######}，经度 {Location.Longitude:0.######}，坐标系 GCJ02，精度约 {Location.AccuracyMeters:0} 米，采集时间 {Location.CapturedAt:O}。
            这是本轮当前位置，可直接作为高德周边查询中心或路线起点；地名需通过 ReverseGeocodeAmapLocation 核实，不要凭坐标猜地名，也不必再次询问出发地。精度较粗时只能描述大致位置，不能断言具体小区或建筑。
            定位仅用于本轮问题，不代表持续追踪。后续未附加定位的消息不能把本次位置当作实时位置；除非用户明确要求，不展示经纬度，不自动创建记录、计划或长期记忆。
            """);

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(new AIContext { Instructions = Instructions });
}

public sealed class AssistantLocationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public static AssistantLocationException ConversionUnavailable() =>
        new("location_conversion_unavailable", "位置坐标转换暂时不可用，请稍后重试，或在消息中提供出发地。");
}
