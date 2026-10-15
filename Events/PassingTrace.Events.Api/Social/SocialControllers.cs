using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PassingTrace.Events.Api.Security;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Social;

[ApiController, Authorize, Route("api/v1/friends")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FriendsController(FriendService friends, SharedContentService content, EventParticipationService participation, TraceDbContext db) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(string? query, CancellationToken ct) => Ok(await friends.ListAsync(User.GetUserId(), query, ct));
    [HttpPut("{id:guid}/preference")]
    public async Task<IActionResult> Preference(Guid id, PreferenceInput input, CancellationToken ct)
    { await friends.PreferenceAsync(User.GetUserId(), id, input, ct); return NoContent(); }
    [HttpPost("{id:guid}/relationship")]
    public async Task<IActionResult> Relationship(Guid id, RelationshipInput input, CancellationToken ct)
    { await friends.RelationshipAsync(User.GetUserId(), id, "propose", input.Kind, input.Version, ct); return NoContent(); }
    [HttpPost("{id:guid}/relationship/decision")]
    public async Task<IActionResult> Decide(Guid id, DecisionInput input, CancellationToken ct)
    { await friends.RelationshipAsync(User.GetUserId(), id, input.Decision, null, input.Version, ct); return NoContent(); }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, bool block, CancellationToken ct)
    { await friends.RemoveAsync(User.GetUserId(), id, block, ct); return NoContent(); }
    [HttpGet("blocks")]
    public async Task<IActionResult> Blocks(CancellationToken ct) =>
        Ok(await db.UserBlocks.Where(x => x.UserId == User.GetUserId()).Select(x => x.BlockedUserId.ToString()).ToListAsync(ct));
    [HttpDelete("blocks/{id:long}")]
    public async Task<IActionResult> Unblock(long id, CancellationToken ct)
    { await db.UserBlocks.Where(x => x.UserId == User.GetUserId() && x.BlockedUserId == id).ExecuteDeleteAsync(ct); return NoContent(); }
    [HttpGet("shared-records")]
    public async Task<IActionResult> Joint(CancellationToken ct, int limit = 20, long? before = null) =>
        Ok(await content.JointRecordsAsync(User.GetUserId(), limit, before, ct));
    [HttpGet("shared-records/{id:long}")]
    public async Task<IActionResult> JointRecord(long id, CancellationToken ct) =>
        Ok(await content.MentionAsync(User.GetUserId(), id, ct));
    [HttpDelete("shared-records/{id:long}/participation")]
    public async Task<IActionResult> Detach(long id, CancellationToken ct)
    { await participation.DetachAsync(User.GetUserId(), id, ct); return NoContent(); }
    [HttpGet("shared-records/{id:long}/media/{mediaId:guid}")]
    public async Task<IActionResult> Media(long id, Guid mediaId, CancellationToken ct)
    {
        var media = await content.OpenMediaAsync(User.GetUserId(), null, id, mediaId, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return media.Inline ? File(media.Stream, media.ContentType) : File(media.Stream, media.ContentType, media.FileName);
    }
}

[ApiController, Authorize, Route("api/v1/friend-requests")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FriendRequestsController(FriendService friends) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await friends.RequestsAsync(User.GetUserId(), ct));
    [HttpPost]
    public async Task<IActionResult> Create(FriendRequestInput input, CancellationToken ct) =>
        Ok(new { id = await friends.RequestAsync(User.GetUserId(), input.Code, ct) });
    [HttpPost("{id:guid}/decision")]
    public async Task<IActionResult> Decision(Guid id, DecisionInput input, CancellationToken ct)
    { await friends.DecideAsync(User.GetUserId(), id, input.Decision, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/v1/conversations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DirectConversationsController(DirectChatService chat, TraceDbContext db) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Summary(Guid id, CancellationToken ct) =>
        Ok(await chat.SummaryAsync(User.GetUserId(), id, ct));
    [HttpGet("unread")]
    public async Task<IActionResult> Unread(CancellationToken ct)
    {
        var me = User.GetUserId();
        var count = await db.DirectMessages.LongCountAsync(m => m.SenderId != me && db.ConversationMembers.Any(p =>
            p.ConversationId == m.ConversationId && p.UserId == me && m.Id > p.ReadThroughId && m.Id > p.ClearedThroughId), ct);
        return Ok(new { count });
    }
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct, int limit = 20, string? cursor = null) =>
        Ok(await chat.ListAsync(User.GetUserId(), limit, cursor, ct));
    [HttpPost]
    public async Task<IActionResult> Open([FromBody] OpenConversationInput input, CancellationToken ct) =>
        Ok(new { id = await chat.OpenAsync(User.GetUserId(), input.FriendshipId, ct) });
    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> Messages(Guid id, CancellationToken ct, int limit = 30, long? before = null, long? after = null) =>
        Ok(await chat.MessagesAsync(User.GetUserId(), id, limit, before, after, ct));
    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> Send(Guid id, SendDirectMessageInput input, CancellationToken ct) =>
        Ok(await chat.SendAsync(User.GetUserId(), id, input, ct));
    [HttpPost("{id:guid}/share-statuses")]
    public async Task<IActionResult> ShareStatuses(Guid id, [FromBody] Guid[] ids, CancellationToken ct) =>
        Ok(await chat.ShareStatusesAsync(User.GetUserId(), id, ids, ct));
    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, ReadMessagesInput input, CancellationToken ct)
    { await chat.ReadAsync(User.GetUserId(), id, input.ThroughId, false, ct); return NoContent(); }
    [HttpDelete("{id:guid}/messages")]
    public async Task<IActionResult> Clear(Guid id, CancellationToken ct)
    { await chat.ReadAsync(User.GetUserId(), id, 0, true, ct); return NoContent(); }
}
public sealed record OpenConversationInput(Guid FriendshipId);
public sealed record ReadMessagesInput(long ThroughId);

[ApiController, Authorize, Route("api/v1/shares")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SharesController(SharedContentService content, TraceDbContext db) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(SendDirectMessageInput input, CancellationToken ct) =>
        Ok(await content.BuildAsync(User.GetUserId(), input.Kind, input.EventId, input.StorylineId, ct));
    [HttpGet]
    public async Task<IActionResult> Mine(CancellationToken ct, long? eventId = null, Guid? storylineId = null) =>
        Ok(await db.ContentShares.AsNoTracking().Where(x => x.OwnerId == User.GetUserId() &&
            (eventId == null || x.EventId == eventId) && (storylineId == null || x.StorylineId == storylineId))
            .OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new { x.Id, recipientId = x.RecipientId.ToString(), x.Kind, x.CreatedAt, x.RevokedAt }).ToListAsync(ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await content.GetAsync(User.GetUserId(), id, ct));
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    { await content.RevokeAsync(User.GetUserId(), id, ct); return NoContent(); }
    [HttpGet("{id:guid}/media/{mediaId:guid}")]
    public async Task<IActionResult> Media(Guid id, Guid mediaId, CancellationToken ct)
    {
        var media = await content.OpenMediaAsync(User.GetUserId(), id, null, mediaId, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return media.Inline ? File(media.Stream, media.ContentType) : File(media.Stream, media.ContentType, media.FileName);
    }
}

[ApiController, Authorize, Route("api/v1/notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SocialNotificationsController(TraceDbContext db, TimeProvider clock) : ControllerBase
{
    // Transport events must remain in the stream, but never masquerade as user notices.
    private static readonly string[] VisibleKinds = ["friend-request", "relationship", "mention", "access-changed"];
    private IQueryable<PassingTrace.Core.Social.SocialNotification> Query(bool visibleOnly) =>
        db.SocialNotifications.Where(x => x.UserId == User.GetUserId() && (!visibleOnly || VisibleKinds.Contains(x.Kind)));

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct, long? before = null, bool visibleOnly = false) => Ok(
        await Query(visibleOnly).AsNoTracking().Where(x => before == null || x.Id < before)
            .OrderByDescending(x => x.Id).Take(30).ToListAsync(ct));
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(new
    {
        UnreadCount = await Query(true).CountAsync(x => !x.Read, ct),
        Latest = await Query(true).AsNoTracking().OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct)
    });
    [HttpPut("read")]
    public async Task<IActionResult> Read(ReadMessagesInput input, CancellationToken ct, bool visibleOnly = false)
    {
        var changed = await Query(visibleOnly).Where(x => !x.Read && x.Id <= input.ThroughId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Read, true), ct);
        if (changed > 0)
        {
            db.SocialNotifications.Add(new() { UserId = User.GetUserId(), Kind = "notification-read-sync", Text = "通知状态已更新", CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }
    [HttpGet("stream")]
    public IResult Stream(CancellationToken ct, long after = 0)
    {
        Response.Headers["X-Accel-Buffering"] = "no";
        return TypedResults.ServerSentEvents(Events(User.GetUserId(), Math.Max(after, 0), ct));
    }
    private async IAsyncEnumerable<SseItem<object>> Events(long me, long after, [EnumeratorCancellation] CancellationToken ct)
    {
        var until = clock.GetUtcNow().AddSeconds(50);
        while (!ct.IsCancellationRequested && clock.GetUtcNow() < until)
        {
            var events = await db.SocialNotifications.AsNoTracking().Where(x => x.UserId == me && x.Id > after)
                .OrderBy(x => x.Id).Take(100).ToListAsync(ct);
            foreach (var item in events)
            {
                after = item.Id;
                yield return new SseItem<object>(new { item.Id, item.Kind, item.Text, item.Target, item.CreatedAt }, "notification") { EventId = item.Id.ToString() };
            }
            if (events.Count == 100) continue;
            yield return new SseItem<object>(new { cursor = after }, "heartbeat");
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
}
