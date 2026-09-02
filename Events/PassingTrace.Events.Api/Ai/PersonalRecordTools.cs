using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;
using PassingTrace.Infrastructure;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace PassingTrace.Events.Api.Ai;

/// <summary>Agent 可调用的四个只读 Typed Tools。所有查询首先强制 user_id 过滤。</summary>
public sealed class PersonalRecordTools(
    TraceDbContext db,
    CurrentUserContext currentUser,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
{
    private readonly HashSet<long> _retrievedEventIds = [];
    private readonly List<RecordEvidence> _recordEvidence = [];
    private readonly List<MemoryEvidence> _memoryEvidence = [];
    private string? _aggregateEvidence;
    private readonly List<PlaceEvidence> _placeEvidence = [];
    private readonly HashSet<long> _retrievedLocationIds = [];
    private object? _navigationTarget;
    private readonly List<StorylineEvidence> _storylineEvidence = [];
    private readonly HashSet<Guid> _retrievedStorylineIds = [];

    public EvidenceBundle Snapshot => new(
        _recordEvidence.GroupBy(x => x.EventId).Select(x => x.First()).ToArray(),
        _memoryEvidence.GroupBy(x => x.MemoryId).Select(x => x.First()).ToArray(),
        _aggregateEvidence, Places: _placeEvidence.GroupBy(x => x.LocationId).Select(x => x.First()).ToArray(),
        NavigationTarget: _navigationTarget,
        Storylines: _storylineEvidence.GroupBy(x => x.StorylineId).Select(x => x.First()).ToArray());

    [Description("搜索当前登录用户自己的故事线，适合旅行过程、项目阶段、活动纪实和生命周期问题；不接受 userId。")]
    public async Task<IReadOnlyList<StorylineEvidence>> SearchMyStorylinesAsync(
        [Description("自然语言关键词或问题")] string query,
        [Description("故事线主分类 key，可空")] string? category = null,
        [Description("Ongoing 或 Completed，可空")] string? status = null,
        [Description("最多返回 1-10 条")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 10);
        var userId = currentUser.UserId;
        var indexes = db.StorylineSearchIndexes.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsCurrent &&
                db.Storylines.Any(s => s.Id == x.StorylineId && s.UserId == userId && s.DeletedAt == null));
        if (!string.IsNullOrWhiteSpace(category))
        {
            var key = category.Trim().ToLowerInvariant();
            indexes = indexes.Where(x => db.Storylines.Any(s => s.Id == x.StorylineId && s.UserId == userId && s.CategoryKey == key));
        }
        if (Enum.TryParse<StorylineStatus>(status, true, out var parsedStatus))
            indexes = indexes.Where(x => db.Storylines.Any(s => s.Id == x.StorylineId && s.UserId == userId && s.Status == parsedStatus));
        query = query?.Trim() ?? string.Empty;
        var scores = new Dictionary<Guid, double>();
        var recent = await indexes.OrderByDescending(x => x.UpdatedAt).Take(30).Select(x => x.StorylineId).ToListAsync(cancellationToken);
        AddRanking(scores, recent);
        if (query.Length > 0)
        {
            var text = await indexes.OrderByDescending(x => EF.Functions.TrigramsSimilarity(x.RetrievalText, query))
                .Take(30).Select(x => x.StorylineId).ToListAsync(cancellationToken);
            AddRanking(scores, text);
            try
            {
                var generated = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
                var vector = new Vector(generated[0].Vector);
                var semantic = await indexes.Where(x => EF.Property<Vector?>(x, "Embedding") != null)
                    .OrderBy(x => EF.Property<Vector>(x, "Embedding").CosineDistance(vector)).Take(30)
                    .Select(x => x.StorylineId).ToListAsync(cancellationToken);
                AddRanking(scores, semantic);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
        }
        var ids = scores.OrderByDescending(x => x.Value).Take(limit).Select(x => x.Key).ToArray();
        var storylines = await db.Storylines.AsNoTracking().Where(x => x.UserId == userId && ids.Contains(x.Id) && x.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var revisions = await db.StorylineRevisions.AsNoTracking().Include(x => x.Stages)
            .Include(x => x.Nodes).ThenInclude(x => x.Event).ThenInclude(x => x.SourceRevisions)
            .Include(x => x.Edges).Where(x => ids.Contains(x.StorylineId)).AsSplitQuery().ToListAsync(cancellationToken);
        var indexRows = await indexes.Where(x => ids.Contains(x.StorylineId)).ToListAsync(cancellationToken);
        var evidence = ids.Select(id =>
        {
            var story = storylines.Single(x => x.Id == id);
            var revision = revisions.Single(x => x.StorylineId == id && x.Revision == story.CurrentRevision);
            var index = indexRows.Single(x => x.StorylineId == id && x.Revision == story.CurrentRevision);
            var stages = revision.Stages.OrderBy(x => x.SemanticOrder).Select(stage => new StorylineStageEvidence(
                stage.Key, stage.Title, revision.Nodes.Where(x => x.StageKey == stage.Key).OrderBy(x => x.SemanticOrder)
                    .Select(x => PinnedSource(x).Title ?? "无标题记录").ToArray())).ToArray();
            return new StorylineEvidence(id, revision.Revision, story.Title, StorylineTaxonomy.Label(story.CategoryKey),
                story.Status.ToString(), Snippet(index.RetrievalText, query), story.RangeStart, story.RangeEnd, stages,
                revision.Nodes.Where(x => x.Event.DeletedAt == null).Select(x => x.EventId).Distinct().ToArray(), scores[id]);
        }).ToArray();
        foreach (var item in evidence) { _storylineEvidence.Add(item); _retrievedStorylineIds.Add(item.StorylineId); }
        return evidence;
    }

    [Description("读取本轮已经检索到的故事线阶段、拓扑关系和固定记录修订证据。")]
    public async Task<object?> GetMyStorylineEvidenceAsync(Guid storylineId, CancellationToken cancellationToken = default)
    {
        if (!_retrievedStorylineIds.Contains(storylineId)) return null;
        var userId = currentUser.UserId;
        var story = await db.Storylines.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == storylineId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (story is null) return null;
        var revision = await db.StorylineRevisions.AsNoTracking().Include(x => x.Stages)
            .Include(x => x.Nodes).ThenInclude(x => x.Event).ThenInclude(x => x.SourceRevisions)
            .Include(x => x.Edges).FirstAsync(x => x.StorylineId == storylineId && x.Revision == story.CurrentRevision, cancellationToken);
        return new
        {
            storylineId,
            revision = revision.Revision,
            story.Title,
            stages = revision.Stages.OrderBy(x => x.SemanticOrder).Select(x => new { x.Key, x.Title, x.SemanticOrder }),
            nodes = revision.Nodes.OrderBy(x => x.SemanticOrder).Where(x => x.Event.DeletedAt == null)
                .Select(x => new
                {
                    x.Key,
                    x.EventId,
                    x.SourceRevision,
                    title = PinnedSource(x).Title,
                    rawContent = PinnedSource(x).RawContent,
                    occurredAt = PinnedSource(x).HappenedAt ?? PinnedSource(x).PlannedAt,
                    kind = x.Event.EventKind.ToString(),
                    status = x.Event.Status.ToString(),
                    x.StageKey,
                }),
            edges = revision.Edges.Select(x => new { x.SourceNodeKey, x.TargetNodeKey, relation = x.RelationType.ToString(), x.Label }),
        };
    }

    private static SourceRevision PinnedSource(StorylineNode node) =>
        node.Event.SourceRevisions.Single(x => x.Revision == node.SourceRevision);

    private static void AddRanking(Dictionary<Guid, double> scores, IReadOnlyList<Guid> ranking)
    {
        for (var rank = 0; rank < ranking.Count; rank++)
            scores[ranking[rank]] = scores.GetValueOrDefault(ranking[rank]) + 1d / (61 + rank);
    }

    [Description("搜索当前登录用户自己的记录。支持关键词、时间、记录类型、状态、语义类别和地点；返回已排序的证据，不接受 userId。")]
    public async Task<EvidenceBundle> SearchMyRecordsAsync(
        [Description("自然语言关键词或问题")] string query,
        [Description("ISO-8601 起始时间，可空")] string? from = null,
        [Description("ISO-8601 结束时间，可空")] string? to = null,
        [Description("Trace 或 Plan，可空")] string? kind = null,
        [Description("Completed、Planned、Cancelled 等状态，可空")] string? status = null,
        [Description("主分类 taxonomy key，可空")] string? category = null,
        [Description("行为标签 taxonomy key，可空")] string? tag = null,
        [Description("地点名称，可空")] string? location = null,
        [Description("行政区 adCode，可空")] string? adCode = null,
        [Description("中心纬度，可空")] decimal? centerLatitude = null,
        [Description("中心经度，可空")] decimal? centerLongitude = null,
        [Description("半径米数，可空")] int? radiusMeters = null,
        [Description("最多返回 1-20 条")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 20);
        var userId = currentUser.UserId;
        var indexes = db.EventSearchIndexes.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsCurrent);
        if (DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromValue))
        {
            indexes = indexes.Where(x => db.Events.Any(e => e.Id == x.EventId && e.UserId == userId &&
                (e.HappenedAt ?? e.CreatedAt) >= fromValue.ToUniversalTime()));
        }
        if (DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toValue))
        {
            indexes = indexes.Where(x => db.Events.Any(e => e.Id == x.EventId && e.UserId == userId &&
                (e.HappenedAt ?? e.CreatedAt) <= toValue.ToUniversalTime()));
        }
        if (Enum.TryParse<EventKind>(kind, true, out var eventKind))
        {
            indexes = indexes.Where(x => db.Events.Any(e => e.Id == x.EventId && e.UserId == userId && e.EventKind == eventKind));
        }
        if (Enum.TryParse<EventStatus>(status, true, out var eventStatus))
        {
            indexes = indexes.Where(x => db.Events.Any(e => e.Id == x.EventId && e.UserId == userId && e.Status == eventStatus));
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            var key = category.ToLowerInvariant();
            indexes = indexes.Where(x => db.EventLabelIndexes.Any(m => m.UserId == userId && m.EventId == x.EventId &&
                m.IsCurrent && m.Type == EventLabelType.PrimaryCategory && m.TaxonomyKey == key));
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var key = tag.ToLowerInvariant();
            indexes = indexes.Where(x => db.EventLabelIndexes.Any(m => m.UserId == userId && m.EventId == x.EventId &&
                m.IsCurrent && m.Type == EventLabelType.BehaviorTag && m.TaxonomyKey == key));
        }
        if (!string.IsNullOrWhiteSpace(adCode))
            indexes = indexes.Where(x => db.EventLocations.Any(l => l.UserId == userId && l.EventId == x.EventId &&
                l.SourceRevision == x.SourceRevision && l.AdCode == adCode));
        if (centerLatitude.HasValue && centerLongitude.HasValue)
        {
            var radius = Math.Clamp(radiusMeters ?? 1000, 100, 100000);
            var latitudeDelta = (decimal)radius / 111_320m;
            var longitudeDelta = latitudeDelta / (decimal)Math.Max(0.1, Math.Cos((double)centerLatitude.Value * Math.PI / 180));
            var minLat = centerLatitude.Value - latitudeDelta; var maxLat = centerLatitude.Value + latitudeDelta;
            var minLon = centerLongitude.Value - longitudeDelta; var maxLon = centerLongitude.Value + longitudeDelta;
            indexes = indexes.Where(x => db.EventLocations.Any(l => l.UserId == userId && l.EventId == x.EventId &&
                l.SourceRevision == x.SourceRevision && l.Latitude >= minLat && l.Latitude <= maxLat &&
                l.Longitude >= minLon && l.Longitude <= maxLon));
        }
        if (!string.IsNullOrWhiteSpace(location))
        {
            indexes = indexes.Where(x => x.SemanticRunId != null && db.SemanticMentions.Any(m =>
                m.UserId == userId && m.SemanticRunId == x.SemanticRunId && m.Category == "location" &&
                EF.Functions.TrigramsSimilarity(m.NormalizedValue, location) > 0.15));
        }

        var rankings = new List<IReadOnlyList<long>>();
        rankings.Add(await indexes.OrderByDescending(x => x.UpdatedAt).Take(40)
            .Select(x => x.EventId).ToListAsync(cancellationToken));

        query = query?.Trim() ?? string.Empty;
        if (query.Length > 0)
        {
            rankings.Add(await indexes
                .OrderByDescending(x => EF.Functions.TrigramsSimilarity(x.RetrievalText, query))
                .ThenByDescending(x => x.UpdatedAt)
                .Take(40)
                .Select(x => x.EventId)
                .ToListAsync(cancellationToken));
            try
            {
                var generated = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
                var vector = new Vector(generated[0].Vector);
                rankings.Add(await indexes
                    .Where(x => EF.Property<Vector?>(x, "Embedding") != null)
                    .OrderBy(x => EF.Property<Vector>(x, "Embedding").CosineDistance(vector))
                    .Take(40)
                    .Select(x => x.EventId)
                    .ToListAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 未配置 Embedding 时仍保留结构化与 pg_trgm 检索。
            }
        }

        var scores = new Dictionary<long, double>();
        foreach (var ranking in rankings)
        {
            for (var rank = 0; rank < ranking.Count; rank++)
            {
                scores[ranking[rank]] = scores.GetValueOrDefault(ranking[rank]) + 1d / (60 + rank + 1);
            }
        }
        var ids = scores.OrderByDescending(x => x.Value).Take(limit).Select(x => x.Key).ToArray();
        var rows = await (
            from index in db.EventSearchIndexes.AsNoTracking()
            join evt in db.Events.AsNoTracking() on index.EventId equals evt.Id
            where ids.Contains(index.EventId) && index.UserId == userId && evt.UserId == userId &&
                  index.IsCurrent && evt.DeletedAt == null
            select new { index, evt }).ToListAsync(cancellationToken);
        var labelRows = await db.EventLabelIndexes.AsNoTracking().Where(x => x.UserId == userId && x.IsCurrent && ids.Contains(x.EventId))
            .ToListAsync(cancellationToken);
        var locationRows = await db.EventLocations.AsNoTracking().Where(x => x.UserId == userId && ids.Contains(x.EventId))
            .ToListAsync(cancellationToken);
        var records = ids.Select(id => rows.Single(x => x.index.EventId == id))
            .Select(x => new RecordEvidence(
                x.evt.Id,
                x.index.SourceRevision,
                x.evt.Title,
                Snippet(x.index.RetrievalText, query),
                string.IsNullOrWhiteSpace(x.index.AiSummary) ? null : x.index.AiSummary,
                x.evt.HappenedAt,
                x.evt.CreatedAt,
                scores[x.evt.Id],
                labelRows.Where(l => l.EventId == x.evt.Id).Select(l => l.DisplayName).ToArray(),
                locationRows.FirstOrDefault(l => l.EventId == x.evt.Id && l.SourceRevision == x.index.SourceRevision)?.Name))
            .ToArray();
        foreach (var record in records)
        {
            _retrievedEventIds.Add(record.EventId);
            _recordEvidence.Add(record);
        }
        return new EvidenceBundle(records, [], TimeRange: BuildTimeRange(from, to));
    }

    [Description("对当前用户记录执行白名单统计。metric 仅允许 count、expense_total、trend、plan_completion_rate；不执行模型生成的 SQL。")]
    public async Task<EvidenceBundle> AggregateMyRecordsAsync(
        string metric,
        string? from = null,
        string? to = null,
        string? currency = null,
        string? category = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        var events = db.Events.AsNoTracking().Where(x => x.UserId == userId && x.DeletedAt == null);
        if (DateTimeOffset.TryParse(from, out var fromValue)) events = events.Where(x => (x.HappenedAt ?? x.CreatedAt) >= fromValue.ToUniversalTime());
        if (DateTimeOffset.TryParse(to, out var toValue)) events = events.Where(x => (x.HappenedAt ?? x.CreatedAt) <= toValue.ToUniversalTime());
        if (!string.IsNullOrWhiteSpace(category))
        {
            var key = category.ToLowerInvariant();
            events = events.Where(e => db.EventLabelIndexes.Any(x => x.UserId == userId && x.EventId == e.Id && x.IsCurrent &&
                x.Type == EventLabelType.PrimaryCategory && x.TaxonomyKey == key));
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var key = tag.ToLowerInvariant();
            events = events.Where(e => db.EventLabelIndexes.Any(x => x.UserId == userId && x.EventId == e.Id && x.IsCurrent &&
                x.Type == EventLabelType.BehaviorTag && x.TaxonomyKey == key));
        }

        object result = metric.ToLowerInvariant() switch
        {
            "count" => new { metric = "count", value = await events.LongCountAsync(cancellationToken) },
            "expense_total" => await AggregateExpensesAsync(events, userId, currency, cancellationToken),
            "plan_completion_rate" => await AggregateCompletionAsync(events, cancellationToken),
            "trend" => await AggregateTrendAsync(events, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(metric), "只支持 count、expense_total、trend、plan_completion_rate。"),
        };
        _aggregateEvidence = JsonSerializer.Serialize(result);
        return new EvidenceBundle([], [], _aggregateEvidence, BuildTimeRange(from, to));
    }

    [Description("读取 SearchMyRecords 已返回记录的原文、图片描述、时间和语义证据。不能读取未先检索的 Event。")]
    public async Task<IReadOnlyList<RecordEvidenceDetail>> GetMyRecordEvidenceAsync(
        IReadOnlyList<long> eventIds,
        CancellationToken cancellationToken = default)
    {
        var ids = eventIds.Distinct().Where(_retrievedEventIds.Contains).Take(20).ToArray();
        if (ids.Length == 0) return [];
        var userId = currentUser.UserId;
        var rows = await (
            from evt in db.Events.AsNoTracking()
            join index in db.EventSearchIndexes.AsNoTracking() on evt.Id equals index.EventId
            where ids.Contains(evt.Id) && evt.UserId == userId && index.UserId == userId && index.IsCurrent && evt.DeletedAt == null
            select new { evt, index }).ToListAsync(cancellationToken);
        var details = new List<RecordEvidenceDetail>();
        foreach (var row in rows)
        {
            var mentions = row.index.SemanticRunId is null
                ? []
                : await db.SemanticMentions.AsNoTracking()
                    .Where(x => x.UserId == userId && x.SemanticRunId == row.index.SemanticRunId)
                    .Take(100)
                    .Select(x => $"{x.Category}: {x.NormalizedValue} (confidence={x.Confidence})")
                    .ToListAsync(cancellationToken);
            details.Add(new RecordEvidenceDetail(row.evt.Id, row.index.SourceRevision, row.evt.Title,
                row.evt.RawContent, row.index.ImageDescriptions, row.evt.HappenedAt, row.evt.CreatedAt, mentions));
        }
        return details;
    }

    [Description("搜索当前用户有证据的长期记忆。拒绝状态不会返回；不接受 userId。")]
    public async Task<IReadOnlyList<MemoryEvidence>> SearchMyMemoriesAsync(
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        limit = Math.Clamp(limit, 1, 10);
        var memories = db.UserMemories.AsNoTracking()
            .Where(x => x.UserId == userId && x.Status != UserMemoryStatus.Rejected);
        List<long> ids;
        try
        {
            var generated = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
            var vector = new Vector(generated[0].Vector);
            ids = await memories.Where(x => EF.Property<Vector?>(x, "Embedding") != null)
                .OrderBy(x => EF.Property<Vector>(x, "Embedding").CosineDistance(vector))
                .Take(limit).Select(x => x.Id).ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ids = await memories.Where(x => EF.Functions.TrigramsSimilarity(x.Content, query) > 0.1)
                .OrderByDescending(x => x.UpdatedAt).Take(limit).Select(x => x.Id).ToListAsync(cancellationToken);
        }
        var result = await db.UserMemories.AsNoTracking().Include(x => x.Evidence)
            .Where(x => ids.Contains(x.Id) && x.UserId == userId && x.Status != UserMemoryStatus.Rejected)
            .ToListAsync(cancellationToken);
        var ordered = ids.Select(id => result.Single(x => x.Id == id))
            .Select(x => new MemoryEvidence(x.Id, x.Type.ToString(), x.Content, x.Status.ToString(),
                x.Confidence, x.Evidence.Select(e => e.EventId).Distinct().ToArray()))
            .ToArray();
        _memoryEvidence.AddRange(ordered);
        foreach (var eventId in ordered.SelectMany(x => x.EventIds)) _retrievedEventIds.Add(eventId);
        return ordered;
    }

    [Description("搜索当前用户已确认的历史地点，不接受 userId。")]
    public async Task<IReadOnlyList<PlaceEvidence>> SearchMyPlacesAsync(string query, int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        limit = Math.Clamp(limit, 1, 20);
        var places = await (from location in db.EventLocations.AsNoTracking()
                            join evt in db.Events.AsNoTracking() on location.EventId equals evt.Id
                            where location.UserId == userId && evt.UserId == userId && evt.DeletedAt == null &&
                                  location.UserConfirmed && location.SourceRevision == evt.CurrentSourceRevision &&
                                  (query == "" || EF.Functions.TrigramsSimilarity(location.Name + " " + location.Address, query) > 0.1)
                            orderby evt.HappenedAt ?? evt.CreatedAt descending
                            select new PlaceEvidence(location.Id, evt.Id, evt.Title ?? "无标题", location.Name, location.Address,
                                location.AdCode, evt.HappenedAt, 1)).Take(limit).ToListAsync(cancellationToken);
        foreach (var place in places) { _retrievedLocationIds.Add(place.LocationId); _retrievedEventIds.Add(place.EventId); }
        _placeEvidence.AddRange(places);
        return places;
    }

    [Description("读取本轮已经检索到的历史地点证据。")]
    public Task<IReadOnlyList<PlaceEvidence>> GetMyPlaceEvidenceAsync(IReadOnlyList<long> locationIds,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlaceEvidence>>(
            _placeEvidence.Where(x => locationIds.Contains(x.LocationId) && _retrievedLocationIds.Contains(x.LocationId)).ToArray());

    [Description("为本轮已检索且有可信坐标的地点生成结构化导航目标。")]
    public async Task<object?> GetNavigationTargetAsync(long locationId, CancellationToken cancellationToken = default)
    {
        if (!_retrievedLocationIds.Contains(locationId)) return null;
        var userId = currentUser.UserId;
        _navigationTarget = await (from location in db.EventLocations.AsNoTracking()
                                   join evt in db.Events.AsNoTracking() on location.EventId equals evt.Id
                                   where location.Id == locationId && location.UserId == userId && evt.UserId == userId && evt.DeletedAt == null &&
                                         location.SourceRevision == evt.CurrentSourceRevision && location.UserConfirmed && location.Latitude != null &&
                                         location.Longitude != null && location.CoordinateSystem == "GCJ02"
                                   select new { type = "navigation", eventId = evt.Id, locationId = location.Id, label = $"导航到{location.Name}" })
            .FirstOrDefaultAsync(cancellationToken);
        return _navigationTarget;
    }

    private async Task<object> AggregateExpensesAsync(IQueryable<Event> events, long userId, string? currency, CancellationToken cancellationToken)
    {
        currency = string.IsNullOrWhiteSpace(currency) ? "CNY" : currency.ToUpperInvariant();
        var ids = events.Select(x => new { x.Id, x.CurrentSourceRevision });
        var total = await (
            from expense in db.ExpenseFacts.AsNoTracking()
            join run in db.EventSemanticRuns.AsNoTracking() on expense.SemanticRunId equals run.Id
            join evt in ids on new { Id = run.EventId, CurrentSourceRevision = run.SourceRevision }
                equals new { evt.Id, evt.CurrentSourceRevision }
            where expense.UserId == userId && run.UserId == userId && run.Status == SemanticRunStatus.Completed && expense.Currency == currency
            select expense.Amount).SumAsync(cancellationToken);
        return new { metric = "expense_total", value = total, currency };
    }

    private static async Task<object> AggregateCompletionAsync(IQueryable<Event> events, CancellationToken cancellationToken)
    {
        var plans = events.Where(x => x.EventKind == EventKind.Plan);
        var total = await plans.LongCountAsync(cancellationToken);
        var completed = await plans.LongCountAsync(x => x.Status == EventStatus.Completed, cancellationToken);
        return new { metric = "plan_completion_rate", completed, total, value = total == 0 ? 0 : completed / (double)total };
    }

    private static async Task<object> AggregateTrendAsync(IQueryable<Event> events, CancellationToken cancellationToken)
    {
        var rows = await events.GroupBy(x => new { x.CreatedAt.Year, x.CreatedAt.Month })
            .Select(x => new { month = $"{x.Key.Year:D4}-{x.Key.Month:D2}", count = x.LongCount() })
            .OrderBy(x => x.month).Take(24).ToListAsync(cancellationToken);
        return new { metric = "trend", points = rows };
    }

    private static string Snippet(string text, string query)
    {
        const int limit = 320;
        if (text.Length <= limit) return text;
        var index = query.Length == 0 ? 0 : text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, index < 0 ? 0 : index - 80);
        return text.Substring(start, Math.Min(limit, text.Length - start));
    }

    private static string BuildTimeRange(string? from, string? to) => $"{from ?? "未限定"} - {to ?? "未限定"}";
}
