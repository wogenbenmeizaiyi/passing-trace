using System.Globalization;
using Microsoft.Extensions.AI;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai.Assistant.Context;
using PassingTrace.Events.Api.Ai.Evidence;
using PassingTrace.Events.Api.Common;

namespace PassingTrace.Events.Api.Ai.Tools.Queries;

/// <summary>通过内部 MCP 暴露的只读工具。所有查询首先强制当前用户过滤。</summary>
public sealed partial class PersonalRecordTools(
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

    public EvidenceBundle Snapshot => new(
        _recordEvidence.GroupBy(x => x.EventId).Select(x => x.First()).ToArray(),
        _memoryEvidence.GroupBy(x => x.MemoryId).Select(x => x.First()).ToArray(),
        _aggregateEvidence, TimeRange: _aggregateTimeRange,
        Places: _placeEvidence.GroupBy(x => x.LocationId).Select(x => x.First()).ToArray(),
        NavigationTarget: _navigationTarget,
        Storylines: _storylineEvidence.GroupBy(x => x.StorylineId).Select(x => x.First()).ToArray());

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
