using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Events;

namespace PassingTrace.Events.Api.Places;

public sealed class AmapPlaceService(HttpClient httpClient, IOptions<AmapOptions> options)
{
    public async Task<IReadOnlyList<PlaceCandidateResponse>> SearchAsync(PlaceSearchRequest request, CancellationToken cancellationToken)
    {
        var key = options.Value.WebServiceKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("尚未配置高德 Web 服务 Key。");
        var mode = request.Mode?.Trim().ToLowerInvariant();
        if (mode is not ("nearby" or "keyword")) throw new DomainValidationException("地点搜索 mode 只支持 nearby 或 keyword。");
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            throw new DomainValidationException("地点搜索坐标超出有效范围。");
        if (mode == "nearby" && (!request.Latitude.HasValue || !request.Longitude.HasValue))
            throw new DomainValidationException("附近搜索必须提供经纬度。");
        if (mode == "keyword" && string.IsNullOrWhiteSpace(request.Query))
            throw new DomainValidationException("关键词搜索必须提供 query。");

        var values = new Dictionary<string, string?>
        {
            ["key"] = key,
            ["output"] = "json",
            ["offset"] = "20",
            ["page"] = "1",
            ["extensions"] = "base",
            ["city"] = request.CityAdCode,
        };
        string path;
        if (mode == "nearby")
        {
            path = "/v3/place/around";
            values["location"] = FormattableString.Invariant($"{request.Longitude:0.######},{request.Latitude:0.######}");
            values["radius"] = Math.Clamp(request.RadiusMeters ?? 1000, 100, 3000).ToString(CultureInfo.InvariantCulture);
            values["keywords"] = request.Query?.Trim();
        }
        else
        {
            path = "/v3/place/text";
            values["keywords"] = request.Query!.Trim();
        }
        var query = string.Join('&', values.Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
        using var response = await httpClient.GetAsync($"{path}?{query}", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        if (root.GetProperty("status").GetString() != "1")
            throw new InvalidOperationException($"高德地点服务暂不可用：{root.GetProperty("infocode").GetString()}");
        var result = new List<PlaceCandidateResponse>();
        foreach (var poi in root.GetProperty("pois").EnumerateArray())
        {
            var location = GetString(poi, "location")?.Split(',');
            if (location is not { Length: 2 } || !decimal.TryParse(location[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
                !decimal.TryParse(location[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            result.Add(new PlaceCandidateResponse("amap", GetString(poi, "id") ?? string.Empty,
                GetString(poi, "name") ?? "未知地点", GetString(poi, "address"), GetString(poi, "pname"),
                GetString(poi, "cityname"), GetString(poi, "adname"), GetString(poi, "adcode"),
                GetString(poi, "type"), lat, lon, "GCJ02", int.TryParse(GetString(poi, "distance"), out var d) ? d : null));
        }
        return result;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Array) return null;
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) || text == "[]" ? null : text;
    }
}
