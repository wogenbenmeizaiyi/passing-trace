using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PassingTrace.Events.Api.Ai.Assistant.Context;
using PassingTrace.Events.Api.Places;

namespace PassingTrace.Events.Api.Ai.Amap;

public sealed class AmapCoordinateConverter(HttpClient client, IOptions<AmapOptions> options, IAmapQuotaGuard quota)
{
    public async Task<(decimal Latitude, decimal Longitude)> ConvertAsync(decimal latitude, decimal longitude,
        CancellationToken cancellationToken)
    {
        var key = options.Value.WebServiceKey;
        if (string.IsNullOrWhiteSpace(key)) throw AssistantLocationException.ConversionUnavailable();
        try
        {
            if (!await quota.TryConsumeAsync(AmapQuotaKind.Lbs, cancellationToken))
                throw AssistantLocationException.ConversionUnavailable();
            var coordinates = FormattableString.Invariant($"{longitude:0.######},{latitude:0.######}");
            using var response = await client.GetAsync(
                $"/v3/assistant/coordinate/convert?coordsys=gps&output=json&locations={Uri.EscapeDataString(coordinates)}&key={Uri.EscapeDataString(key)}",
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;
            if (root.GetProperty("status").GetString() != "1") throw AssistantLocationException.ConversionUnavailable();
            var result = root.GetProperty("locations").GetString()?.Split(',');
            if (result is not { Length: 2 } ||
                !decimal.TryParse(result[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
                !decimal.TryParse(result[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                lat is < -90 or > 90 || lon is < -180 or > 180)
                throw AssistantLocationException.ConversionUnavailable();
            return (lat, lon);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Never propagate an HTTP exception containing the key or the user's coordinates to logs.
            throw AssistantLocationException.ConversionUnavailable();
        }
    }
}
