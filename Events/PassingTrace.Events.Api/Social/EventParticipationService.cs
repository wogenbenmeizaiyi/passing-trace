using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Events;
using PassingTrace.Core.Social;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Social;

public interface IEventParticipationService
{
    Task ApplyAsync(Event evt, SourceRevision revision, IReadOnlyList<string>? users, CancellationToken ct);
    Task ChangedAsync(Event evt, CancellationToken ct);
}

public sealed class EventParticipationService(TraceDbContext db, FriendService friends) : IEventParticipationService
{
    public async Task ApplyAsync(Event evt, SourceRevision revision, IReadOnlyList<string>? users, CancellationToken ct)
    {
        if (users is not null)
        {
            if (users.Count > 30 || users.Any(x => !long.TryParse(x, out var id) || id <= 0 || id == evt.UserId))
                throw new DomainValidationException("请选择有效的好友，一条记录最多添加 30 位。");
            var ids = users.Select(long.Parse).Distinct().ToArray();
            var links = await friends.Active(evt.UserId).ToListAsync(ct);
            foreach (var id in ids)
            {
                var link = links.FirstOrDefault(x => FriendService.Other(x, evt.UserId) == id)
                    ?? throw new DomainValidationException("只能标记当前好友，请刷新好友列表。");
                var p = evt.Participants.FirstOrDefault(x => x.UserId == id);
                link.Version = Guid.NewGuid();
                if (p?.Declined == true) throw new DomainValidationException("对方已移除这条记录的关联，请尊重对方的选择。");
                if (p is null) { p = new EventParticipant { Event = evt, UserId = id }; evt.Participants.Add(p); }
                if (!p.Active || p.FriendshipId != link.Id)
                    friends.Notify(id, "mention", "好友在共同记录中提到了你", "/events?scope=joint");
                p.Active = true; p.FriendshipId = link.Id; p.Version = Guid.NewGuid();
            }
            foreach (var p in evt.Participants.Where(x => !ids.Contains(x.UserId))) { p.Active = false; p.Version = Guid.NewGuid(); }
        }
        revision.ParticipantIdsJson = JsonSerializer.Serialize(evt.Participants.Where(x => x.Active).Select(x => x.UserId.ToString()).ToArray());
        await ChangedAsync(evt, ct);
    }

    public async Task ChangedAsync(Event evt, CancellationToken ct)
    {
        var recipients = evt.Participants.Select(x => x.UserId).ToHashSet();
        if (evt.Id != 0)
        {
            var sharedWith = await db.ContentShares.Where(x => x.RevokedAt == null && (x.EventId == evt.Id ||
                (x.StorylineId != null && db.StorylineNodes.Any(n => n.EventId == evt.Id && n.Revision.StorylineId == x.StorylineId))))
                .Select(x => x.RecipientId).ToListAsync(ct);
            recipients.UnionWith(sharedWith);
        }
        await friends.TouchAsync(recipients, ct);
        foreach (var recipient in recipients) friends.Notify(recipient, "access-changed", "共同记录或分享内容已更新", "/events");
    }

    public async Task DetachAsync(long me, long eventId, CancellationToken ct)
    {
        var p = await db.EventParticipants.Include(x => x.Event).FirstOrDefaultAsync(x => x.EventId == eventId && x.UserId == me && x.Active, ct)
            ?? throw new KeyNotFoundException("共同记录已不可查看。");
        p.Active = false; p.Declined = true; p.Version = Guid.NewGuid();
        friends.Notify(p.Event.UserId, "access-changed", "好友移除了共同记录关联", "/events");
        friends.Notify(me, "access-changed", "共同记录关联已移除", "/events?scope=joint");
        await friends.TouchAsync([me, p.Event.UserId], ct);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class NoopEventParticipationService : IEventParticipationService
{
    public Task ApplyAsync(Event evt, SourceRevision revision, IReadOnlyList<string>? users, CancellationToken ct)
    {
        if (users?.Count > 0) throw new DomainValidationException("好友功能暂时不可用。");
        return Task.CompletedTask;
    }
    public Task ChangedAsync(Event evt, CancellationToken ct) => Task.CompletedTask;
}
