using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Social;

public static class SocialEvidenceGuard
{
    public static bool IsSocial(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var e = JsonSerializer.Deserialize<EvidenceBundle>(json, SharedContentService.Json);
            return e?.Friends?.Count > 0 || e?.FriendActivities != null || e?.SharedContents?.Count > 0 || e?.Records.Any(x => x.AccessPath != null) == true;
        }
        catch (JsonException) { return false; }
    }

    public static async Task<EvidenceBundle?> ReadAsync(TraceDbContext db, long me, string? json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var e = JsonSerializer.Deserialize<EvidenceBundle>(json, SharedContentService.Json);
        if (e is null || !IsSocial(json)) return e;
        var friends = await db.Friendships.AsNoTracking().Where(x => x.Active && (x.FirstUserId == me || x.SecondUserId == me)).Select(x => x.Id).ToListAsync(ct);
        var ids = e.Records.Select(x => x.EventId).ToArray();
        var accessible = await db.Events.Where(x => ids.Contains(x.Id) && x.DeletedAt == null && (x.UserId == me ||
            db.EventParticipants.Any(p => p.EventId == x.Id && p.UserId == me && p.Active && friends.Contains(p.FriendshipId))))
            .Select(x => x.Id).ToListAsync(ct);
        var shareIds = (e.SharedContents ?? []).Select(x => x.ShareId).ToArray();
        var shares = await db.ContentShares.Where(x => shareIds.Contains(x.Id) && x.RecipientId == me && x.RevokedAt == null && friends.Contains(x.FriendshipId) &&
            ((x.EventId != null && db.Events.Any(ev => ev.Id == x.EventId && ev.DeletedAt == null)) ||
             (x.StorylineId != null && db.Storylines.Any(s => s.Id == x.StorylineId && s.DeletedAt == null))))
            .Select(x => x.Id).ToListAsync(ct);
        return e with
        {
            Records = e.Records.Where(x => accessible.Contains(x.EventId)).ToArray(),
            Friends = e.Friends?.Where(x => friends.Contains(x.Id)).ToArray(),
            FriendActivities = e.FriendActivities is { } a ? a with
            {
                Items = a.Items.Where(x => friends.Contains(x.Friend.Id))
                .Select(x => x with { Records = x.Records.Where(r => accessible.Contains(r.EventId)).ToArray() }).ToArray()
            } : null,
            SharedContents = e.SharedContents?.Where(x => shares.Contains(x.ShareId)).Select(x => x with { Snippet = "" }).ToArray()
        };
    }
}
