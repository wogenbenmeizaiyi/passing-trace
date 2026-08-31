using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Media;

namespace PassingTrace.Events.Api.Events;

/// <summary>
/// Event 应用编排：封装创建、查询、修改 Source 与软删除用例，
/// 不直接接触 DbContext。
/// </summary>
/// 
public sealed class EventService
{
    private readonly IEventRepository _repository;
    private readonly TimeProvider _clock;
    private readonly IEventMediaService _mediaService;
    private readonly IAnalysisOutbox _outbox;

    public EventService(IEventRepository repository, TimeProvider clock)
        : this(repository, clock, new NoopEventMediaService(), new NoopAnalysisOutbox())
    {
    }

    public EventService(
        IEventRepository repository,
        TimeProvider clock,
        IEventMediaService mediaService,
        IAnalysisOutbox outbox)
    {
        _repository = repository;
        _clock = clock;
        _mediaService = mediaService;
        _outbox = outbox;
    }


    /// <summary>创建 Event 并写入初始 Source 修订，幂等键冲突时复用或拒绝。</summary>
    public async Task<Event> CreateAsync(
        CreateEventCommand command,
        CancellationToken cancellationToken)
    {
        var media = await _mediaService.ResolveAsync(command.UserId, command.MediaIds, cancellationToken);
        EnsureContent(command.Title, command.RawContent, media.Count);

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await _repository.FindByIdempotencyKeyAsync(
                command.UserId,
                command.IdempotencyKey,
                cancellationToken);

            if (existing is not null)
            {
                if (!MatchesContent(existing, command))
                {
                    throw new IdempotencyConflictException(command.IdempotencyKey);
                }

                return existing;
            }
        }

        var now = _clock.GetUtcNow();
        var evt = Event.Create(
            command.UserId,
            command.Kind,
            command.Title,
            command.RawContent,
            ToUtc(command.HappenedAt),
            ToUtc(command.PlannedAt),
            NormalizeTimezone(command.Timezone),
            command.IdempotencyKey,
            now);

        var revision = SourceRevision.Create(
            evt.Id,
            1,
            command.Title,
            command.RawContent,
            ToUtc(command.HappenedAt),
            ToUtc(command.PlannedAt),
            now);
        evt.SourceRevisions.Add(revision);
        _mediaService.ReplaceCurrent(evt, revision, media, now);
        ApplyRevisionMetadata(evt, revision, command.UserId, command.Classification, command.Locations, now);
        AddBaseSearchIndex(evt, revision, now);
        _outbox.EnqueueEvent(evt, 1, now);
        await _outbox.IncrementWatermarkAsync(command.UserId, now, cancellationToken);

        _repository.Add(evt);
        await _repository.SaveChangesAsync(cancellationToken);
        return evt;
    }

    /// <summary>按所有权查询 Event 详情。</summary>
    public Task<Event?> GetAsync(
        long userId,
        long eventId,
        CancellationToken cancellationToken)
    {
        return _repository.FindAsync(userId, eventId, cancellationToken);
    }

    /// <summary>按条件查询 Event 列表。</summary>
    public Task<IReadOnlyList<Event>> ListAsync(
        EventListQuery query,
        CancellationToken cancellationToken)
    {
        return _repository.ListAsync(query, cancellationToken);
    }

    /// <summary>修改 Source：递增修订版本并追加历史快照。</summary>
    public async Task<Event> UpdateSourceAsync(
        UpdateEventCommand command,
        CancellationToken cancellationToken)
    {
        var media = await _mediaService.ResolveAsync(command.UserId, command.MediaIds, cancellationToken);
        EnsureContent(command.Title, command.RawContent, media.Count);

        var evt = await _repository.FindAsync(
                command.UserId,
                command.EventId,
                cancellationToken)
            ?? throw new EventNotFoundException(command.UserId, command.EventId);

        EnsureActive(evt, command.UserId);
        EnsureVersion(evt, command.ExpectedVersion);

        var now = _clock.GetUtcNow();
        var nextRevision = evt.CurrentSourceRevision + 1;
        var previousRevision = evt.SourceRevisions.Single(x => x.Revision == evt.CurrentSourceRevision);

        evt.Title = command.Title;
        evt.RawContent = command.RawContent;
        evt.HappenedAt = ToUtc(command.HappenedAt);
        evt.PlannedAt = ToUtc(command.PlannedAt);
        evt.Timezone = NormalizeTimezone(command.Timezone);
        evt.CurrentSourceRevision = nextRevision;
        evt.UpdatedAt = now;

        var revision = SourceRevision.Create(
            evt.Id,
            nextRevision,
            command.Title,
            command.RawContent,
            ToUtc(command.HappenedAt),
            ToUtc(command.PlannedAt),
            now);
        evt.SourceRevisions.Add(revision);
        _mediaService.ReplaceCurrent(evt, revision, media, now);
        var classification = command.Classification ?? CopyClassification(previousRevision);
        var locations = command.Locations ?? CopyLocations(previousRevision);
        foreach (var label in evt.LabelIndexes.Where(x => x.IsCurrent)) label.IsCurrent = false;
        ApplyRevisionMetadata(evt, revision, command.UserId, classification, locations, now);
        foreach (var index in evt.SearchIndexes.Where(x => x.IsCurrent)) index.IsCurrent = false;
        AddBaseSearchIndex(evt, revision, now);
        _outbox.EnqueueEvent(evt, nextRevision, now);
        await _outbox.IncrementWatermarkAsync(command.UserId, now, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);
        return evt;
    }

    /// <summary>软删除 Event，进入可恢复删除期。</summary>
    public async Task SoftDeleteAsync(
        long userId,
        long eventId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        var evt = await _repository.FindAsync(userId, eventId, cancellationToken)
            ?? throw new EventNotFoundException(userId, eventId);

        if (evt.DeletedAt is not null)
        {
            return;
        }

        EnsureVersion(evt, expectedVersion);

        var now = _clock.GetUtcNow();
        evt.DeletedAt = now;
        evt.UpdatedAt = now;

        _outbox.EnqueueEvent(evt, evt.CurrentSourceRevision, now, messageType: "event.deleted");
        await _outbox.IncrementWatermarkAsync(userId, now, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureContent(string? title, string? rawContent, int mediaCount)
    {
        if (string.IsNullOrWhiteSpace(title) &&
            string.IsNullOrWhiteSpace(rawContent) &&
            mediaCount == 0)
        {
            throw new DomainValidationException("标题、原文和附件不能同时为空。");
        }
    }

    private static void EnsureActive(Event evt, long userId)
    {
        if (evt.DeletedAt is not null)
        {
            throw new EventNotFoundException(userId, evt.Id);
        }
    }

    private static void EnsureVersion(Event evt, uint expectedVersion)
    {
        if (evt.RowVersion != expectedVersion)
        {
            throw new ConcurrencyException("事件版本已过期，请刷新后重试。");
        }
    }

    private static bool MatchesContent(Event evt, CreateEventCommand command)
    {
        if (!(evt.EventKind == command.Kind &&
            evt.Title == command.Title &&
            evt.RawContent == command.RawContent &&
            evt.HappenedAt == ToUtc(command.HappenedAt) &&
            evt.PlannedAt == ToUtc(command.PlannedAt) &&
            evt.MediaAssets.OrderBy(x => x.SortOrder).Select(x => x.MediaAssetId)
                .SequenceEqual(command.MediaIds ?? []))) return false;
        var revision = evt.SourceRevisions.SingleOrDefault(x => x.Revision == evt.CurrentSourceRevision);
        if (revision is null) return command.Classification is null && command.Locations is null;
        var expected = BuildLabels(new SourceRevision(), command.UserId,
            command.Classification ?? new ClassificationInput(null, [], []));
        var actualLabelKeys = revision.Labels.OrderBy(x => x.Type).ThenBy(x => x.Decision).ThenBy(x => x.NormalizedValue)
            .Select(x => $"{x.Type}:{x.Decision}:{x.TaxonomyKey}:{x.NormalizedValue}");
        var expectedLabelKeys = expected.OrderBy(x => x.Type).ThenBy(x => x.Decision).ThenBy(x => x.NormalizedValue)
            .Select(x => $"{x.Type}:{x.Decision}:{x.TaxonomyKey}:{x.NormalizedValue}");
        if (!actualLabelKeys.SequenceEqual(expectedLabelKeys)) return false;
        var requestedLocations = command.Locations ?? [];
        return revision.Locations.Count == requestedLocations.Count && revision.Locations.Zip(requestedLocations)
            .All(x => x.First.Name == x.Second.Name && x.First.ProviderPoiId == x.Second.ProviderPoiId &&
                x.First.Latitude == x.Second.Latitude && x.First.Longitude == x.Second.Longitude);
    }

    private static void ApplyRevisionMetadata(
        Event evt,
        SourceRevision revision,
        long userId,
        ClassificationInput? classification,
        IReadOnlyList<EventLocationInput>? locationInputs,
        DateTimeOffset now)
    {
        classification ??= new ClassificationInput(null, [], []);
        var manualLabels = BuildLabels(revision, userId, classification);
        revision.Labels.AddRange(manualLabels);

        var included = manualLabels.Where(x => x.Decision == SourceLabelDecision.Include).ToArray();
        foreach (var label in included)
        {
            evt.LabelIndexes.Add(new EventLabelIndex
            {
                UserId = userId,
                Event = evt,
                SourceRevision = revision.Revision,
                Type = label.Type,
                Origin = EventLabelOrigin.Manual,
                TaxonomyKey = label.TaxonomyKey,
                DisplayName = label.DisplayName,
                NormalizedValue = label.NormalizedValue,
                IsCurrent = true,
                CreatedAt = now,
            });
        }

        var locations = locationInputs ?? [];
        if (locations.Count > 1) throw new DomainValidationException("第一版每条记录最多只能保存一个地点。");
        foreach (var input in locations)
        {
            var location = BuildLocation(evt, revision, userId, input, now);
            revision.Locations.Add(location);
        }
    }

    private static IReadOnlyList<SourceRevisionLabel> BuildLabels(
        SourceRevision revision,
        long userId,
        ClassificationInput input)
    {
        var labels = new List<SourceRevisionLabel>();
        if (!string.IsNullOrWhiteSpace(input.PrimaryCategoryKey))
        {
            var key = input.PrimaryCategoryKey.Trim().ToLowerInvariant();
            if (!EventTaxonomy.IsCategory(key)) throw new DomainValidationException("未知的主分类。");
            labels.Add(NewLabel(revision, userId, EventLabelType.PrimaryCategory, SourceLabelDecision.Include,
                key, EventTaxonomy.CategoryLabel(key), 0));
        }

        var tags = input.Tags ?? [];
        if (tags.Count > 10) throw new DomainValidationException("一条记录最多只能设置 10 个行为标签。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var tag in tags)
        {
            var hasKey = !string.IsNullOrWhiteSpace(tag.TaxonomyKey);
            var hasName = !string.IsNullOrWhiteSpace(tag.Name);
            if (hasKey == hasName) throw new DomainValidationException("行为标签必须且只能指定 taxonomyKey 或自定义名称。");
            string? key = null;
            string display;
            if (hasKey)
            {
                key = tag.TaxonomyKey!.Trim().ToLowerInvariant();
                if (!EventTaxonomy.IsBehaviorTag(key)) throw new DomainValidationException("未知的行为标签。");
                display = EventTaxonomy.BehaviorTagLabel(key);
            }
            else display = EventTaxonomy.NormalizeCustomTag(tag.Name!);
            var normalized = EventTaxonomy.NormalizedValue(display);
            if (!seen.Add(normalized)) continue;
            labels.Add(NewLabel(revision, userId, EventLabelType.BehaviorTag, SourceLabelDecision.Include,
                key, display, order++));
        }

        foreach (var rawKey in input.SuppressedAiTagKeys ?? [])
        {
            var key = rawKey.Trim().ToLowerInvariant();
            if (!EventTaxonomy.IsBehaviorTag(key)) throw new DomainValidationException("未知的被排除 AI 标签。");
            var display = EventTaxonomy.BehaviorTagLabel(key);
            if (seen.Contains(EventTaxonomy.NormalizedValue(display))) continue;
            labels.Add(NewLabel(revision, userId, EventLabelType.BehaviorTag, SourceLabelDecision.Exclude,
                key, display, order++));
        }
        return labels;
    }

    private static SourceRevisionLabel NewLabel(SourceRevision revision, long userId, EventLabelType type,
        SourceLabelDecision decision, string? key, string display, int order) => new()
        {
            SourceRevision = revision,
            UserId = userId,
            Type = type,
            Decision = decision,
            TaxonomyKey = key,
            DisplayName = display,
            NormalizedValue = EventTaxonomy.NormalizedValue(display),
            SortOrder = order,
        };

    private static EventLocation BuildLocation(Event evt, SourceRevision revision, long userId,
        EventLocationInput input, DateTimeOffset now)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 256) throw new DomainValidationException("地点名称长度必须为 1 到 256 个字符。");
        if (input.Latitude.HasValue != input.Longitude.HasValue)
            throw new DomainValidationException("地点经纬度必须同时提供。");
        if (input.Latitude is < -90 or > 90 || input.Longitude is < -180 or > 180)
            throw new DomainValidationException("地点经纬度超出有效范围。");
        var coordinateSystem = input.Latitude.HasValue
            ? (string.Equals(input.CoordinateSystem, "GCJ02", StringComparison.OrdinalIgnoreCase) ? "GCJ02" :
                throw new DomainValidationException("带坐标的地点必须使用 GCJ02 坐标系。"))
            : "UNKNOWN";
        return new EventLocation
        {
            UserId = userId,
            Event = evt,
            Revision = revision,
            SourceRevision = revision.Revision,
            Name = name,
            Address = Limit(input.Address, 512),
            Province = Limit(input.Province, 128),
            City = Limit(input.City, 128),
            District = Limit(input.District, 128),
            AdCode = Limit(input.AdCode, 16),
            ProviderPoiId = Limit(input.ProviderPoiId, 128),
            PoiType = Limit(input.PoiType, 128),
            Latitude = input.Latitude,
            Longitude = input.Longitude,
            AccuracyMeters = input.AccuracyMeters,
            CoordinateSystem = coordinateSystem,
            Source = input.Source,
            CapturedAt = input.CapturedAt?.ToUniversalTime(),
            UserConfirmed = true,
            CreatedAt = now,
        };
    }

    private static ClassificationInput CopyClassification(SourceRevision revision) => new(
        revision.Labels.FirstOrDefault(x => x.Type == EventLabelType.PrimaryCategory && x.Decision == SourceLabelDecision.Include)?.TaxonomyKey,
        revision.Labels.Where(x => x.Type == EventLabelType.BehaviorTag && x.Decision == SourceLabelDecision.Include)
            .OrderBy(x => x.SortOrder).Select(x => new ManualTagInput(x.TaxonomyKey, x.TaxonomyKey is null ? x.DisplayName : null)).ToArray(),
        revision.Labels.Where(x => x.Type == EventLabelType.BehaviorTag && x.Decision == SourceLabelDecision.Exclude)
            .Select(x => x.TaxonomyKey!).ToArray());

    private static IReadOnlyList<EventLocationInput> CopyLocations(SourceRevision revision) => revision.Locations
        .Select(x => new EventLocationInput(x.Name, x.Address, x.Province, x.City, x.District, x.AdCode,
            x.ProviderPoiId, x.PoiType, x.Latitude, x.Longitude, x.AccuracyMeters, x.CoordinateSystem, x.Source, x.CapturedAt))
        .ToArray();

    private static void AddBaseSearchIndex(Event evt, SourceRevision revision, DateTimeOffset now)
    {
        var labels = evt.LabelIndexes.Where(x => x.SourceRevision == revision.Revision && x.IsCurrent)
            .Select(x => x.DisplayName);
        var locations = revision.Locations.SelectMany(x => new[] { x.Name, x.Address, x.City, x.District });
        var retrieval = string.Join('\n', new[] { revision.Title, revision.RawContent }
            .Concat(labels).Concat(locations).Where(x => !string.IsNullOrWhiteSpace(x))!);
        evt.SearchIndexes.Add(new PassingTrace.Core.Ai.EventSearchIndex
        {
            Event = evt,
            UserId = evt.UserId,
            SourceRevision = revision.Revision,
            Title = revision.Title ?? string.Empty,
            RawContent = revision.RawContent ?? string.Empty,
            RetrievalText = retrieval,
            IsCurrent = true,
            UpdatedAt = now,
        });
    }

    private static string? Limit(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];

    /// <summary>归一化为 UTC，满足 Npgsql timestamp with time zone 只接受 Offset=0 的要求。</summary>
    private static DateTimeOffset? ToUtc(DateTimeOffset? value) => value?.ToUniversalTime();

    private static string NormalizeTimezone(string timezone)
    {
        return string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone;
    }

    private sealed class NoopEventMediaService : IEventMediaService
    {
        public Task<IReadOnlyList<MediaAsset>> ResolveAsync(long userId, IReadOnlyList<Guid>? mediaIds, CancellationToken cancellationToken)
        {
            if (mediaIds is { Count: > 0 })
            {
                throw new InvalidOperationException("未配置附件服务。");
            }
            return Task.FromResult<IReadOnlyList<MediaAsset>>([]);
        }

        public void ReplaceCurrent(Event evt, SourceRevision revision, IReadOnlyList<MediaAsset> media, DateTimeOffset now)
        {
        }
    }

    private sealed class NoopAnalysisOutbox : IAnalysisOutbox
    {
        public void EnqueueEvent(Event evt, int sourceRevision, DateTimeOffset now, int priority = 100, string messageType = "event.analyze")
        {
        }

        public void EnqueueMedia(long userId, Guid mediaAssetId, DateTimeOffset now, int priority = 100)
        {
        }

        public Task IncrementWatermarkAsync(long userId, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
