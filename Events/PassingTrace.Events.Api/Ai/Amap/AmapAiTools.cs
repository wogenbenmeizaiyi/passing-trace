using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PassingTrace.Events.Api.Ai.Amap;

public sealed record AmapToolResponse(
    bool Success,
    string Capability,
    string Source,
    string? Error = null,
    string? Summary = null,
    IReadOnlyList<AmapPlaceEvidence>? Places = null,
    bool RequiresSelection = false,
    AssistantAction? Action = null);

public sealed record AmapToolSnapshot(
    IReadOnlyList<AmapPlaceEvidence> Places,
    IReadOnlyList<AssistantAction> Actions,
    IReadOnlyList<AmapResultEvidence> Results)
{
    public bool HasEvidence => Places.Count > 0 || Actions.Count > 0 || Results.Count > 0;
}

/// <summary>
/// 提供给模型的稳定高德工具合同。所有供应商返回先归一化，再进入会话证据和客户端动作。
/// </summary>
public sealed partial class AmapAiTools(
    IAmapMcpGateway gateway,
    IAmapQuotaGuard quota,
    TimeProvider clock)
{
    private const int MaxCallsPerTurn = 6;
    private int _callCount;
    private readonly Dictionary<string, AmapPlaceEvidence> _candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AmapPoiReference> _poiReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AmapPlaceEvidence> _places = [];
    private readonly List<AssistantAction> _actions = [];
    private readonly List<AmapResultEvidence> _results = [];
    private AmapPlaceEvidence? _lastUnambiguousCandidate;

    public bool IsAvailable => gateway.IsConfigured;

    public AmapToolSnapshot Snapshot => new(
        _places.DistinctBy(x => x.CandidateId, StringComparer.OrdinalIgnoreCase).ToArray(),
        _actions.DistinctBy(x => $"{x.Type}:{x.Latitude}:{x.Longitude}:{x.PlaceName}").ToArray(),
        _results.ToArray());

    public AmapPlaceEvidence? PreferredNavigationCandidate
    {
        get
        {
            var candidates = _candidates.Values
                .DistinctBy(candidate => candidate.CandidateId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return candidates.Length == 1 ? candidates[0] : _lastUnambiguousCandidate;
        }
    }

    public void SeedCandidates(IEnumerable<AmapPlaceEvidence> places)
    {
        foreach (var place in places.Where(IsValidPlace).Take(12))
        {
            _candidates[place.CandidateId] = place;
            if (!string.IsNullOrWhiteSpace(place.PoiId))
                _poiReferences[place.PoiId] = new(place.PoiId, place.Name, place.Address);
        }
    }

    [Description("用高德地图按关键词搜索任意地点；若给出中心点则执行周边搜索。附近查询没有明确经纬度时不要调用，应先询问用户。最多返回 3 个候选。")]
    public async Task<AmapToolResponse> SearchAmapPlacesAsync(
        [Description("地点、商店、景点或地铁站关键词")] string keywords,
        [Description("城市名称或 adCode，可空")] string? city = null,
        [Description("周边搜索中心点经度；必须与纬度同时提供")] decimal? centerLongitude = null,
        [Description("周边搜索中心点纬度；必须与经度同时提供")] decimal? centerLatitude = null,
        [Description("周边搜索半径，100-3000 米")] int radiusMeters = 1000,
        CancellationToken cancellationToken = default)
    {
        keywords = keywords?.Trim() ?? string.Empty;
        if (keywords.Length == 0)
            return Failure("place-search", "请提供要查找的地点关键词。");
        if (centerLongitude.HasValue != centerLatitude.HasValue)
            return Failure("place-search", "附近搜索需要同时提供起点经度和纬度。");
        if (centerLongitude.HasValue && !ValidCoordinates(centerLatitude!.Value, centerLongitude.Value))
            return Failure("place-search", "附近搜索的起点坐标无效。");

        var around = centerLongitude.HasValue;
        var arguments = around
            ? Args(
                ("keywords", keywords),
                ("location", Coordinate(centerLongitude!.Value, centerLatitude!.Value)),
                ("radius", Math.Clamp(radiusMeters, 100, 3000).ToString(CultureInfo.InvariantCulture)))
            : Args(("keywords", keywords), ("city", NullIfBlank(city)));
        return await InvokePlacesAsync(
            "place-search",
            around ? ["maps_around_search"] : ["maps_text_search"],
            arguments,
            AmapQuotaKind.Search,
            cancellationToken);
    }

    [Description("读取本轮或近期会话中已检索高德 POI 的详情。poiId 必须来自 SearchAmapPlaces 的结果。")]
    public async Task<AmapToolResponse> GetAmapPlaceDetailsAsync(
        string poiId,
        CancellationToken cancellationToken = default)
    {
        var resolvedPoiId = ResolvePoiId(poiId);
        if (resolvedPoiId is null)
            return Failure("place-detail", "只能读取本轮或近期会话已经检索到的高德地点详情。");
        return await InvokePlacesAsync(
            "place-detail", ["maps_search_detail"], Args(("id", resolvedPoiId)),
            AmapQuotaKind.Search, cancellationToken);
    }

    [Description("把地址或地标名称解析成高德 GCJ02 坐标。解析结果只用于本轮回答，不会修改用户记录。")]
    public async Task<AmapToolResponse> GeocodeAmapAddressAsync(
        string address,
        string? city = null,
        CancellationToken cancellationToken = default)
    {
        address = address?.Trim() ?? string.Empty;
        if (address.Length == 0) return Failure("geocode", "请提供需要解析的地址。");
        return await InvokePlacesAsync(
            "geocode", ["maps_geo"], Args(("address", address), ("city", NullIfBlank(city))),
            AmapQuotaKind.Lbs, cancellationToken);
    }

    [Description("把高德 GCJ02 经纬度转换成行政区和地址。")]
    public Task<AmapToolResponse> ReverseGeocodeAmapLocationAsync(
        decimal longitude,
        decimal latitude,
        CancellationToken cancellationToken = default) =>
        ValidCoordinates(latitude, longitude)
            ? InvokeRawAsync("reverse-geocode", ["maps_regeocode"],
                Args(("location", Coordinate(longitude, latitude))), AmapQuotaKind.Lbs, cancellationToken)
            : Task.FromResult(Failure("reverse-geocode", "经纬度无效。"));

    [Description("查询高德实时天气或预报。city 使用城市名称或标准 adCode。")]
    public Task<AmapToolResponse> GetAmapWeatherAsync(
        string city,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(city)
            ? Task.FromResult(Failure("weather", "请提供城市名称或 adCode。"))
            : InvokeRawAsync("weather", ["maps_weather"], Args(("city", city.Trim())),
                AmapQuotaKind.Lbs, cancellationToken);

    [Description("用高德规划步行、骑行、公交或驾车路线。公交路线必须提供起点和终点城市。")]
    public Task<AmapToolResponse> PlanAmapRouteAsync(
        decimal originLongitude,
        decimal originLatitude,
        decimal destinationLongitude,
        decimal destinationLatitude,
        [Description("walking、bicycling、transit 或 driving")] string mode,
        string? originCity = null,
        string? destinationCity = null,
        CancellationToken cancellationToken = default)
    {
        if (!ValidCoordinates(originLatitude, originLongitude) ||
            !ValidCoordinates(destinationLatitude, destinationLongitude))
            return Task.FromResult(Failure("route", "路线起点或终点坐标无效。"));
        mode = mode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (mode == "transit" && (string.IsNullOrWhiteSpace(originCity) || string.IsNullOrWhiteSpace(destinationCity)))
            return Task.FromResult(Failure("route", "公交路线需要起点城市和终点城市。"));
        var tools = mode switch
        {
            "walking" => new[] { "maps_direction_walking" },
            "bicycling" => new[] { "maps_bicycling", "maps_direction_bicycling" },
            "transit" => new[] { "maps_direction_transit_integrated" },
            "driving" => new[] { "maps_direction_driving" },
            _ => [],
        };
        if (tools.Length == 0)
            return Task.FromResult(Failure("route", "路线方式只支持 walking、bicycling、transit 或 driving。"));
        return InvokeRawAsync($"route-{mode}", tools,
            Args(
                ("origin", Coordinate(originLongitude, originLatitude)),
                ("destination", Coordinate(destinationLongitude, destinationLatitude)),
                ("city", NullIfBlank(originCity)),
                ("cityd", NullIfBlank(destinationCity))),
            AmapQuotaKind.Lbs,
            cancellationToken);
    }

    [Description("测量两个高德 GCJ02 坐标之间的距离。type 为 0 直线、1 驾车、3 步行。")]
    public Task<AmapToolResponse> MeasureAmapDistanceAsync(
        decimal originLongitude,
        decimal originLatitude,
        decimal destinationLongitude,
        decimal destinationLatitude,
        int type = 1,
        CancellationToken cancellationToken = default)
    {
        if (!ValidCoordinates(originLatitude, originLongitude) ||
            !ValidCoordinates(destinationLatitude, destinationLongitude))
            return Task.FromResult(Failure("distance", "测距起点或终点坐标无效。"));
        if (type is not (0 or 1 or 3)) type = 1;
        return InvokeRawAsync("distance", ["maps_distance"],
            Args(
                ("origins", Coordinate(originLongitude, originLatitude)),
                ("destination", Coordinate(destinationLongitude, destinationLatitude)),
                ("type", type.ToString(CultureInfo.InvariantCulture))),
            AmapQuotaKind.Lbs,
            cancellationToken);
    }

    [Description("为已经由高德搜索或解析出的候选地点创建安全的目的地导航动作。它不需要起点，高德 App 会使用用户当前位置；candidateId 必须来自工具结果，不接受模型自造坐标或 URL。唯一候选时应直接调用。")]
    public async Task<AmapToolResponse> CreateAmapNavigationAsync(
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReserveTurnCall(out var error)) return error!;
        var place = ResolveCandidate(candidateId);
        if (place is null)
        {
            var poiId = ResolvePoiId(candidateId);
            if (poiId is not null)
            {
                var details = await InvokePlacesAsync(
                    "place-detail", ["maps_search_detail"], Args(("id", poiId)),
                    AmapQuotaKind.Search, cancellationToken);
                if (!details.Success) return details;
                place = ResolveCandidate(poiId);
            }
        }
        if (place is null || !IsValidPlace(place))
            return Failure("navigation", "只能导航到本轮或近期会话中已经检索到的高德候选地点。");
        var action = new AssistantAction(
            "amap-navigation", "amap", $"导航到{place.Name}", place.Name, place.Address,
            place.Latitude, place.Longitude, "GCJ02", place.PoiId, "amap-live");
        _actions.Add(action);
        return new AmapToolResponse(
            true, "navigation", "amap-live", Summary: $"已创建“{place.Name}”的高德导航动作。", Action: action);
    }

    [Description("把结构化行程交给高德生成专属地图。只会接受并返回通过高德 HTTPS 域名校验的链接。")]
    public async Task<AmapToolResponse> CreateAmapTripMapAsync(
        string title,
        string itinerary,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(itinerary))
            return Failure("trip-map", "行程名称和行程内容不能为空。");
        var invoked = await InvokeAsync(
            "trip-map",
            ["maps_generate_trip_map", "maps_generate_map", "maps_create_trip_map"],
            Args(("title", Limit(title.Trim(), 120)), ("itinerary", Limit(itinerary.Trim(), 6000))),
            AmapQuotaKind.Lbs,
            cancellationToken);
        if (!invoked.Success || invoked.Response is null) return invoked.Error!;
        var url = ExtractTrustedAmapUrl(invoked.Response.Text) ??
            (invoked.Response.StructuredContent is { } structured
                ? ExtractTrustedAmapUrl(structured.ToString())
                : null);
        if (url is null)
            return Failure("trip-map", "高德没有返回可安全打开的专属地图链接。");
        var action = new AssistantAction(
            "amap-trip-map", "amap", $"在高德打开{Limit(title.Trim(), 40)}", Limit(title.Trim(), 120), null,
            0, 0, "GCJ02", null, "amap-live", WebUrl: url);
        _actions.Add(action);
        CaptureResult("trip-map", "高德已生成专属地图链接。");
        return new AmapToolResponse(true, "trip-map", "amap-live", Summary: "已生成高德专属地图。", Action: action);
    }

    private async Task<AmapToolResponse> InvokePlacesAsync(
        string capability,
        IReadOnlyList<string> tools,
        IReadOnlyDictionary<string, object?> arguments,
        AmapQuotaKind quotaKind,
        CancellationToken cancellationToken)
    {
        var invoked = await InvokeAsync(capability, tools, arguments, quotaKind, cancellationToken);
        if (!invoked.Success || invoked.Response is null) return invoked.Error!;
        foreach (var reference in AmapPayloadNormalizer.ReadPoiReferences(invoked.Response))
            _poiReferences[reference.Id] = reference;
        var places = AmapPayloadNormalizer.ReadPlaces(invoked.Response).Take(3).ToArray();
        foreach (var place in places)
        {
            _candidates[place.CandidateId] = place;
            _places.Add(place);
        }
        if (places.Length == 1) _lastUnambiguousCandidate = places[0];
        var summary = Sanitize(invoked.Response.Text);
        if (summary.Length == 0 && places.Length > 0)
            summary = string.Join("；", places.Select(x => $"{x.Name}（{x.Address ?? "地址未提供"}）"));
        CaptureResult(capability, summary);
        return new AmapToolResponse(
            true, capability, "amap-live", Summary: Limit(summary, 6000), Places: places,
            RequiresSelection: places.Length > 1);
    }

    private async Task<AmapToolResponse> InvokeRawAsync(
        string capability,
        IReadOnlyList<string> tools,
        IReadOnlyDictionary<string, object?> arguments,
        AmapQuotaKind quotaKind,
        CancellationToken cancellationToken)
    {
        var invoked = await InvokeAsync(capability, tools, arguments, quotaKind, cancellationToken);
        if (!invoked.Success || invoked.Response is null) return invoked.Error!;
        var summary = Sanitize(invoked.Response.Text);
        if (summary.Length == 0 && invoked.Response.StructuredContent is { } structured)
            summary = Sanitize(structured.ToString());
        CaptureResult(capability, summary);
        return new AmapToolResponse(true, capability, "amap-live", Summary: Limit(summary, 6000));
    }

    private async Task<(bool Success, AmapMcpResponse? Response, AmapToolResponse? Error)> InvokeAsync(
        string capability,
        IReadOnlyList<string> tools,
        IReadOnlyDictionary<string, object?> arguments,
        AmapQuotaKind quotaKind,
        CancellationToken cancellationToken)
    {
        if (!ReserveTurnCall(out var callError)) return (false, null, callError);
        if (!gateway.IsConfigured)
            return (false, null, Failure(capability, "高德地图能力尚未配置。"));
        if (!await quota.TryConsumeAsync(quotaKind, cancellationToken))
            return (false, null, Failure(capability, "高德地图本月额度保护已触发，本次没有继续调用。"));
        try
        {
            return (true, await gateway.CallAsync(tools, arguments, cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmapCapabilityUnavailableException exception)
        {
            return (false, null, Failure(capability, exception.Message));
        }
        catch (Exception)
        {
            return (false, null, Failure(capability, "高德地图暂时不可用，请稍后再试。"));
        }
    }

    private bool ReserveTurnCall(out AmapToolResponse? error)
    {
        if (Interlocked.Increment(ref _callCount) <= MaxCallsPerTurn)
        {
            error = null;
            return true;
        }
        error = Failure("limit", "本轮最多调用 6 次高德工具，请基于已有结果回答或让用户继续下一轮。");
        return false;
    }

    private void CaptureResult(string capability, string summary)
    {
        summary = Limit(Sanitize(summary), 1200);
        if (summary.Length > 0)
            _results.Add(new AmapResultEvidence(capability, summary, clock.GetUtcNow()));
    }

    private static AmapToolResponse Failure(string capability, string error) =>
        new(false, capability, "amap-live", Error: error);

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] values)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in values)
            if (value is not null) result[key] = value;
        return result;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Coordinate(decimal longitude, decimal latitude) =>
        FormattableString.Invariant($"{longitude:0.######},{latitude:0.######}");

    private static bool ValidCoordinates(decimal latitude, decimal longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private static bool IsValidPlace(AmapPlaceEvidence place) =>
        !string.IsNullOrWhiteSpace(place.CandidateId) && !string.IsNullOrWhiteSpace(place.Name) &&
        ValidCoordinates(place.Latitude, place.Longitude) && place.CoordinateSystem == "GCJ02";

    private AmapPlaceEvidence? ResolveCandidate(string? reference)
    {
        var candidates = _candidates.Values
            .DistinctBy(candidate => candidate.CandidateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var lookup = reference?.Trim() ?? string.Empty;
        if (_candidates.TryGetValue(lookup, out var direct)) return direct;

        // 模型有时会回传 poiId、地点名称，或把半角括号改成全角括号。
        // 这里只在已经由高德检索出的候选中做唯一匹配，不接受模型提供坐标或 URL。
        var normalizedLookup = NormalizePlaceReference(lookup);
        var matches = candidates
            .Where(candidate =>
                string.Equals(candidate.PoiId, lookup, StringComparison.OrdinalIgnoreCase) ||
                NormalizePlaceReference(candidate.Name) == normalizedLookup)
            .Take(2)
            .ToArray();
        if (matches.Length == 1) return matches[0];

        // 高德只返回一个候选时，后续工具无需依赖模型逐字复制内部标识。
        // 多候选绝不走该分支，仍要求模型明确选择，避免导航到错误地点。
        return candidates.Length == 1 ? candidates[0] : _lastUnambiguousCandidate;
    }

    private string? ResolvePoiId(string? reference)
    {
        var lookup = reference?.Trim() ?? string.Empty;
        var place = ResolveCandidate(lookup);
        if (!string.IsNullOrWhiteSpace(place?.PoiId)) return place.PoiId;
        if (_poiReferences.ContainsKey(lookup)) return lookup;

        var normalizedLookup = NormalizePlaceReference(lookup);
        var matches = _poiReferences.Values
            .Where(candidate =>
            {
                var normalizedName = NormalizePlaceReference(candidate.Name);
                return normalizedName == normalizedLookup ||
                    normalizedLookup.Contains(normalizedName, StringComparison.OrdinalIgnoreCase);
            })
            .DistinctBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0].Id : null;
    }

    private static string NormalizePlaceReference(string value) =>
        value.Normalize(NormalizationForm.FormKC)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var noUrls = HttpUrlRegex().Replace(value, "[外部链接已移除]");
        return Limit(noUrls.Trim(), 6000);
    }

    private static string? ExtractTrustedAmapUrl(string value)
    {
        foreach (Match match in HttpUrlRegex().Matches(value))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', ')', ']', '}', '\"', '\'');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                continue;
            if (uri.Host.Equals("uri.amap.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("m.amap.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".amap.com", StringComparison.OrdinalIgnoreCase))
                return uri.AbsoluteUri;
        }
        return null;
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    [GeneratedRegex(@"https?://[^\s<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpUrlRegex();
}

internal sealed record AmapPoiReference(string Id, string Name, string? Address);

internal static class AmapPayloadNormalizer
{
    public static IReadOnlyList<AmapPoiReference> ReadPoiReferences(AmapMcpResponse response)
    {
        var roots = ReadRoots(response);
        return roots.SelectMany(FindPoiReferenceElements)
            .Select(element => new AmapPoiReference(
                FirstString(element, "id", "poiid", "poiId")!,
                FirstString(element, "name")!,
                FirstString(element, "address", "formatted_address")))
            .DistinctBy(reference => reference.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<AmapPlaceEvidence> ReadPlaces(AmapMcpResponse response)
    {
        var roots = ReadRoots(response);
        if (roots.Count == 0) return [];
        var candidates = roots.SelectMany(FindCandidateElements).ToArray();
        var result = new List<AmapPlaceEvidence>();
        foreach (var element in candidates)
        {
            if (!TryCoordinates(element, out var longitude, out var latitude)) continue;
            var name = FirstString(element, "name", "formatted_address", "address", "district") ?? "高德地点";
            var address = FirstString(element, "address", "formatted_address");
            var poiId = FirstString(element, "id", "poiid", "poiId");
            var candidateId = !string.IsNullOrWhiteSpace(poiId)
                ? poiId
                : CreateCandidateId(name, address, latitude, longitude);
            result.Add(new AmapPlaceEvidence(
                candidateId,
                poiId,
                name,
                address,
                FirstString(element, "pname", "province"),
                FirstString(element, "cityname", "city"),
                FirstString(element, "adname", "district"),
                latitude,
                longitude));
        }
        return result.DistinctBy(x => x.CandidateId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<JsonElement> ReadRoots(AmapMcpResponse response)
    {
        var roots = new List<JsonElement>();
        if (response.StructuredContent is { } structured) roots.Add(structured);
        if (TryReadJson(response.Text) is { } textRoot) roots.Add(textRoot);
        return roots;
    }

    private static JsonElement? TryReadJson(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0) return null;
        var objectStart = text.IndexOf('{');
        var arrayStart = text.IndexOf('[');
        var start = objectStart < 0 ? arrayStart : arrayStart < 0 ? objectStart : Math.Min(objectStart, arrayStart);
        if (start < 0) return null;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text[start..]);
            var reader = new Utf8JsonReader(bytes);
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> FindCandidateElements(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var candidate in FindCandidateElements(item)) yield return candidate;
            yield break;
        }
        if (element.ValueKind != JsonValueKind.Object) yield break;

        if (HasCoordinates(element)) yield return element;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                foreach (var candidate in FindCandidateElements(property.Value)) yield return candidate;
            }
            else if (property.Value.ValueKind == JsonValueKind.String &&
                     TryReadJson(property.Value.GetString()) is { } nested)
            {
                foreach (var candidate in FindCandidateElements(nested)) yield return candidate;
            }
        }
    }

    private static IEnumerable<JsonElement> FindPoiReferenceElements(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var candidate in FindPoiReferenceElements(item)) yield return candidate;
            yield break;
        }
        if (element.ValueKind != JsonValueKind.Object) yield break;

        if (!string.IsNullOrWhiteSpace(FirstString(element, "id", "poiid", "poiId")) &&
            !string.IsNullOrWhiteSpace(FirstString(element, "name")))
            yield return element;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                foreach (var candidate in FindPoiReferenceElements(property.Value)) yield return candidate;
            }
            else if (property.Value.ValueKind == JsonValueKind.String &&
                     TryReadJson(property.Value.GetString()) is { } nested)
            {
                foreach (var candidate in FindPoiReferenceElements(nested)) yield return candidate;
            }
        }
    }

    private static bool HasCoordinates(JsonElement element) =>
        TryProperty(element, "location", out _) ||
        (TryProperty(element, "longitude", out _) && TryProperty(element, "latitude", out _));

    private static bool TryCoordinates(JsonElement element, out decimal longitude, out decimal latitude)
    {
        longitude = latitude = 0;
        if (TryProperty(element, "location", out var location))
        {
            if (location.ValueKind == JsonValueKind.String)
            {
                var parts = location.GetString()?.Split(',');
                if (parts is { Length: 2 } &&
                    decimal.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude) &&
                    decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude))
                    return Valid(latitude, longitude);
            }
            if (location.ValueKind == JsonValueKind.Object &&
                TryDecimal(location, out longitude, "longitude", "lng", "lon") &&
                TryDecimal(location, out latitude, "latitude", "lat"))
                return Valid(latitude, longitude);
        }
        return TryDecimal(element, out longitude, "longitude", "lng", "lon") &&
            TryDecimal(element, out latitude, "latitude", "lat") && Valid(latitude, longitude);
    }

    private static bool TryDecimal(JsonElement element, out decimal value, params string[] names)
    {
        value = 0;
        foreach (var name in names)
        {
            if (!TryProperty(element, name, out var property)) continue;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value)) return true;
            if (property.ValueKind == JsonValueKind.String &&
                decimal.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
        }
        return false;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryProperty(element, name, out var value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Array or JsonValueKind.Object) continue;
            var result = value.ToString().Trim();
            if (result.Length > 0 && result != "[]") return result;
        }
        return null;
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool Valid(decimal latitude, decimal longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private static string CreateCandidateId(string name, string? address, decimal latitude, decimal longitude)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"{name}|{address}|{latitude:0.######}|{longitude:0.######}")));
        return $"amap-{Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant()}";
    }
}
