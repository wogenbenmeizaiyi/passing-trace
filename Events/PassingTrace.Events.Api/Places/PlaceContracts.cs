namespace PassingTrace.Events.Api.Places;

public sealed record PlaceSearchRequest(
    string Mode,
    string? Query,
    decimal? Latitude,
    decimal? Longitude,
    int? RadiusMeters,
    string? CityAdCode);

public sealed record PlaceCandidateResponse(
    string Provider,
    string PoiId,
    string Name,
    string? Address,
    string? Province,
    string? City,
    string? District,
    string? AdCode,
    string? PoiType,
    decimal Latitude,
    decimal Longitude,
    string CoordinateSystem,
    int? DistanceMeters);

public sealed record NavigationTargetResponse(
    long EventId,
    long LocationId,
    string Name,
    decimal Latitude,
    decimal Longitude,
    string CoordinateSystem,
    string? ProviderPoiId);
