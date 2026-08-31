namespace PassingTrace.Core.Events;

public enum EventLocationSource
{
    CurrentPosition = 1,
    NearbyPoi = 2,
    KeywordSearch = 3,
    ManualText = 4,
}

public sealed class EventLocation
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long EventId { get; set; }
    public long SourceRevisionId { get; set; }
    public int SourceRevision { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Province { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? AdCode { get; set; }
    public string? ProviderPoiId { get; set; }
    public string? PoiType { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? AccuracyMeters { get; set; }
    public string CoordinateSystem { get; set; } = "UNKNOWN";
    public EventLocationSource Source { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public bool UserConfirmed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public SourceRevision Revision { get; set; } = null!;
    public Event Event { get; set; } = null!;
}

/// <summary>用户范围内可重建的历史地点聚合索引。</summary>
public sealed class UserPlace
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string CanonicalKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? AdCode { get; set; }
    public string? ProviderPoiId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string CoordinateSystem { get; set; } = "UNKNOWN";
    public int VisitCount { get; set; }
    public DateTimeOffset FirstVisitedAt { get; set; }
    public DateTimeOffset LastVisitedAt { get; set; }
    public string RetrievalText { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
