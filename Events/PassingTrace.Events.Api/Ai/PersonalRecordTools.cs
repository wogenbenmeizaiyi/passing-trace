using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
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

    public EvidenceBundle Snapshot => new(
        _recordEvidence.GroupBy(x => x.EventId).Select(x => x.First()).ToArray(),
        _memoryEvidence.GroupBy(x => x.MemoryId).Select(x => x.First()).ToArray(),
        _aggregateEvidence);

    [Description("搜索当前登录用户自己的记录。支持关键词、时间、记录类型、状态、语义类别和地点；返回已排序的证据，不接受 userId。")]
    public async Task<EvidenceBundle> SearchMyRecordsAsync(
        [Description("自然语言关键词或问题")] string query,
        [Description("ISO-8601 起始时间，可空")] string? from = null,
        [Description("ISO-8601 结束时间，可空")] string? to = null,
        [Description("Trace 或 Plan，可空")] string? kind = null,
        [Description("Completed、Planned、Cancelled 等状态，可空")] string? status = null,
        [Description("语义类别，可空")] string? category = null,
        [Description("地点名称，可空")] string? location = null,
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
            indexes = indexes.Where(x => x.SemanticRunId != null && db.SemanticMentions.Any(m =>
                m.UserId == userId && m.SemanticRunId == x.SemanticRunId && m.Category == category));
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
        var records = ids.Select(id => rows.Single(x => x.index.EventId == id))
            .Select(x => new RecordEvidence(
                x.evt.Id,
                x.index.SourceRevision,
                x.evt.Title,
                Snippet(x.index.RetrievalText, query),
                string.IsNullOrWhiteSpace(x.index.AiSummary) ? null : x.index.AiSummary,
                x.evt.HappenedAt,
                x.evt.CreatedAt,
                scores[x.evt.Id]))
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
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        var events = db.Events.AsNoTracking().Where(x => x.UserId == userId && x.DeletedAt == null);
        if (DateTimeOffset.TryParse(from, out var fromValue)) events = events.Where(x => (x.HappenedAt ?? x.CreatedAt) >= fromValue.ToUniversalTime());
        if (DateTimeOffset.TryParse(to, out var toValue)) events = events.Where(x => (x.HappenedAt ?? x.CreatedAt) <= toValue.ToUniversalTime());

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
