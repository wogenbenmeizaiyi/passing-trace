using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;

namespace PassingTrace.Infrastructure.Persistence.Ai;

public sealed class AiConversationRepository(TraceDbContext db) : IAiConversationRepository
{
    private IQueryable<AiConversation> Owned(long userId) => db.AiConversations.Where(x => x.UserId == userId && x.DeletedAt == null);

    public async Task<IReadOnlyList<AiConversationHeader>> ListAsync(long userId, int? limit, AiConversationCursor? cursor, CancellationToken cancellationToken)
    {
        var query = Owned(userId).AsNoTracking();
        if (cursor is not null) query = query.Where(x => x.UpdatedAt < cursor.UpdatedAt || (x.UpdatedAt == cursor.UpdatedAt && x.Id.CompareTo(cursor.Id) < 0));
        var headers = query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .Select(x => new AiConversationHeader(x.Id, x.Title, x.CreatedAt, x.UpdatedAt));
        return await (limit is { } count ? headers.Take(count) : headers).ToArrayAsync(cancellationToken);
    }

    public Task<AiConversationHeader?> ReadHeaderAsync(long userId, Guid id, CancellationToken cancellationToken) =>
        Owned(userId).AsNoTracking().Where(x => x.Id == id)
            .Select(x => new AiConversationHeader(x.Id, x.Title, x.CreatedAt, x.UpdatedAt)).FirstOrDefaultAsync(cancellationToken);

    public Task<AiConversation?> FindAsync(long userId, Guid id, CancellationToken cancellationToken) =>
        Owned(userId).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AiMessage>> ReadMessagesAsync(long userId, Guid conversationId, AiMessageQuery query, CancellationToken cancellationToken)
    {
        var messages = db.AiMessages.AsNoTracking().Where(x => x.ConversationId == conversationId && x.UserId == userId && x.Id > query.AfterId &&
            Owned(userId).Any(c => c.Id == x.ConversationId));
        if (query.BeforeId is { } before) messages = messages.Where(x => x.Id < before);
        if (query.NotExpiredAt is { } now) messages = messages.Where(x => x.ExpiresAt == null || x.ExpiresAt > now);
        var ordered = query.NewestFirst ? messages.OrderByDescending(x => x.Id) : messages.OrderBy(x => x.Id);
        return await (query.Limit is { } limit ? ordered.Take(limit) : ordered).ToArrayAsync(cancellationToken);
    }

    public Task<ConversationSummary?> FindSummaryAsync(long userId, Guid conversationId, CancellationToken cancellationToken) =>
        db.ConversationSummaries.FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.UserId == userId &&
            Owned(userId).Any(c => c.Id == x.ConversationId), cancellationToken);

    public async Task<long> ReadWatermarkAsync(long userId, CancellationToken cancellationToken) =>
        await db.UserDataWatermarks.AsNoTracking().Where(x => x.UserId == userId).Select(x => (long?)x.Version).SingleOrDefaultAsync(cancellationToken) ?? 0;

    public Task<bool> HasSocialHistoryAsync(long userId, CancellationToken cancellationToken) =>
        db.Friendships.AsNoTracking().AnyAsync(x => x.FirstUserId == userId || x.SecondUserId == userId, cancellationToken);

    public Task<bool> HasEarlierAnswerAsync(long userId, Guid conversationId, long beforeId, CancellationToken cancellationToken) =>
        db.AiMessages.AsNoTracking().AnyAsync(x => x.UserId == userId && x.ConversationId == conversationId && x.Id < beforeId &&
            x.Role == AiMessageRole.Assistant && Owned(userId).Any(c => c.Id == x.ConversationId), cancellationToken);

    public Task<string?> ReadLatestQuestionAsync(long userId, Guid conversationId, long beforeId, CancellationToken cancellationToken) =>
        db.AiMessages.AsNoTracking().Where(x => x.UserId == userId && x.ConversationId == conversationId && x.Id < beforeId &&
            x.Role == AiMessageRole.User && Owned(userId).Any(c => c.Id == x.ConversationId))
            .OrderByDescending(x => x.Id).Select(x => x.Content).FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryUpdateTitleAsync(AiConversation conversation, string expectedTitle, string title, CancellationToken cancellationToken)
    {
        var count = await Owned(conversation.UserId).Where(x => x.Id == conversation.Id && x.Title == expectedTitle)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Title, title), cancellationToken);
        if (count == 0) return false;
        conversation.Title = title;
        db.Entry(conversation).Property(x => x.Title).OriginalValue = title;
        return true;
    }

    public async Task<AiEvidenceAccess> ReadEvidenceAccessAsync(long userId, IReadOnlyList<long> eventIds, IReadOnlyList<Guid> shareIds, CancellationToken cancellationToken)
    {
        var friends = await db.Friendships.AsNoTracking().Where(x => x.Active && (x.FirstUserId == userId || x.SecondUserId == userId)).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var accessible = await db.Events.Where(x => eventIds.Contains(x.Id) && x.DeletedAt == null && (x.UserId == userId ||
            db.EventParticipants.Any(p => p.EventId == x.Id && p.UserId == userId && p.Active && friends.Contains(p.FriendshipId))))
            .Select(x => x.Id).ToArrayAsync(cancellationToken);
        var shares = await db.ContentShares.Where(x => shareIds.Contains(x.Id) && x.RecipientId == userId && x.RevokedAt == null && friends.Contains(x.FriendshipId) &&
            ((x.EventId != null && db.Events.Any(ev => ev.Id == x.EventId && ev.DeletedAt == null)) ||
             (x.StorylineId != null && db.Storylines.Any(s => s.Id == x.StorylineId && s.DeletedAt == null))))
            .Select(x => x.Id).ToArrayAsync(cancellationToken);
        return new(friends, accessible, shares);
    }

    public void Add(AiConversation conversation) => db.AiConversations.Add(conversation);
    public void Add(AiMessage message) => db.AiMessages.Add(message);
    public void Add(ConversationSummary summary) => db.ConversationSummaries.Add(summary);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
