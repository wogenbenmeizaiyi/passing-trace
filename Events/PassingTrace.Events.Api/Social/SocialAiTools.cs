using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Ai.Capabilities;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Social;

public sealed record FriendActivity(FriendView Friend, long RecordCount, DateTimeOffset? LastHappenedAt,
    long PlannedCount, long UndatedCount, IReadOnlyList<RecordEvidence> Records);
public sealed record FriendActivityResult(DateTimeOffset From, DateTimeOffset To, string Unit,
    IReadOnlyList<FriendActivity> Items);
public sealed record SharedEvidence(Guid ShareId, string Title, string AuthorId, string Snippet, string AccessPath);

public sealed class SocialAiTools(TraceDbContext db, CurrentUserContext currentUser, FriendService friends,
    SharedContentService content, TimeProvider clock)
{
    private readonly List<FriendView> _friends = [];
    private readonly List<SharedEvidence> _shares = [];
    private FriendActivityResult? _activities;
    private AssistantCalendarRange? _month;
    private bool _sharedRequested;
    public bool HasEvidence => _friends.Count > 0 || _shares.Count > 0 || _activities != null;
    public void Configure(AssistantCalendarContext calendar, string question, bool sharedFollowUp)
    {
        _month = calendar.ResolveCurrentMonth(question);
        _sharedRequested = sharedFollowUp || new[] { "分享", "发给", "发来", "收到", "shared" }.Any(x => question.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
    public EvidenceBundle Merge(EvidenceBundle evidence) => evidence with
    {
        Friends = _friends.DistinctBy(x => x.Id).ToArray(),
        FriendActivities = _activities,
        SharedContents = _shares.DistinctBy(x => x.ShareId).ToArray(),
        Records = evidence.Records.Concat(_activities?.Items.SelectMany(x => x.Records) ?? []).DistinctBy(x => x.EventId).ToArray(),
    };

    [Description("查找自己的当前好友，支持昵称、私人备注、私人关系标签或双方确认的关系。同名返回多位候选，不搜索陌生人。好友引用使用 [Friend #id]，id 是返回的好友关系标识；界面会显示可点击的名字，不向用户展示内部标识。")]
    public async Task<IReadOnlyList<FriendView>> SearchMyFriends([MaxLength(100)] string query = "", CancellationToken cancellationToken = default)
    {
        var found = (await friends.ListAsync(currentUser.UserId, query, cancellationToken)).Take(30).ToArray();
        _friends.AddRange(found);
        return found;
    }

    [Description("读取当前好友的关系；remark/label 是本人私人设置，relationship 才是双方确认。不可凭共同记录次数认定恋人。")]
    public async Task<FriendView?> GetMyFriendRelationship(Guid friendshipId, CancellationToken cancellationToken = default)
    {
        var friend = (await friends.ListAsync(currentUser.UserId, null, cancellationToken)).FirstOrDefault(x => x.Id == friendshipId);
        if (friend is not null) _friends.Add(friend);
        return friend;
    }

    [Description("精确统计与好友的共同记录并排名。单位为记录条数而不是见面次数；同一记录仅计一次。默认最近30天，本月按用户日历；返回独立的待执行计划及无发生时间记录数量。")]
    public async Task<FriendActivityResult> AggregateMyFriendActivities(
        [DataType(DataType.DateTime)] DateTimeOffset? from = null, [DataType(DataType.DateTime)] DateTimeOffset? to = null,
        Guid? friendshipId = null, [Range(1, 20)] int limit = 10, CancellationToken cancellationToken = default)
    {
        var me = currentUser.UserId;
        var end = (_month?.To ?? to ?? clock.GetUtcNow()).ToUniversalTime();
        if (_month != null && end > clock.GetUtcNow()) end = clock.GetUtcNow();
        var start = (_month?.From ?? from ?? end.AddDays(-30)).ToUniversalTime();
        if (start > end) throw new DomainValidationException("开始日期不能晚于结束日期。");
        var all = await friends.ListAsync(me, null, cancellationToken);
        if (friendshipId != null) all = all.Where(x => x.Id == friendshipId).ToArray();
        var results = new List<FriendActivity>();
        foreach (var friend in all)
        {
            var other = long.Parse(friend.Person.Id);
            var query = content.ReadableEvents(me).AsNoTracking().Where(e => e.UserId == other ||
                db.EventParticipants.Any(p => p.EventId == e.Id && p.UserId == other && p.Active && db.Friendships.Any(f => f.Id == p.FriendshipId && f.Active)));
            var completed = query.Where(e => e.Status == EventStatus.Completed && e.HappenedAt >= start && e.HappenedAt <= end);
            var count = await completed.LongCountAsync(cancellationToken);
            var recent = await completed.OrderByDescending(x => x.HappenedAt).ThenByDescending(x => x.Id).Take(5).ToListAsync(cancellationToken);
            var planned = await query.LongCountAsync(e => e.Status == EventStatus.Planned && e.PlannedAt >= start && e.PlannedAt <= end, cancellationToken);
            var undated = await query.LongCountAsync(e => e.Status == EventStatus.Completed && e.HappenedAt == null, cancellationToken);
            results.Add(new(friend, count, recent.FirstOrDefault()?.HappenedAt, planned, undated, recent.Select(e => new RecordEvidence(
                e.Id, e.CurrentSourceRevision, e.Title, e.RawContent is { Length: > 240 } raw ? raw[..240] : e.RawContent ?? "",
                null, e.HappenedAt, e.CreatedAt, 1, AuthorId: e.UserId.ToString(), AccessPath: e.UserId == me ? null : $"/joint-records/{e.Id}")).ToArray()));
        }
        _activities = new(start, end, "共同记录条数", results.OrderByDescending(x => x.RecordCount).ThenByDescending(x => x.LastHappenedAt)
            .ThenBy(x => x.Friend.Id).Take(Math.Clamp(limit, 1, 20)).ToArray());
        _friends.AddRange(_activities.Items.Select(x => x.Friend));
        return _activities;
    }

    [Description("仅当用户明确询问好友分享的内容时检索。结果为发送时版本，来自好友，不代表用户本人参与；引用使用 [Share #shareId]。")]
    public async Task<IReadOnlyList<SharedEvidence>> SearchSharedContent([MaxLength(200)] string query = "", Guid? friendshipId = null,
        [Range(1, 20)] int limit = 10, CancellationToken cancellationToken = default)
    {
        if (!_sharedRequested) return [];
        var me = currentUser.UserId;
        var q = db.ContentShares.AsNoTracking().Where(x => x.RecipientId == me && x.RevokedAt == null &&
            db.Friendships.Any(f => f.Id == x.FriendshipId && f.Active));
        if (friendshipId != null) q = q.Where(x => x.FriendshipId == friendshipId);
        if (!string.IsNullOrWhiteSpace(query)) q = q.Where(x => x.SearchText.Contains(query));
        var candidates = await q.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 20)).Select(x => x.Id).ToListAsync(cancellationToken);
        var result = new List<SharedEvidence>();
        foreach (var id in candidates)
        {
            var share = await content.GetAsync(me, id, cancellationToken);
            if (!share.Available) continue;
            var snippet = string.Join('\n', share.Document.Records.Where(x => x.Available).Select(x => x.Title + ": " + x.Content));
            result.Add(new(id, share.Document.Title, share.OwnerId, snippet.Length > 4000 ? snippet[..4000] : snippet, $"/shares/{id}"));
        }
        _shares.AddRange(result);
        return result;
    }
}

public sealed class FriendsCapabilityPackage(SocialAiTools tools) : IAiCapabilityPackage
{
    public string Key => "friends";
    public bool IsAvailable => true;
    public IReadOnlyList<string> Capabilities { get; } = ["friends", "relationships", "joint-records", "friend-statistics", "received-shares"];
    public IReadOnlyList<AITool> CreateTools() =>
    [
        AiFunctionToolFactory.Create(tools, nameof(SocialAiTools.SearchMyFriends), "SearchMyFriends"),
        AiFunctionToolFactory.Create(tools, nameof(SocialAiTools.GetMyFriendRelationship), "GetMyFriendRelationship"),
        AiFunctionToolFactory.Create(tools, nameof(SocialAiTools.AggregateMyFriendActivities), "AggregateMyFriendActivities"),
        AiFunctionToolFactory.Create(tools, nameof(SocialAiTools.SearchSharedContent), "SearchSharedContent"),
    ];
}
