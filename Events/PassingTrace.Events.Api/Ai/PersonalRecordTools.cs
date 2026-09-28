using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;

namespace PassingTrace.Events.Api.Ai;

/// <summary>通过内部 MCP 暴露的只读工具。所有查询首先强制当前用户过滤。</summary>
public sealed class PersonalRecordTools(
    IPersonalRecordQueries queries,
    CurrentUserContext currentUser,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
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
        query = query?.Trim() ?? string.Empty;
        var filter = new StorylineSearchFilter(query, string.IsNullOrWhiteSpace(category) ? null : category.Trim().ToLowerInvariant(),
            Enum.TryParse<StorylineStatus>(status, true, out var parsedStatus) ? parsedStatus : null);
        var scores = new Dictionary<Guid, double>();
        AddRanking(scores, await queries.RankStorylinesAsync(userId, filter, AiSearchOrder.Recent, null, cancellationToken));
        if (query.Length > 0)
        {
            AddRanking(scores, await queries.RankStorylinesAsync(userId, filter, AiSearchOrder.Text, null, cancellationToken));
            try
            {
                var generated = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
                AddRanking(scores, await queries.RankStorylinesAsync(userId, filter, AiSearchOrder.Vector, generated[0].Vector.ToArray(), cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
        }
        var ids = scores.OrderByDescending(x => x.Value).Take(limit).Select(x => x.Key).ToArray();
        var matches = await queries.ReadStorylinesAsync(userId, ids, cancellationToken);
        var evidence = ids.Where(id => matches.Any(x => x.Storyline.Id == id)).Select(id =>
        {
            var match = matches.Single(x => x.Storyline.Id == id);
            var story = match.Storyline;
            var revision = match.Revision;
            var stages = revision.Stages.OrderBy(x => x.SemanticOrder).Select(stage => new StorylineStageEvidence(
                stage.Key, stage.Title, revision.Nodes.Where(x => x.StageKey == stage.Key).OrderBy(x => x.SemanticOrder)
                    .Select(x => PinnedSource(x).Title ?? "无标题记录").ToArray())).ToArray();
            return new StorylineEvidence(id, revision.Revision, story.Title, StorylineTaxonomy.Label(story.CategoryKey),
                story.Status.ToString(), Snippet(match.RetrievalText, query), story.RangeStart, story.RangeEnd, stages,
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
        var revision = await queries.FindStorylineRevisionAsync(userId, storylineId, cancellationToken);
        if (revision is null) return null;
        var story = revision.Storyline;
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
        query = query?.Trim() ?? string.Empty;
        var filter = new RecordSearchFilter(query, ParseTime(from), ParseTime(to),
            Enum.TryParse<EventKind>(kind, true, out var eventKind) ? eventKind : null,
            Enum.TryParse<EventStatus>(status, true, out var eventStatus) ? eventStatus : null,
            NormalizeKey(category), NormalizeKey(tag), string.IsNullOrWhiteSpace(location) ? null : location,
            string.IsNullOrWhiteSpace(adCode) ? null : adCode, centerLatitude, centerLongitude, radiusMeters, participantFriendId);
        var rankings = new List<IReadOnlyList<long>>
        {
            await queries.RankRecordsAsync(userId, filter, AiSearchOrder.Recent, null, cancellationToken)
        };
        if (query.Length > 0)
        {
            rankings.Add(await queries.RankRecordsAsync(userId, filter, AiSearchOrder.Text, null, cancellationToken));
            try
            {
                var generated = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
                rankings.Add(await queries.RankRecordsAsync(userId, filter, AiSearchOrder.Vector, generated[0].Vector.ToArray(), cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Embedding failure retains relational and text candidates.
            }
        }
        var scores = new Dictionary<long, double>();
        foreach (var ranking in rankings)
            for (var rank = 0; rank < ranking.Count; rank++)
                scores[ranking[rank]] = scores.GetValueOrDefault(ranking[rank]) + 1d / (61 + rank);
        var ids = scores.OrderByDescending(x => x.Value).Take(limit).Select(x => x.Key).ToArray();
        var rows = await queries.ReadRecordsAsync(userId, ids, participantFriendId, cancellationToken);
        var labelRows = rows.SelectMany(x => x.Labels).ToArray();
        var locationRows = rows.SelectMany(x => x.Locations).ToArray();
        var records = ids.Where(id => rows.Any(x => x.Index.EventId == id)).Select(id => rows.Single(x => x.Index.EventId == id))
            .Select(x => new RecordEvidence(
                x.Event.Id,
                x.Index.SourceRevision,
                x.Event.Title,
                Snippet(x.Index.RetrievalText, query),
                string.IsNullOrWhiteSpace(x.Index.AiSummary) ? null : x.Index.AiSummary,
                x.Event.HappenedAt,
                x.Event.CreatedAt,
                scores[x.Event.Id],
                labelRows.Where(l => l.EventId == x.Event.Id).Select(l => l.DisplayName).ToArray(),
                locationRows.FirstOrDefault(l => l.EventId == x.Event.Id && l.SourceRevision == x.Index.SourceRevision)?.Name,
                x.Event.UserId.ToString(), x.Event.UserId == userId ? null : $"/joint-records/{x.Event.Id}"))
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

    [Description("对当前用户记录执行精确统计：金额或消费合计用 expense_total，记录数量用 count，月度趋势用 trend，计划完成率用 plan_completion_rate。总金额跨主分类汇总所有金额事实，不依赖搜索分页或金额标签，不要自行限定美食、购物等分类。不接受其他统计类型，不执行模型生成的 SQL。用户询问所有记录时不要添加时间范围；缺少金额事实不能解释为没有消费。")]
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
        var filter = new RecordStatisticsFilter(
            DateTimeOffset.TryParse(from, out var fromValue) ? fromValue.ToUniversalTime() : null,
            DateTimeOffset.TryParse(to, out var toValue) ? toValue.ToUniversalTime() : null,
            NormalizeKey(category), metric == RecordAggregateMetric.ExpenseTotal && string.Equals(tag, "amount", StringComparison.OrdinalIgnoreCase)
                ? null : NormalizeKey(tag));
        object result = metric switch
        {
            RecordAggregateMetric.Count => new { metric = "count", value = await queries.CountRecordsAsync(userId, filter, cancellationToken) },
            RecordAggregateMetric.ExpenseTotal => await AggregateExpensesAsync(filter, userId, currency, cancellationToken),
            RecordAggregateMetric.PlanCompletionRate => await AggregateCompletionAsync(filter, userId, cancellationToken),
            RecordAggregateMetric.Trend => await AggregateTrendAsync(filter, userId, cancellationToken),
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
        var rows = await queries.ReadRecordDetailsAsync(userId, ids, cancellationToken);
        return rows.Select(row => new RecordEvidenceDetail(row.Event.Id, row.Index.SourceRevision, row.Event.Title,
            row.Event.RawContent, row.Index.ImageDescriptions, row.Event.HappenedAt, row.Event.CreatedAt,
            row.Mentions.Select(x => $"{x.Category}: {x.NormalizedValue} (confidence={x.Confidence})").ToArray())).ToArray();
    }

    [Description("搜索当前用户有证据的长期记忆。拒绝状态不会返回；不接受 userId。")]
    public async Task<IReadOnlyList<MemoryEvidence>> SearchMyMemoriesAsync(
        [MaxLength(8000)] string query,
        [Range(1, 10)] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        limit = Math.Clamp(limit, 1, 10);
        IReadOnlyList<UserMemory> result;
        try
        {
            var generated = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
            result = await queries.SearchMemoriesAsync(userId, query, limit, generated[0].Vector.ToArray(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = await queries.SearchMemoriesAsync(userId, query, limit, null, cancellationToken);
        }
        var ordered = result.Select(x => new MemoryEvidence(x.Id, x.Type.ToString(), x.Content, x.Status.ToString(),
            x.Confidence, x.Evidence.Select(e => e.EventId).Distinct().ToArray())).ToArray();
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

    private async Task<object> AggregateExpensesAsync(RecordStatisticsFilter filter, long userId, string? currency, CancellationToken cancellationToken)
    {
        currency = string.IsNullOrWhiteSpace(currency) ? "CNY" : currency.ToUpperInvariant();
        var total = await queries.SumExpensesAsync(userId, filter, currency, cancellationToken);
        return new
        {
            metric = "expense_total",
            value = total.Value,
            currency,
            amountFactCount = total.Count,
            hasAmountData = total.Value is not null,
            explanation = total.Value is null
                ? "所选范围内没有可核实的金额，无法确认消费合计；不能解释为消费为零。"
                : "仅汇总所选范围内已确认的金额，不代表没有记下的实际消费。",
        };
    }

    private async Task<object> AggregateCompletionAsync(RecordStatisticsFilter filter, long userId, CancellationToken cancellationToken)
    {
        var result = await queries.CountPlanCompletionAsync(userId, filter, cancellationToken);
        return new
        {
            metric = "plan_completion_rate",
            completed = result.Completed,
            total = result.Total,
            value = result.Total == 0 ? 0 : result.Completed / (double)result.Total
        };
    }

    private async Task<object> AggregateTrendAsync(RecordStatisticsFilter filter, long userId, CancellationToken cancellationToken)
    {
        var rows = await queries.CountMonthlyRecordsAsync(userId, filter, cancellationToken);
        return new { metric = "trend", points = rows.Select(x => new { month = $"{x.Year:D4}-{x.Month:D2}", count = x.Count }).ToArray() };
    }

    private static string? NormalizeKey(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.ToLowerInvariant();
    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed.ToUniversalTime() : null;

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
