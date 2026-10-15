using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Events;
using PassingTrace.Core.Social;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Social;
using PassingTrace.Events.Api.Storylines;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Development;

public sealed class SocialDemoSeeder(TraceDbContext db, ISocialIdentityClient people, FriendService friends,
    EventService events, StorylineService storylines, DirectChatService chat, TimeProvider clock,
    IHostEnvironment environment, IOptions<DevelopmentDemoOptions> options)
{
    private const string Marker = "development-social-v1";
    private static Guid Stable(long me, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{Marker}:{me}:{key}"))[..16]);
    public async Task SeedAsync(long me, CancellationToken ct)
    {
        if (!environment.IsDevelopment() || !options.Value.Enabled) return;
        if (await db.SocialNotifications.AnyAsync(n => n.UserId == me && n.Kind == Marker, ct)) return;
        var profiles = await people.DevelopmentPeopleAsync(ct);
        if (profiles.Count != 3) return;
        var ids = profiles.Select(p => long.Parse(p.Id)).ToArray();
        foreach (var (id, index) in ids.Take(2).Select((id, index) => (id, index)))
        {
            var key = Stable(me, $"friendship-{index}");
            // Never recreate a friendship a developer deliberately removed.
            if (await db.Friendships.AnyAsync(f => f.Id == key, ct)) continue;
            db.Friendships.Add(new Friendship
            {
                Id = key,
                FirstUserId = Math.Min(me, id),
                SecondUserId = Math.Max(me, id),
                CreatedAt = clock.GetUtcNow(),
                Relationship = index == 0 ? "亲密朋友" : null
            });
            db.FriendPreferences.Add(new FriendPreference { FriendshipId = key, UserId = me, Remark = index == 0 ? "一起跑步的小林" : "同事小周", Label = index == 0 ? "亲密朋友" : "同事" });
        }
        await db.SaveChangesAsync(ct);
        if (!await db.Friendships.AnyAsync(f => f.Id == Stable(me, "friendship-0") && f.Active, ct)) return;
        async Task<Event> Record(long author, string key, string title, string content, int days, long[] participants, bool plan = false)
        {
            var idempotency = $"{Marker}-{me}-{key}";
            var old = await db.Events.SingleOrDefaultAsync(e => e.UserId == author && e.IdempotencyKey == idempotency, ct);
            if (old != null) return old;
            return await events.CreateAsync(new CreateEventCommand(author, plan ? EventKind.Plan : EventKind.Trace, title, content,
                plan ? null : clock.GetUtcNow().AddDays(-days), plan ? clock.GetUtcNow().AddDays(3) : null,
                "Asia/Shanghai", idempotency, ParticipantIds: participants.Select(p => p.ToString()).ToArray()), ct);
        }
        var run = await Record(me, "run", "和小林沿湖跑了五公里", "沿湖慢跑，结束后一起吃了早餐。", 3, [ids[0]]);
        var dinner = await Record(me, "dinner", "和小林、小周一起吃晚饭", "三个人试了一家新开的烤肉店。", 2, [ids[0], ids[1]]);
        await Record(me, "plan", "周末和小林去爬山", "还没有出发，先把路线准备好。", 0, [ids[0]], true);
        var incoming = await Record(ids[0], "incoming", "和你一起逛了书店", "在书店待了一下午，各自挑了一本书。", 1, [me]);
        var privateRecord = await Record(ids[0], "private", "小林自己的阅读笔记", "这条没有 @ 任何人，用于检查私人记录不能被其他人读取。", 1, []);
        _ = privateRecord;
        var storylineKey = $"{Marker}-{me}-story";
        var storyline = await db.Storylines.SingleOrDefaultAsync(s => s.UserId == me && s.CreationIdempotencyKey == storylineKey, ct);
        Guid storyId;
        if (storyline is null)
        {
            var stage = Stable(me, "stage"); var first = Stable(me, "node-run"); var second = Stable(me, "node-dinner");
            var saved = await storylines.CreateAsync(me, new SaveStorylineRequest("和朋友的一周", "跑步与聚餐的共同回忆", "activity", StorylineStatus.Ongoing,
                null, ["共同经历"], [new(stage, "一起度过", 0)],
                [new(first, "existing-event", run.Id, run.CurrentSourceRevision, null, stage, 0), new(second, "existing-event", dinner.Id, dinner.CurrentSourceRevision, null, stage, 1)],
                [new(Stable(me, "edge"), first, second, StorylineRelationType.Sequence, "然后一起吃饭")], null), storylineKey, ct);
            storyId = saved.Storyline.Id;
        }
        else storyId = storyline.Id;
        var conversation = await chat.OpenAsync(me, Stable(me, "friendship-0"), ct);
        await chat.SendAsync(ids[0], conversation, new(Stable(me, "hello"), "text", "这里是演示聊天：共同记录随作者更新，主动分享固定为发送时的版本。"), ct);
        await chat.SendAsync(ids[0], conversation, new(Stable(me, "share-incoming"), "record", EventId: incoming.Id), ct);
        await chat.SendAsync(me, conversation, new(Stable(me, "share-story"), "storyline", StorylineId: storyId), ct);
        await friends.TouchAsync([me, .. ids.Take(2)], ct);
        friends.Notify(me, Marker, "好友与分享演示数据已准备", "/messages");
        await db.SaveChangesAsync(ct);
    }
}
