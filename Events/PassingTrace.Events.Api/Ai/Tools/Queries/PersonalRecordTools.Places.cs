using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using PassingTrace.Events.Api.Ai.Evidence;

namespace PassingTrace.Events.Api.Ai.Tools.Queries;

public sealed partial class PersonalRecordTools
{
    public long? ResolvePreferredNavigationLocationId(string answer)
    {
        foreach (var record in _recordEvidence)
        {
            if (!answer.Contains($"[Event #{record.EventId}]", StringComparison.OrdinalIgnoreCase)) continue;
            var citedPlace = _placeEvidence.FirstOrDefault(place => place.EventId == record.EventId);
            if (citedPlace is not null) return citedPlace.LocationId;
        }

        var distinctPlaces = _placeEvidence.DistinctBy(place => place.LocationId).Take(2).ToArray();
        return distinctPlaces.Length == 1 ? distinctPlaces[0].LocationId : null;
    }

    [Description("搜索当前用户已确认的历史地点，不接受 userId。")]
    public async Task<IReadOnlyList<PlaceEvidence>> SearchMyPlacesAsync([MaxLength(8000)] string query, [Range(1, 20)] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        limit = Math.Clamp(limit, 1, 20);
        var retrievedEventIds = _recordEvidence.Select(x => x.EventId).Distinct().Take(limit).ToArray();
        var values = await queries.SearchPlacesAsync(userId, query, retrievedEventIds, limit, cancellationToken);
        var places = values.Select(x => new PlaceEvidence(x.LocationId, x.EventId, x.EventTitle, x.Name, x.Address, x.AdCode, x.HappenedAt, 1)).ToArray();
        foreach (var place in places) { _retrievedLocationIds.Add(place.LocationId); _retrievedEventIds.Add(place.EventId); }
        _placeEvidence.AddRange(places);
        return places;
    }

    [Description("读取本轮已经检索到的历史地点证据。")]
    public Task<IReadOnlyList<PlaceEvidence>> GetMyPlaceEvidenceAsync([MaxLength(20)] IReadOnlyList<long> locationIds,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlaceEvidence>>(
            _placeEvidence.Where(x => locationIds.Contains(x.LocationId) && _retrievedLocationIds.Contains(x.LocationId)).ToArray());

    [Description("为本轮已检索且有可信坐标的地点生成结构化导航目标。")]
    public async Task<AssistantAction?> GetNavigationTargetAsync(long locationId, CancellationToken cancellationToken = default)
    {
        if (!_retrievedLocationIds.Contains(locationId)) return null;
        var userId = currentUser.UserId;
        var target = await queries.FindNavigationLocationAsync(userId, locationId, cancellationToken);
        if (target is null) return null;
        _navigationTarget = new AssistantAction(
            "amap-navigation", "amap", $"导航到{target.Name}", target.Name, target.Address,
            target.Latitude, target.Longitude, "GCJ02", target.ProviderPoiId, "personal-record",
            target.EventId, target.LocationId);
        return _navigationTarget;
    }
}
