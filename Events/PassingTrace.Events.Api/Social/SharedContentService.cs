using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Events;
using PassingTrace.Core.Social;
using PassingTrace.Events.Api.Media;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Social;

public sealed record SharedRecordListItem(long Id, string Title, string AuthorId, DateTimeOffset? HappenedAt);

public sealed class SharedContentService(TraceDbContext db, FriendService friends, IObjectStorage storage, TimeProvider clock)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public IQueryable<Event> ReadableEvents(long me) => db.Events.Where(e => e.DeletedAt == null &&
        (e.UserId == me || db.EventParticipants.Any(p => p.EventId == e.Id && p.UserId == me && p.Active &&
            db.Friendships.Any(f => f.Id == p.FriendshipId && f.Active &&
                ((f.FirstUserId == me && f.SecondUserId == e.UserId) || (f.SecondUserId == me && f.FirstUserId == e.UserId))))));

    public async Task<SocialPage<SharedRecordListItem>> JointRecordsAsync(long me, int limit, long? before, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 50);
        var query = ReadableEvents(me).AsNoTracking().Where(e => e.UserId != me);
        if (before != null) query = query.Where(x => x.Id < before);
        var rows = await query.OrderByDescending(x => x.Id).Take(limit + 1)
            .Select(x => new SharedRecordListItem(x.Id, x.Title ?? "共同记录", x.UserId.ToString(), x.HappenedAt)).ToListAsync(ct);
        return new(rows.Take(limit).ToArray(), rows.Count > limit ? rows[limit - 1].Id.ToString() : null);
    }

    public async Task<SharedDocument> MentionAsync(long me, long eventId, CancellationToken ct)
    {
        var evt = await ReadableEvents(me).AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, ct)
            ?? throw new KeyNotFoundException("这条共同记录已不可查看。");
        return await BuildAsync(evt.UserId, "record", evt.Id, null, ct);
    }

    public async Task<SharedDocument> BuildAsync(long owner, string kind, long? eventId, Guid? storylineId, CancellationToken ct)
    {
        if (kind == "record" && eventId is not null)
        {
            var evt = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId && x.UserId == owner && x.DeletedAt == null, ct)
                ?? throw new KeyNotFoundException("记录不存在或已删除。");
            var r = await RecordAsync(evt.Id, evt.CurrentSourceRevision, ct);
            return new("record", r.Title, null, owner.ToString(), [r], [], [], [], evt.Status.ToString());
        }
        if (kind != "storyline" || storylineId is null) throw new DomainValidationException("请选择要分享的记录或故事线。");
        var story = await db.Storylines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storylineId && x.UserId == owner && x.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("故事线不存在或已删除。");
        var revision = await db.StorylineRevisions.AsNoTracking().Include(x => x.Stages).Include(x => x.Nodes).Include(x => x.Edges)
            .AsSplitQuery().SingleAsync(x => x.StorylineId == story.Id && x.Revision == story.CurrentRevision, ct);
        var records = new List<SharedRecord>();
        foreach (var node in revision.Nodes.OrderBy(x => x.SemanticOrder))
        {
            if (!await db.Events.AnyAsync(x => x.Id == node.EventId && x.UserId == owner && x.DeletedAt == null, ct))
                records.Add(UnavailableRecord(node.EventId, node.SourceRevision));
            else records.Add(await RecordAsync(node.EventId, node.SourceRevision, ct));
        }
        return new("storyline", revision.Title, revision.Description, owner.ToString(), records,
            revision.Stages.OrderBy(x => x.SemanticOrder).Select(x => new SharedStage(x.Key, x.Title, x.SemanticOrder)).ToArray(),
            revision.Nodes.OrderBy(x => x.SemanticOrder).Select(x => new SharedNode(x.Key, x.EventId, x.StageKey, x.SemanticOrder)).ToArray(),
            revision.Edges.Select(x => new SharedEdge(x.SourceNodeKey, x.TargetNodeKey, x.RelationType.ToString(), x.Label)).ToArray(),
            revision.Status.ToString());
    }

    private async Task<SharedRecord> RecordAsync(long eventId, int revision, CancellationToken ct)
    {
        var source = await db.SourceRevisions.AsNoTracking().Include(x => x.Event)
            .Include(x => x.MediaAssets).ThenInclude(x => x.MediaAsset)
            .Include(x => x.Labels).Include(x => x.Locations).AsSplitQuery()
            .SingleAsync(x => x.EventId == eventId && x.Revision == revision, ct);
        return new(eventId, revision, source.Title ?? "无标题记录", source.RawContent, source.HappenedAt, source.PlannedAt,
            source.Event.Status.ToString(), source.MediaAssets.Where(x => x.MediaAsset.DeletedAt == null && x.MediaAsset.ConfirmedAt != null)
                .OrderBy(x => x.SortOrder).Select(x => new SharedMedia(x.MediaAssetId, x.MediaAsset.OriginalFileName,
                    x.MediaAsset.Kind.ToString(), x.MediaAsset.VerifiedMimeType ?? x.MediaAsset.DeclaredMimeType)).ToArray(),
            // Revisions created before social features were migrated with an empty string.
            // Keep their historical meaning: no participants, not the current record's participants.
            string.IsNullOrWhiteSpace(source.ParticipantIdsJson) ? [] : JsonSerializer.Deserialize<string[]>(source.ParticipantIdsJson) ?? [],
            Labels: source.Labels.Where(x => x.Decision == SourceLabelDecision.Include).OrderBy(x => x.SortOrder).Select(x => x.DisplayName).ToArray(),
            Places: source.Locations.Select(x => new SharedPlace(x.Name, x.Address, x.Latitude, x.Longitude, x.CoordinateSystem)).ToArray());
    }

    public async Task<ContentShare> CreateAsync(long owner, long recipient, Guid friendshipId, SendDirectMessageInput input, CancellationToken ct)
    {
        var f = await friends.GuardWriteAsync(owner, friendshipId, ct);
        if (FriendService.Other(f, owner) != recipient) throw new KeyNotFoundException("接收人不可用。");
        var doc = await BuildAsync(owner, input.Kind, input.EventId, input.StorylineId, ct);
        var revision = input.Kind == "record" ? doc.Records[0].Revision :
            await db.Storylines.Where(x => x.Id == input.StorylineId && x.UserId == owner).Select(x => x.CurrentRevision).SingleAsync(ct);
        var share = new ContentShare
        {
            FriendshipId = friendshipId,
            OwnerId = owner,
            RecipientId = recipient,
            Kind = input.Kind,
            EventId = input.Kind == "record" ? input.EventId : null,
            StorylineId = input.Kind == "storyline" ? input.StorylineId : null,
            Revision = revision,
            ContentJson = JsonSerializer.Serialize(doc, Json),
            SearchText = string.Join('\n', new[] { doc.Title, doc.Description ?? "" }.Concat(doc.Records.Select(x => x.Title + " " + x.Content))),
            CreatedAt = clock.GetUtcNow()
        };
        db.ContentShares.Add(share);
        await friends.TouchAsync([recipient], ct);
        return share;
    }

    public async Task<ShareView> GetAsync(long me, Guid id, CancellationToken ct)
    {
        var share = await db.ContentShares.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (x.OwnerId == me || x.RecipientId == me), ct)
            ?? throw new KeyNotFoundException("分享不存在。");
        var valid = share.RevokedAt == null && await db.Friendships.AnyAsync(x => x.Id == share.FriendshipId && x.Active, ct);
        valid &= share.Kind == "record"
            ? await db.Events.AnyAsync(x => x.Id == share.EventId && x.UserId == share.OwnerId && x.DeletedAt == null, ct)
            : await db.Storylines.AnyAsync(x => x.Id == share.StorylineId && x.UserId == share.OwnerId && x.DeletedAt == null, ct);
        if (!valid) return new(share.Id, share.OwnerId.ToString(), share.RecipientId.ToString(),
            new(share.Kind, "内容已不可查看", null, share.OwnerId.ToString(), [], [], [], [], Available: false), share.CreatedAt, false);
        var doc = JsonSerializer.Deserialize<SharedDocument>(share.ContentJson, Json)!;
        var eventIds = doc.Records.Select(x => x.EventId).ToArray();
        var alive = await db.Events.Where(x => eventIds.Contains(x.Id) && x.UserId == share.OwnerId && x.DeletedAt == null).Select(x => x.Id).ToListAsync(ct);
        var mediaIds = doc.Records.SelectMany(x => x.Media).Select(x => x.Id).ToArray();
        var mediaAlive = await db.MediaAssets.Where(x => mediaIds.Contains(x.Id) && x.DeletedAt == null && x.ConfirmedAt != null).Select(x => x.Id).ToListAsync(ct);
        doc = doc with
        {
            Records = doc.Records.Select(x => alive.Contains(x.EventId)
            ? x with { Media = x.Media.Where(m => mediaAlive.Contains(m.Id)).ToArray() } : UnavailableRecord(x.EventId, x.Revision)).ToArray()
        };
        return new(share.Id, share.OwnerId.ToString(), share.RecipientId.ToString(), doc, share.CreatedAt, true);
    }

    public async Task RevokeAsync(long me, Guid id, CancellationToken ct)
    {
        var share = await db.ContentShares.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == me, ct)
            ?? throw new KeyNotFoundException("分享不存在。");
        share.RevokedAt = clock.GetUtcNow();
        await friends.TouchAsync([share.RecipientId], ct);
        friends.Notify(share.RecipientId, "access-changed", "一条分享已停止展示", "/messages");
        friends.Notify(me, "access-changed", "一条分享已停止展示", "/messages");
        await db.SaveChangesAsync(ct);
    }

    public async Task<MediaContent> OpenMediaAsync(long me, Guid? shareId, long? eventId, Guid mediaId, CancellationToken ct)
    {
        var doc = shareId is { } id ? (await GetAsync(me, id, ct)).Document : await MentionAsync(me, eventId!.Value, ct);
        if (!doc.Available || !doc.Records.Any(x => x.Available && x.Media.Any(m => m.Id == mediaId)))
            throw new KeyNotFoundException("附件已不可查看。");
        var asset = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mediaId && x.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException("附件已不可查看。");
        return new(await storage.OpenReadAsync(asset.ObjectKey, ct), asset.OriginalFileName,
            asset.VerifiedMimeType ?? asset.DeclaredMimeType, asset.Kind is Core.Media.MediaKind.Image or Core.Media.MediaKind.Video);
    }

    private static SharedRecord UnavailableRecord(long id, int revision) => new(id, revision, "内容已不可查看", null, null, null, "", [], [], false);
}
