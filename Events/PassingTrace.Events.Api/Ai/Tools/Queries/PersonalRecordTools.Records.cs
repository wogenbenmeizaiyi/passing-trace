using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Ai.Evidence;

namespace PassingTrace.Events.Api.Ai.Tools.Queries;

public sealed partial class PersonalRecordTools
{
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
}
