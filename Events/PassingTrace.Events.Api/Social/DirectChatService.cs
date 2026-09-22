using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Events;
using PassingTrace.Core.Social;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Social;

public sealed class DirectChatService(TraceDbContext db, FriendService friends, SharedContentService shares,
    ISocialIdentityClient people, TimeProvider clock)
{
    public async Task<DirectConversation> RequireAsync(long me, Guid id, CancellationToken ct) =>
        await db.DirectConversations.SingleOrDefaultAsync(x => x.Id == id && (x.FirstUserId == me || x.SecondUserId == me), ct)
        ?? throw new KeyNotFoundException("对话不存在。");

    public async Task<Guid> OpenAsync(long me, Guid friendshipId, CancellationToken ct)
    {
        var f = await friends.RequireAsync(me, friendshipId, ct);
        var existing = await db.DirectConversations.AsNoTracking().FirstOrDefaultAsync(x => x.FriendshipId == f.Id, ct);
        if (existing is not null) return existing.Id;
        f.Version = Guid.NewGuid();
        var conversation = new DirectConversation { FriendshipId = f.Id, FirstUserId = f.FirstUserId, SecondUserId = f.SecondUserId, UpdatedAt = clock.GetUtcNow() };
        db.DirectConversations.Add(conversation);
        db.ConversationMembers.AddRange(new ConversationMember { ConversationId = conversation.Id, UserId = f.FirstUserId },
            new ConversationMember { ConversationId = conversation.Id, UserId = f.SecondUserId });
        await db.SaveChangesAsync(ct);
        return conversation.Id;
    }

    public async Task<SocialPage<DirectConversationView>> ListAsync(long me, int limit, string? cursor, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 50);
        var query = db.DirectConversations.AsNoTracking().Where(x => x.FirstUserId == me || x.SecondUserId == me);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var parts = cursor.Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var id))
                throw new DomainValidationException("列表位置已失效，请刷新。");
            var at = new DateTimeOffset(ticks, TimeSpan.Zero);
            query = query.Where(x => x.UpdatedAt < at || (x.UpdatedAt == at && x.Id.CompareTo(id) < 0));
        }
        var rows = await query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var profiles = await people.ProfilesAsync(rows.Select(x => x.FirstUserId == me ? x.SecondUserId : x.FirstUserId), ct);
        var views = new List<DirectConversationView>();
        foreach (var c in rows.Take(limit))
        {
            var peer = c.FirstUserId == me ? c.SecondUserId : c.FirstUserId;
            var members = await db.ConversationMembers.AsNoTracking().Where(x => x.ConversationId == c.Id).ToListAsync(ct);
            var mine = members.Single(x => x.UserId == me);
            var messages = db.DirectMessages.AsNoTracking().Where(x => x.ConversationId == c.Id && x.Id > mine.ClearedThroughId);
            var last = await messages.OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            var preview = last is null ? "开始聊天" : last.Kind == "text" ? last.Text! : last.Kind == "record" ? "[记录]" : "[故事线]";
            var unread = await messages.LongCountAsync(x => x.Id > mine.ReadThroughId && x.SenderId != me, ct);
            views.Add(new(c.Id, c.FriendshipId, profiles.GetValueOrDefault(peer) ?? FriendService.MissingPerson(peer),
                preview.Length > 80 ? preview[..80] : preview, unread, members.Single(x => x.UserId == peer).ReadThroughId, c.UpdatedAt,
                await db.Friendships.AnyAsync(x => x.Id == c.FriendshipId && x.Active, ct), mine.ClearedThroughId));
        }
        var tail = rows.Take(limit).LastOrDefault();
        return new(views, rows.Count > limit ? $"{tail!.UpdatedAt.UtcTicks}|{tail.Id}" : null);
    }

    public async Task<SocialPage<DirectMessageView>> MessagesAsync(long me, Guid id, int limit, long? before, long? after, CancellationToken ct)
    {
        await RequireAsync(me, id, ct);
        limit = Math.Clamp(limit, 1, 100);
        var member = await db.ConversationMembers.AsNoTracking().SingleAsync(x => x.ConversationId == id && x.UserId == me, ct);
        var query = db.DirectMessages.AsNoTracking().Where(x => x.ConversationId == id && x.Id > member.ClearedThroughId);
        if (before != null) query = query.Where(x => x.Id < before);
        if (after != null) query = query.Where(x => x.Id > after);
        var rows = after is null
            ? await query.OrderByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct)
            : await query.OrderBy(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var selected = rows.Take(limit).ToList();
        var views = new List<DirectMessageView>();
        foreach (var row in selected.OrderBy(x => x.Id)) views.Add(await MapAsync(me, row, ct));
        return new(views, rows.Count > limit ? selected[^1].Id.ToString() : null);
    }

    public async Task<DirectConversationView> SummaryAsync(long me, Guid id, CancellationToken ct)
    {
        var c = await RequireAsync(me, id, ct);
        var peer = c.FirstUserId == me ? c.SecondUserId : c.FirstUserId;
        var members = await db.ConversationMembers.AsNoTracking().Where(x => x.ConversationId == id).ToListAsync(ct);
        var mine = members.Single(x => x.UserId == me);
        var rows = db.DirectMessages.AsNoTracking().Where(x => x.ConversationId == id && x.Id > mine.ClearedThroughId);
        var last = await rows.OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var profiles = await people.ProfilesAsync([peer], ct);
        var preview = last?.Kind == "text" ? last.Text ?? "" : last is null ? "开始聊天" : "[分享]";
        return new(id, c.FriendshipId, profiles.GetValueOrDefault(peer) ?? FriendService.MissingPerson(peer),
            preview[..Math.Min(preview.Length, 80)], await rows.LongCountAsync(x => x.Id > mine.ReadThroughId && x.SenderId != me, ct),
            members.Single(x => x.UserId == peer).ReadThroughId, c.UpdatedAt,
            await db.Friendships.AnyAsync(x => x.Id == c.FriendshipId && x.Active, ct), mine.ClearedThroughId);
    }

    public async Task<IReadOnlyList<ShareCardStatus>> ShareStatusesAsync(long me, Guid conversationId, Guid[] ids, CancellationToken ct)
    {
        await RequireAsync(me, conversationId, ct);
        if (ids.Length > 100) throw new DomainValidationException("一次最多检查 100 个分享。");
        var permitted = await db.DirectMessages.AsNoTracking().Where(m => m.ConversationId == conversationId &&
            m.ShareId != null && ids.Contains(m.ShareId.Value)).Select(m => m.ShareId!.Value).Distinct().ToListAsync(ct);
        var result = new List<ShareCardStatus>();
        foreach (var id in permitted)
        {
            var share = await shares.GetAsync(me, id, ct);
            result.Add(new(id, share.Document.Title, share.Available));
        }
        return result;
    }

    public async Task<DirectMessageView> SendAsync(long me, Guid id, SendDirectMessageInput input, CancellationToken ct)
    {
        if (input.ClientMessageId == Guid.Empty) throw new DomainValidationException("请重新发送这条消息。");
        var c = await RequireAsync(me, id, ct);
        var existing = await db.DirectMessages.AsNoTracking().FirstOrDefaultAsync(x => x.SenderId == me && x.ClientMessageId == input.ClientMessageId, ct);
        if (existing != null)
        {
            if (existing.ConversationId != id || existing.Kind != input.Kind || (input.Kind == "text" && existing.Text != input.Text?.Trim()))
                throw new DomainValidationException("这次发送与原消息不一致，请重新发送。");
            if (existing.ShareId is { } existingShareId && !await db.ContentShares.AnyAsync(s => s.Id == existingShareId &&
                s.EventId == input.EventId && s.StorylineId == input.StorylineId, ct))
                throw new DomainValidationException("这次分享与原内容不一致，请重新发送。");
            return await MapAsync(me, existing, ct);
        }
        await friends.GuardWriteAsync(me, c.FriendshipId, ct);
        if (input.Kind is not ("text" or "record" or "storyline")) throw new DomainValidationException("暂不支持这种消息。");
        if (input.Kind == "text" && (string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 8000))
            throw new DomainValidationException("请输入 1～8000 字的消息。");
        var peer = c.FirstUserId == me ? c.SecondUserId : c.FirstUserId;
        var share = input.Kind == "text" ? null : await shares.CreateAsync(me, peer, c.FriendshipId, input, ct);
        var m = new DirectMessage
        {
            ConversationId = id,
            SenderId = me,
            ClientMessageId = input.ClientMessageId,
            Kind = input.Kind,
            Text = input.Kind == "text" ? input.Text!.Trim() : null,
            ShareId = share?.Id,
            CreatedAt = clock.GetUtcNow()
        };
        c.UpdatedAt = m.CreatedAt;
        db.DirectMessages.Add(m);
        friends.Notify(peer, "message", "收到一条新消息", $"/messages/{id}");
        friends.Notify(me, "message-sync", "消息已发送", $"/messages/{id}");
        await db.SaveChangesAsync(ct);
        return await MapAsync(me, m, ct);
    }

    public async Task ReadAsync(long me, Guid id, long through, bool clear, CancellationToken ct)
    {
        var c = await RequireAsync(me, id, ct);
        var latest = await db.DirectMessages.Where(x => x.ConversationId == id).MaxAsync(x => (long?)x.Id, ct) ?? 0;
        through = clear ? latest : Math.Clamp(through, 0, latest);
        if (!await db.ConversationMembers.AnyAsync(x => x.ConversationId == id && x.UserId == me &&
            (x.ReadThroughId < through || (clear && x.ClearedThroughId < through)), ct)) return;
        await db.ConversationMembers.Where(x => x.ConversationId == id && x.UserId == me).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.ReadThroughId, x => x.ReadThroughId > through ? x.ReadThroughId : through)
            .SetProperty(x => x.ClearedThroughId, x => clear ? through : x.ClearedThroughId), ct);
        friends.Notify(me, "read-sync", clear ? "聊天记录已清除" : "未读状态已更新", $"/messages/{id}");
        friends.Notify(c.FirstUserId == me ? c.SecondUserId : c.FirstUserId, "read-sync", "消息已读", $"/messages/{id}");
        await db.SaveChangesAsync(ct);
    }

    private async Task<DirectMessageView> MapAsync(long me, DirectMessage m, CancellationToken ct)
    {
        var share = m.ShareId is { } id ? await shares.GetAsync(me, id, ct) : null;
        return new(m.Id, m.ConversationId, m.SenderId.ToString(), m.ClientMessageId, m.Kind, m.Text,
            m.ShareId, share?.Document.Title, share?.Available ?? false, m.CreatedAt);
    }
}
