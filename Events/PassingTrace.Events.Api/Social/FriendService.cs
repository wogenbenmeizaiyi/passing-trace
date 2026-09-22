using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Events;
using PassingTrace.Core.Social;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Social;

public sealed class FriendService(TraceDbContext db, ISocialIdentityClient people, IAnalysisOutbox outbox, TimeProvider clock)
{
    public IQueryable<Friendship> Active(long userId) => db.Friendships.Where(x => x.Active &&
        (x.FirstUserId == userId || x.SecondUserId == userId));
    public static long Other(Friendship f, long me) => f.FirstUserId == me ? f.SecondUserId : f.FirstUserId;
    public static PersonProfile MissingPerson(long id) => new(id.ToString(), "暂不可用的用户", "", false, "");

    public async Task<Friendship> RequireAsync(long userId, Guid id, CancellationToken ct) =>
        await Active(userId).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("好友关系已失效，请刷新后重试。");

    // Grant/message mutations join the friendship optimistic transaction, so a
    // concurrent delete or block rolls back the entire SaveChanges operation.
    public async Task<Friendship> GuardWriteAsync(long userId, Guid id, CancellationToken ct)
    {
        var f = await RequireAsync(userId, id, ct);
        f.Version = Guid.NewGuid();
        return f;
    }

    public async Task<IReadOnlyList<FriendView>> ListAsync(long userId, string? query, CancellationToken ct)
    {
        var rows = await Active(userId).AsNoTracking().OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var prefs = await db.FriendPreferences.AsNoTracking().Where(x => x.UserId == userId).ToDictionaryAsync(x => x.FriendshipId, ct);
        var profiles = await people.ProfilesAsync(rows.Select(x => Other(x, userId)), ct);
        return rows.Select(f => new FriendView(f.Id, profiles.GetValueOrDefault(Other(f, userId)) ?? MissingPerson(Other(f, userId)),
                prefs.GetValueOrDefault(f.Id)?.Remark ?? "", prefs.GetValueOrDefault(f.Id)?.Label ?? "朋友",
                f.Relationship, f.ProposedRelationship, f.RelationshipRequestedBy?.ToString(), f.Version))
            .Where(x => string.IsNullOrWhiteSpace(query) || new[] { x.Person.Nickname, x.Remark, x.Label, x.Relationship ?? "" }
                .Any(v => v.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    public async Task<IReadOnlyList<FriendRequestView>> RequestsAsync(long me, CancellationToken ct)
    {
        var rows = await db.FriendRequests.AsNoTracking().Where(x => (x.SenderId == me || x.RecipientId == me) && x.Status == "pending")
            .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
        var profiles = await people.ProfilesAsync(rows.Select(x => x.SenderId == me ? x.RecipientId : x.SenderId), ct);
        return rows.Select(x => new FriendRequestView(x.Id,
            profiles.GetValueOrDefault(x.SenderId == me ? x.RecipientId : x.SenderId) ?? MissingPerson(x.SenderId == me ? x.RecipientId : x.SenderId),
            x.SenderId == me ? "sent" : "received", x.Status, x.CreatedAt)).ToArray();
    }

    public async Task<Guid> RequestAsync(long me, string code, CancellationToken ct)
    {
        var person = await people.ResolveAsync(code, ct) ?? throw new DomainValidationException("没有找到这个好友码，请检查后重试。");
        var other = long.Parse(person.Id);
        if (me == other) throw new DomainValidationException("这是你自己的好友码。");
        if (await BlockedAsync(me, other, ct)) throw new DomainValidationException("暂时无法向这位用户发送申请。");
        if (await Active(me).AnyAsync(x => x.FirstUserId == other || x.SecondUserId == other, ct))
            throw new DomainValidationException("你们已经是好友了。");
        var existing = await db.FriendRequests.FirstOrDefaultAsync(x => x.SenderId == me && x.RecipientId == other && x.Status == "pending", ct);
        if (existing != null) return existing.Id;
        var request = new FriendRequest { SenderId = me, RecipientId = other, CreatedAt = clock.GetUtcNow() };
        db.FriendRequests.Add(request);
        Notify(other, "friend-request", "收到一条好友申请", "/messages?tab=friends");
        await db.SaveChangesAsync(ct);
        return request.Id;
    }

    public async Task DecideAsync(long me, Guid id, string decision, CancellationToken ct)
    {
        var r = await db.FriendRequests.SingleOrDefaultAsync(x => x.Id == id && (x.SenderId == me || x.RecipientId == me), ct)
            ?? throw new KeyNotFoundException("申请不存在。");
        if (r.Status != "pending") return;
        if (decision == "withdraw" && r.SenderId == me) r.Status = "withdrawn";
        else if (decision is "accept" or "reject" && r.RecipientId == me)
        {
            if (decision == "accept")
            {
                if (await BlockedAsync(me, r.SenderId, ct)) throw new DomainValidationException("暂时无法添加这位好友。");
                var low = Math.Min(me, r.SenderId); var high = Math.Max(me, r.SenderId);
                if (!await db.Friendships.AnyAsync(x => x.FirstUserId == low && x.SecondUserId == high && x.Active, ct))
                    db.Friendships.Add(new Friendship { FirstUserId = low, SecondUserId = high, CreatedAt = clock.GetUtcNow() });
                var crossed = await db.FriendRequests.Where(x => x.SenderId == me && x.RecipientId == r.SenderId && x.Status == "pending").ToListAsync(ct);
                foreach (var x in crossed) { x.Status = "accepted"; x.Version = Guid.NewGuid(); }
                await TouchAsync([me, r.SenderId], ct);
            }
            r.Status = decision == "accept" ? "accepted" : "rejected";
            Notify(r.SenderId, "friend-request", decision == "accept" ? "好友申请已通过" : "好友申请未通过", "/messages?tab=friends");
        }
        else throw new DomainValidationException("无法执行这项操作。");
        r.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }

    public async Task PreferenceAsync(long me, Guid id, PreferenceInput input, CancellationToken ct)
    {
        await GuardWriteAsync(me, id, ct);
        if (input.Remark.Length > 100 || input.Label.Length > 30) throw new DomainValidationException("备注或关系标签太长了。");
        var p = await db.FriendPreferences.FindAsync([id, me], ct);
        if (p is null) { p = new() { FriendshipId = id, UserId = me }; db.FriendPreferences.Add(p); }
        p.Remark = input.Remark.Trim(); p.Label = input.Label.Trim();
        await TouchAsync([me], ct);
        Notify(me, "friend-sync", "好友设置已更新", "/messages?tab=friends");
        await db.SaveChangesAsync(ct);
    }

    public async Task RelationshipAsync(long me, Guid id, string action, string? kind, Guid? version, CancellationToken ct)
    {
        var f = await RequireAsync(me, id, ct);
        if (version != f.Version) throw new DomainValidationException("关系已更新，请刷新后再操作。");
        if (action == "propose")
        {
            if (kind is not ("亲密朋友" or "恋人" or "家人")) throw new DomainValidationException("请选择支持的关系。");
            f.ProposedRelationship = kind; f.RelationshipRequestedBy = me;
        }
        else if (action is "accept" or "reject")
        {
            if (f.RelationshipRequestedBy is null || f.RelationshipRequestedBy == me) throw new DomainValidationException("没有等待你确认的关系申请。");
            if (action == "accept") f.Relationship = f.ProposedRelationship;
            f.ProposedRelationship = null; f.RelationshipRequestedBy = null;
        }
        else if (action == "clear") { f.Relationship = null; f.ProposedRelationship = null; f.RelationshipRequestedBy = null; }
        else throw new DomainValidationException("无法执行这项操作。");
        f.Version = Guid.NewGuid();
        Notify(Other(f, me), "relationship", action == "propose" ? "收到关系确认申请" : "好友关系信息已更新", "/messages?tab=friends");
        Notify(me, "friend-sync", "好友关系信息已更新", "/messages?tab=friends");
        await TouchAsync([f.FirstUserId, f.SecondUserId], ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(long me, Guid id, bool block, CancellationToken ct)
    {
        var f = await RequireAsync(me, id, ct);
        var other = Other(f, me);
        f.Active = false; f.Version = Guid.NewGuid();
        if (block && !await db.UserBlocks.AnyAsync(x => x.UserId == me && x.BlockedUserId == other, ct))
            db.UserBlocks.Add(new UserBlock { UserId = me, BlockedUserId = other });
        var participants = await db.EventParticipants.Where(x => x.FriendshipId == id && x.Active).ToListAsync(ct);
        foreach (var p in participants) { p.Active = false; p.Version = Guid.NewGuid(); }
        var shares = await db.ContentShares.Where(x => x.FriendshipId == id && x.RevokedAt == null).ToListAsync(ct);
        foreach (var s in shares) s.RevokedAt = clock.GetUtcNow();
        await TouchAsync([me, other], ct);
        Notify(other, "access-changed", "部分共同内容的访问权限已更新", "/messages");
        Notify(me, "access-changed", "好友关系和相关访问权限已更新", "/messages");
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> BlockedAsync(long a, long b, CancellationToken ct) => db.UserBlocks.AnyAsync(
        x => (x.UserId == a && x.BlockedUserId == b) || (x.UserId == b && x.BlockedUserId == a), ct);

    public void Notify(long user, string kind, string text, string? target = null) => db.SocialNotifications.Add(new()
    { UserId = user, Kind = kind, Text = text, Target = target, CreatedAt = clock.GetUtcNow() });

    public async Task TouchAsync(IEnumerable<long> users, CancellationToken ct)
    {
        foreach (var user in users.Distinct()) await outbox.IncrementWatermarkAsync(user, clock.GetUtcNow(), ct);
    }
}
