using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Ai;

/// <summary>标题列表与消息正文分开读取；分页不能加载其他对话的正文或证据。</summary>
public static class AssistantConversationHistory
{
    public static async Task<AiConversationPageResponse> ListPageAsync(
        TraceDbContext db, long userId, int limit, string? cursor, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 50);
        var query = db.AiConversations.AsNoTracking()
            .Where(x => x.UserId == userId && x.DeletedAt == null);
        if (cursor is not null)
        {
            var (updatedAt, id) = DecodeCursor(cursor);
            query = query.Where(x => x.UpdatedAt < updatedAt ||
                (x.UpdatedAt == updatedAt && x.Id.CompareTo(id) < 0));
        }
        var rows = await query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .Select(x => new AiConversationResponse(x.Id, x.Title, x.CreatedAt, x.UpdatedAt))
            .Take(limit + 1).ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var items = rows.Take(limit).Select(CleanSummary).ToArray();
        return new AiConversationPageResponse(items, hasMore ? EncodeCursor(items[^1]) : null);
    }

    public static async Task<AiConversationResponse?> GetSummaryAsync(
        TraceDbContext db, long userId, Guid id, CancellationToken cancellationToken)
    {
        var result = await db.AiConversations.AsNoTracking()
            .Where(x => x.UserId == userId && x.Id == id && x.DeletedAt == null)
            .Select(x => new AiConversationResponse(x.Id, x.Title, x.CreatedAt, x.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);
        return result is null ? null : CleanSummary(result);
    }

    public static async Task<AiMessagePageResponse?> GetMessagesPageAsync(
        TraceDbContext db, long userId, Guid id, int limit, long? beforeId, CancellationToken cancellationToken)
    {
        if (beforeId is <= 0) throw new ArgumentOutOfRangeException(nameof(beforeId));
        if (!await db.AiConversations.AsNoTracking().AnyAsync(
                x => x.Id == id && x.UserId == userId && x.DeletedAt == null, cancellationToken))
            return null;
        limit = Math.Clamp(limit, 1, 50);
        var query = db.AiMessages.AsNoTracking()
            .Where(x => x.ConversationId == id && x.UserId == userId);
        if (beforeId is not null) query = query.Where(x => x.Id < beforeId.Value);
        var rows = await query.OrderByDescending(x => x.Id)
            .Select(x => new { x.Id, x.Role, x.Content, x.CreatedAt, x.EvidenceSnapshotJson })
            .Take(limit + 1).ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var items = rows.Take(limit).Reverse().Select(x => new AiMessageResponse(
            x.Id, x.Role.ToString(), x.Content, x.CreatedAt,
            string.IsNullOrWhiteSpace(x.EvidenceSnapshotJson) ? null : JsonSerializer.Deserialize<object>(x.EvidenceSnapshotJson)))
            .ToArray();
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
