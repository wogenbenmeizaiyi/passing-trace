using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
using PassingTrace.Events.Api.Social;

namespace PassingTrace.Events.Api.Ai;

/// <summary>通过内部 MCP 暴露的只读工具。所有查询首先强制当前用户过滤。</summary>
public sealed class PersonalRecordTools(
    TraceDbContext db,
    CurrentUserContext currentUser,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    SharedContentService? sharedContent = null)
{
    private readonly HashSet<long> _retrievedEventIds = [];
    private readonly List<RecordEvidence> _recordEvidence = [];
    private readonly List<MemoryEvidence> _memoryEvidence = [];
    private string? _aggregateEvidence;
    private string? _aggregateTimeRange;
    private AssistantCalendarRange? _currentMonthRange;
    private readonly List<PlaceEvidence> _placeEvidence = [];
    private readonly HashSet<long> _retrievedLocationIds = [];
    private AssistantAction? _navigationTarget;
    private readonly List<StorylineEvidence> _storylineEvidence = [];
    private readonly HashSet<Guid> _retrievedStorylineIds = [];

    public void ConfigureCalendarContext(AssistantCalendarContext calendar, string question) =>
        _currentMonthRange = calendar.ResolveCurrentMonth(question);

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

    public EvidenceBundle Snapshot => new(
        _recordEvidence.GroupBy(x => x.EventId).Select(x => x.First()).ToArray(),
        _memoryEvidence.GroupBy(x => x.MemoryId).Select(x => x.First()).ToArray(),
        _aggregateEvidence, TimeRange: _aggregateTimeRange,
        Places: _placeEvidence.GroupBy(x => x.LocationId).Select(x => x.First()).ToArray(),
        NavigationTarget: _navigationTarget,
        Storylines: _storylineEvidence.GroupBy(x => x.StorylineId).Select(x => x.First()).ToArray());

    [Description("搜索当前登录用户自己的故事线，适合旅行过程、项目阶段、活动纪实和生命周期问题；不接受 userId。")]
    public async Task<IReadOnlyList<StorylineEvidence>> SearchMyStorylinesAsync(
        [MaxLength(8000), Description("自然语言关键词或问题")] string query,
        [Description("故事线主分类 key，可空")] string? category = null,
        [RegularExpression("^(Ongoing|Completed)$"), Description("Ongoing 或 Completed，可空")] string? status = null,
        [Range(1, 10), Description("最多返回 1-10 条")] int limit = 5,
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
        [MaxLength(8000), Description("自然语言关键词或问题")] string query,
        [DataType(DataType.DateTime), Description("ISO-8601 起始时间，带时区，可空")] string? from = null,
        [DataType(DataType.DateTime), Description("ISO-8601 结束时间，带时区，可空")] string? to = null,
        [RegularExpression("^(Trace|Plan)$"), Description("Trace 或 Plan，可空")] string? kind = null,
        [RegularExpression("^(Completed|Planned|Cancelled)$"), Description("Completed、Planned、Cancelled，可空")] string? status = null,
        [Description("主分类 taxonomy key，可空")] string? category = null,
        [Description("行为标签 taxonomy key，可空")] string? tag = null,
        [Description("地点名称，可空")] string? location = null,
        [Description("行政区 adCode，可空")] string? adCode = null,
        [Range(-90, 90), Description("中心纬度，可空")] decimal? centerLatitude = null,
        [Range(-180, 180), Description("中心经度，可空")] decimal? centerLongitude = null,
        [Range(100, 100000), Description("半径米数，可空")] int? radiusMeters = null,
        [Range(1, 20), Description("最多返回 1-20 条")] int limit = 10,
        CancellationToken cancellationToken = default,
        [Description("SearchMyFriends 返回的好友关系 ID；查询与该好友共同参与的记录时填写。")] Guid? participantFriendId = null)
    {
        limit = Math.Clamp(limit, 1, 20);
        var userId = currentUser.UserId;
        var visible = sharedContent?.ReadableEvents(userId) ?? db.Events.Where(x => x.UserId == userId && x.DeletedAt == null);
        if (participantFriendId is { } friendId)
        {
            var friend = await db.Friendships.AsNoTracking().SingleOrDefaultAsync(x => x.Id == friendId && x.Active &&
                (x.FirstUserId == userId || x.SecondUserId == userId), cancellationToken)
                ?? throw new DomainValidationException("这位好友已不可查询，请重新查找好友。");
            var other = FriendService.Other(friend, userId);
            visible = visible.Where(e => e.UserId == other || db.EventParticipants.Any(p => p.EventId == e.Id && p.UserId == other && p.Active &&
                db.Friendships.Any(f => f.Id == p.FriendshipId && f.Active)));
        }
        var indexes = db.EventSearchIndexes.AsNoTracking()
            .Where(x => x.IsCurrent && visible.Any(e => e.Id == x.EventId && e.UserId == x.UserId));
        if (DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromValue))
        {
            indexes = indexes.Where(x => visible.Any(e => e.Id == x.EventId &&
                (e.HappenedAt ?? e.CreatedAt) >= fromValue.ToUniversalTime()));
        }
        if (DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toValue))
        {
            indexes = indexes.Where(x => visible.Any(e => e.Id == x.EventId &&
                (e.HappenedAt ?? e.CreatedAt) <= toValue.ToUniversalTime()));
        }
        if (Enum.TryParse<EventKind>(kind, true, out var eventKind))
        {
            indexes = indexes.Where(x => visible.Any(e => e.Id == x.EventId && e.EventKind == eventKind));
        }
        if (Enum.TryParse<EventStatus>(status, true, out var eventStatus))
        {
            indexes = indexes.Where(x => visible.Any(e => e.Id == x.EventId && e.Status == eventStatus));
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            var key = category.ToLowerInvariant();
            indexes = indexes.Where(x => db.EventLabelIndexes.Any(m => m.UserId == x.UserId && m.EventId == x.EventId &&
                m.IsCurrent && m.Type == EventLabelType.PrimaryCategory && m.TaxonomyKey == key));
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var key = tag.ToLowerInvariant();
            indexes = indexes.Where(x => db.EventLabelIndexes.Any(m => m.UserId == x.UserId && m.EventId == x.EventId &&
                m.IsCurrent && m.Type == EventLabelType.BehaviorTag && m.TaxonomyKey == key));
        }
        if (!string.IsNullOrWhiteSpace(adCode))
            indexes = indexes.Where(x => db.EventLocations.Any(l => l.UserId == x.UserId && l.EventId == x.EventId &&
                l.SourceRevision == x.SourceRevision && l.AdCode == adCode));
        if (centerLatitude.HasValue && centerLongitude.HasValue)
        {
            var radius = Math.Clamp(radiusMeters ?? 1000, 100, 100000);
            var latitudeDelta = (decimal)radius / 111_320m;
            var longitudeDelta = latitudeDelta / (decimal)Math.Max(0.1, Math.Cos((double)centerLatitude.Value * Math.PI / 180));
            var minLat = centerLatitude.Value - latitudeDelta; var maxLat = centerLatitude.Value + latitudeDelta;
            var minLon = centerLongitude.Value - longitudeDelta; var maxLon = centerLongitude.Value + longitudeDelta;
            indexes = indexes.Where(x => db.EventLocations.Any(l => l.UserId == x.UserId && l.EventId == x.EventId &&
                l.SourceRevision == x.SourceRevision && l.Latitude >= minLat && l.Latitude <= maxLat &&
                l.Longitude >= minLon && l.Longitude <= maxLon));
        }
        if (!string.IsNullOrWhiteSpace(location))
        {
            indexes = indexes.Where(x => x.SemanticRunId != null && db.SemanticMentions.Any(m =>
                m.UserId == x.UserId && m.SemanticRunId == x.SemanticRunId && m.Category == "location" &&
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
            join evt in visible.AsNoTracking() on index.EventId equals evt.Id
            where ids.Contains(index.EventId) && index.UserId == evt.UserId &&
                  index.IsCurrent && evt.DeletedAt == null
            select new { index, evt }).ToListAsync(cancellationToken);
        var labelRows = await db.EventLabelIndexes.AsNoTracking().Where(x => x.IsCurrent && ids.Contains(x.EventId) && visible.Any(e => e.Id == x.EventId && e.UserId == x.UserId))
            .ToListAsync(cancellationToken);
        var locationRows = await db.EventLocations.AsNoTracking().Where(x => ids.Contains(x.EventId) && visible.Any(e => e.Id == x.EventId && e.UserId == x.UserId))
            .ToListAsync(cancellationToken);
        var records = ids.Where(id => rows.Any(x => x.index.EventId == id)).Select(id => rows.Single(x => x.index.EventId == id))
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
                locationRows.FirstOrDefault(l => l.EventId == x.evt.Id && l.SourceRevision == x.index.SourceRevision)?.Name,
                x.evt.UserId.ToString(), x.evt.UserId == userId ? null : $"/joint-records/{x.evt.Id}"))
            .ToArray();
        foreach (var record in records)
        {
            _retrievedEventIds.Add(record.EventId);
            _recordEvidence.Add(record);
        }
        var places = locationRows
            .Where(locationRow => locationRow.UserConfirmed && records.Any(record =>
                record.EventId == locationRow.EventId && record.SourceRevision == locationRow.SourceRevision))
            .Select(locationRow =>
            {
                var record = records.Single(record => record.EventId == locationRow.EventId);
                return new PlaceEvidence(locationRow.Id, record.EventId, record.Title ?? "无标题", locationRow.Name,
                    locationRow.Address, locationRow.AdCode, record.HappenedAt, 1);
            })
            .ToArray();
        foreach (var place in places)
        {
            _retrievedLocationIds.Add(place.LocationId);
            _placeEvidence.Add(place);
        }
        return new EvidenceBundle(records, [], TimeRange: BuildTimeRange(from, to), Places: places);
    }

    [Description("对当前用户记录执行精确统计：金额或消费合计用 expense_total，记录数量用 count，月度趋势用 trend，计划完成率用 plan_completion_rate。不接受其他统计类型，不执行模型生成的 SQL。用户询问所有记录时不要添加时间范围；缺少金额事实不能解释为没有消费。")]
    public async Task<EvidenceBundle> AggregateMyRecordsAsync(
        [Description("必填：金额合计 expense_total；数量 count；月度趋势 trend；计划完成率 plan_completion_rate。")] RecordAggregateMetric metric,
        [DataType(DataType.DateTime), Description("时间范围起点，ISO 8601，带时区；统计全部时留空。")] string? from = null,
        [DataType(DataType.DateTime), Description("时间范围终点，ISO 8601，带时区；统计全部时留空。")] string? to = null,
        [RegularExpression("^[A-Za-z]{3}$"), Description("金额统计的币种代码，默认 CNY；不同币种不混合求和。")] string? currency = null,
        [Description("已知记录主分类 key，可空，不猜测不存在的分类。")] string? category = null,
        [Description("已知记录标签 key，可空。")] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(metric)) throw new AssistantStatisticsToolException();
        if (_currentMonthRange is not null)
        {
            // Never let a model substitute its training-date month for an explicit "this month".
            from = _currentMonthRange.FromText;
            to = _currentMonthRange.ToText;
        }
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

        object result = metric switch
        {
            RecordAggregateMetric.Count => new { metric = "count", value = await events.LongCountAsync(cancellationToken) },
            RecordAggregateMetric.ExpenseTotal => await AggregateExpensesAsync(events, userId, currency, cancellationToken),
            RecordAggregateMetric.PlanCompletionRate => await AggregateCompletionAsync(events, cancellationToken),
            RecordAggregateMetric.Trend => await AggregateTrendAsync(events, cancellationToken),
            _ => throw new AssistantStatisticsToolException(),
        };
        _aggregateEvidence = JsonSerializer.Serialize(result);
        _aggregateTimeRange = BuildTimeRange(from, to);
        return new EvidenceBundle([], [], _aggregateEvidence, _aggregateTimeRange);
    }

    [Description("读取 SearchMyRecords 已返回记录的原文、图片描述、时间和语义证据。不能读取未先检索的 Event。")]
    public async Task<IReadOnlyList<RecordEvidenceDetail>> GetMyRecordEvidenceAsync(
        [MaxLength(20)] IReadOnlyList<long> eventIds,
        CancellationToken cancellationToken = default)
    {
        var ids = eventIds.Distinct().Where(_retrievedEventIds.Contains).Take(20).ToArray();
        if (ids.Length == 0) return [];
        var userId = currentUser.UserId;
        var rows = await (
            from evt in (sharedContent?.ReadableEvents(userId) ?? db.Events.Where(x => x.UserId == userId && x.DeletedAt == null)).AsNoTracking()
            join index in db.EventSearchIndexes.AsNoTracking() on evt.Id equals index.EventId
            where ids.Contains(evt.Id) && index.UserId == evt.UserId && index.IsCurrent && evt.DeletedAt == null
            select new { evt, index }).ToListAsync(cancellationToken);
        var details = new List<RecordEvidenceDetail>();
        foreach (var row in rows)
        {
            var mentions = row.index.SemanticRunId is null
                ? []
                : await db.SemanticMentions.AsNoTracking()
                    .Where(x => x.UserId == row.evt.UserId && x.SemanticRunId == row.index.SemanticRunId)
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
        [MaxLength(8000)] string query,
        [Range(1, 10)] int limit = 5,
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
    public async Task<IReadOnlyList<PlaceEvidence>> SearchMyPlacesAsync([MaxLength(8000)] string query, [Range(1, 20)] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        limit = Math.Clamp(limit, 1, 20);
        var retrievedEventIds = _recordEvidence.Select(x => x.EventId).Distinct().Take(limit).ToArray();
        var places = await (from location in db.EventLocations.AsNoTracking()
                            join evt in db.Events.AsNoTracking() on location.EventId equals evt.Id
                            where location.UserId == userId && evt.UserId == userId && evt.DeletedAt == null &&
                                  location.UserConfirmed && location.SourceRevision == evt.CurrentSourceRevision &&
                                  (query == "" || retrievedEventIds.Contains(evt.Id) ||
                                   EF.Functions.TrigramsSimilarity(location.Name + " " + location.Address, query) > 0.1)
                            orderby evt.HappenedAt ?? evt.CreatedAt descending
                            select new PlaceEvidence(location.Id, evt.Id, evt.Title ?? "无标题", location.Name, location.Address,
                                location.AdCode, evt.HappenedAt, 1)).Take(limit).ToListAsync(cancellationToken);
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
        var target = await (from location in db.EventLocations.AsNoTracking()
                            join evt in db.Events.AsNoTracking() on location.EventId equals evt.Id
                            where location.Id == locationId && location.UserId == userId && evt.UserId == userId && evt.DeletedAt == null &&
                                  location.SourceRevision == evt.CurrentSourceRevision && location.UserConfirmed && location.Latitude != null &&
                                  location.Longitude != null && location.CoordinateSystem == "GCJ02"
                            select new
                            {
                                EventId = evt.Id,
                                LocationId = location.Id,
                                location.Name,
                                location.Address,
                                Latitude = location.Latitude!.Value,
                                Longitude = location.Longitude!.Value,
                                location.ProviderPoiId,
                            })
            .FirstOrDefaultAsync(cancellationToken);
        if (target is null) return null;
        _navigationTarget = new AssistantAction(
            "amap-navigation", "amap", $"导航到{target.Name}", target.Name, target.Address,
            target.Latitude, target.Longitude, "GCJ02", target.ProviderPoiId, "personal-record",
            target.EventId, target.LocationId);
        return _navigationTarget;
    }

    private async Task<object> AggregateExpensesAsync(IQueryable<Event> events, long userId, string? currency, CancellationToken cancellationToken)
    {
        currency = string.IsNullOrWhiteSpace(currency) ? "CNY" : currency.ToUpperInvariant();
        var ids = events.Select(x => new { x.Id, x.CurrentSourceRevision });
        var amounts = (
            from expense in db.ExpenseFacts.AsNoTracking()
            join run in db.EventSemanticRuns.AsNoTracking() on expense.SemanticRunId equals run.Id
            join evt in ids on new { Id = run.EventId, CurrentSourceRevision = run.SourceRevision }
                equals new { evt.Id, evt.CurrentSourceRevision }
            where expense.UserId == userId && run.UserId == userId && run.Status == SemanticRunStatus.Completed && expense.Currency == currency
            select expense.Amount);
        var total = await amounts.GroupBy(_ => 1)
            .Select(group => new { Value = group.Sum(), Count = group.LongCount() })
            .SingleOrDefaultAsync(cancellationToken);
        return new
        {
            metric = "expense_total",
            value = total is null ? (decimal?)null : total.Value,
            currency,
            amountFactCount = total?.Count ?? 0,
            hasAmountData = total is not null,
            explanation = total is null
                ? "所选范围内没有可核实的金额，无法确认消费合计；不能解释为消费为零。"
                : "仅汇总所选范围内已确认的金额，不代表没有记下的实际消费。",
        };
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
            .Select(x => new { x.Key.Year, x.Key.Month, count = x.LongCount() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).Take(24).ToListAsync(cancellationToken);
        // Formatting is a presentation step; PostgreSQL sorts the numeric buckets before materializing.
        return new { metric = "trend", points = rows.Select(x => new { month = $"{x.Year:D4}-{x.Month:D2}", x.count }).ToArray() };
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
