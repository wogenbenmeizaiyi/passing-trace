using PassingTrace.Core.Events;

namespace PassingTrace.Events.Api.Events;

using PassingTrace.Events.Api.Media;

/// <summary>创建 Event 的应用命令。</summary>
public sealed record CreateEventCommand(
    long UserId,
    EventKind Kind,
    string? Title,
    string? RawContent,
    DateTimeOffset? HappenedAt,
    DateTimeOffset? PlannedAt,
    string Timezone,
    string? IdempotencyKey,
    IReadOnlyList<Guid>? MediaIds = null,
    ClassificationInput? Classification = null,
    IReadOnlyList<EventLocationInput>? Locations = null);

/// <summary>修改 Event Source 的应用命令。</summary>
public sealed record UpdateEventCommand(
    long UserId,
    long EventId,
    uint ExpectedVersion,
    string? Title,
    string? RawContent,
    DateTimeOffset? HappenedAt,
    DateTimeOffset? PlannedAt,
    string Timezone,
    IReadOnlyList<Guid>? MediaIds = null,
    ClassificationInput? Classification = null,
    IReadOnlyList<EventLocationInput>? Locations = null);

/// <summary>创建 Event 的 HTTP 请求体。</summary>
public sealed record CreateEventRequest(
    EventKind Kind,
    string? Title,
    string? RawContent,
    DateTimeOffset? HappenedAt,
    DateTimeOffset? PlannedAt,
    string? Timezone,
    IReadOnlyList<Guid>? MediaIds = null,
    ClassificationInput? Classification = null,
    IReadOnlyList<EventLocationInput>? Locations = null);

/// <summary>修改 Event Source 的 HTTP 请求体。</summary>
public sealed record UpdateEventRequest(
    string? Title,
    string? RawContent,
    DateTimeOffset? HappenedAt,
    DateTimeOffset? PlannedAt,
    string? Timezone,
    IReadOnlyList<Guid>? MediaIds = null,
    ClassificationInput? Classification = null,
    IReadOnlyList<EventLocationInput>? Locations = null);

public sealed record ClassificationInput(
    string? PrimaryCategoryKey,
    IReadOnlyList<ManualTagInput>? Tags,
    IReadOnlyList<string>? SuppressedAiTagKeys);

public sealed record ManualTagInput(string? TaxonomyKey, string? Name);

public sealed record EventLocationInput(
    string Name,
    string? Address,
    string? Province,
    string? City,
    string? District,
    string? AdCode,
    string? ProviderPoiId,
    string? PoiType,
    decimal? Latitude,
    decimal? Longitude,
    decimal? AccuracyMeters,
    string? CoordinateSystem,
    EventLocationSource Source,
    DateTimeOffset? CapturedAt);

public sealed record EventLabelResponse(
    string? TaxonomyKey,
    string DisplayName,
    string Origin,
    decimal? Confidence);

public sealed record ManualClassificationResponse(
    string? PrimaryCategoryKey,
    IReadOnlyList<ManualTagInput> Tags,
    IReadOnlyList<string> SuppressedAiTagKeys);

public sealed record EffectiveClassificationResponse(
    EventLabelResponse? PrimaryCategory,
    IReadOnlyList<EventLabelResponse> Tags,
    string TaxonomyVersion);

public sealed record EventLocationResponse(
    long Id,
    string Name,
    string? Address,
    string? Province,
    string? City,
    string? District,
    string? AdCode,
    string? ProviderPoiId,
    string? PoiType,
    decimal? Latitude,
    decimal? Longitude,
    decimal? AccuracyMeters,
    string CoordinateSystem,
    EventLocationSource Source,
    DateTimeOffset? CapturedAt);

/// <summary>Event 的 HTTP 响应体。</summary>
public sealed record EventResponse(
    long Id,
    EventKind Kind,
    EventStatus Status,
    string? Title,
    string? RawContent,
    DateTimeOffset? HappenedAt,
    DateTimeOffset? PlannedAt,
    DateTimeOffset? CompletedAt,
    string Timezone,
    Visibility Visibility,
    int SourceRevision,
    uint Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<MediaResponse> Media,
    string SemanticStatus,
    string? SemanticSummary,
    ManualClassificationResponse ManualClassification,
    EffectiveClassificationResponse EffectiveClassification,
    IReadOnlyList<EventLocationResponse> Locations);

/// <summary>Event 列表响应体。</summary>
public sealed record EventListResponse(
    IReadOnlyList<EventResponse> Items,
    long? NextCursor);
