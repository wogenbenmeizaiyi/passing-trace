using PassingTrace.Events.Api.Ai.Evidence;
using System.Text.Json;
using PassingTrace.Core.Ai;

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

    public static async Task<EvidenceBundle?> ReadAsync(IAiEvidenceQueries queries, long me, string? json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var e = JsonSerializer.Deserialize<EvidenceBundle>(json, SharedContentService.Json);
        if (e is null || !IsSocial(json)) return e;
        var access = await queries.ReadEvidenceAccessAsync(me, e.Records.Select(x => x.EventId).ToArray(),
            (e.SharedContents ?? []).Select(x => x.ShareId).ToArray(), ct);
        var friends = access.FriendshipIds;
        var accessible = access.EventIds;
        var shares = access.ShareIds;
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
