using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PassingTrace.Events.Api.Security;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Places;

[ApiController]
[Authorize]
[Route("api/v1/places")]
public sealed class PlacesController(AmapPlaceService places) : ControllerBase
{
    [HttpPost("search")]
    public async Task<ActionResult<IReadOnlyList<PlaceCandidateResponse>>> SearchAsync(
        PlaceSearchRequest request, CancellationToken cancellationToken) =>
        Ok(await places.SearchAsync(request, cancellationToken));
}

[ApiController]
[Authorize]
[Route("api/v1/events/{eventId:long}/locations")]
public sealed class EventLocationsController(TraceDbContext db) : ControllerBase
{
    [HttpGet("{locationId:long}/navigation-target")]
    public async Task<ActionResult<NavigationTargetResponse>> NavigationTargetAsync(
        long eventId, long locationId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var target = await (from location in db.EventLocations.AsNoTracking()
                            join evt in db.Events.AsNoTracking() on location.EventId equals evt.Id
                            where evt.Id == eventId && evt.UserId == userId && evt.DeletedAt == null &&
                                  location.Id == locationId && location.UserId == userId && location.UserConfirmed &&
                                  location.SourceRevision == evt.CurrentSourceRevision && location.Latitude != null &&
                                  location.Longitude != null && location.CoordinateSystem == "GCJ02"
                            select new NavigationTargetResponse(evt.Id, location.Id, location.Name,
                                location.Latitude!.Value, location.Longitude!.Value, location.CoordinateSystem,
                                location.ProviderPoiId)).FirstOrDefaultAsync(cancellationToken);
        return target is null ? NotFound() : Ok(target);
    }
}
