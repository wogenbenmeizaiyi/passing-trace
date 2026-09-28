using PassingTrace.Core.Ai;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PassingTrace.Events.Api.Ai;

/// <summary>标题列表与消息正文分开读取；分页不能加载其他对话的正文或证据。</summary>
public static class AssistantConversationHistory
{
    public static async Task<AiConversationPageResponse> ListPageAsync(
        IAiConversationRepository repository, long userId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 50);
        AiConversationCursor? position = null;
        if (cursor is not null)
        {
            var (updatedAt, id) = DecodeCursor(cursor);
            position = new(updatedAt, id);
        }
        var rows = (await repository.ListAsync(userId, limit + 1, position, cancellationToken))
            .Select(x => new AiConversationResponse(x.Id, x.Title, x.CreatedAt, x.UpdatedAt)).ToArray();
        var hasMore = rows.Length > limit;
        var items = rows.Take(limit).Select(CleanSummary).ToArray();
        return new AiConversationPageResponse(items, hasMore ? EncodeCursor(items[^1]) : null);
    }

    public static async Task<AiConversationResponse?> GetSummaryAsync(
        IAiConversationRepository repository, long userId, Guid id, CancellationToken cancellationToken)
    {
        var result = await repository.ReadHeaderAsync(userId, id, cancellationToken);
        return result is null ? null : CleanSummary(new(result.Id, result.Title, result.CreatedAt, result.UpdatedAt));
    }

    public static async Task<AiMessagePageResponse?> GetMessagesPageAsync(
        IAiConversationRepository repository, long userId, Guid id, int limit, long? beforeId, CancellationToken cancellationToken)
    {
        if (beforeId is <= 0) throw new ArgumentOutOfRangeException(nameof(beforeId));
        if (await repository.ReadHeaderAsync(userId, id, cancellationToken) is null) return null;
        limit = Math.Clamp(limit, 1, 50);
        var rows = await repository.ReadMessagesAsync(userId, id, new(BeforeId: beforeId, Limit: limit + 1, NewestFirst: true), cancellationToken);
        var hasMore = rows.Count > limit;
        var items = new List<AiMessageResponse>();
        foreach (var x in rows.Take(limit).Reverse())
            items.Add(new AiMessageResponse(x.Id, x.Role.ToString(), x.Content, x.CreatedAt,
                await Social.SocialEvidenceGuard.ReadAsync(repository, userId, x.EvidenceSnapshotJson, cancellationToken)));
        return new AiMessagePageResponse(items, hasMore, hasMore ? items[0].Id : null);
    }

    private static AiConversationResponse CleanSummary(AiConversationResponse value) =>
        value with { Title = AssistantConversationTitle.CleanDisplay(value.Title) };

    private static string EncodeCursor(AiConversationResponse row) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes($"{row.UpdatedAt.UtcTicks}:{row.Id:N}"));

    private static (DateTimeOffset UpdatedAt, Guid Id) DecodeCursor(string cursor)
    {
        try
        {
            if (cursor.Length > 180) throw new FormatException();
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var ticks) ||
                !Guid.TryParseExact(parts[1], "N", out var id)) throw new FormatException();
            return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException("会话列表的位置已失效。", nameof(cursor));
        }
    }
}
