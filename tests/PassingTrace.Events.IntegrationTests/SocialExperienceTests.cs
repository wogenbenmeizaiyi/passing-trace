using System.Security.Claims;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using PassingTrace.Events.Api.Development;
using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Media;
using PassingTrace.Events.Api.Social;
using PassingTrace.Events.Api.Storylines;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai.Capabilities;
using Microsoft.Extensions.AI;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class SocialExperienceTests : IClassFixture<StorylinePostgresFixture>, IAsyncLifetime
{
    private readonly StorylinePostgresFixture _fixture;
    private TraceDbContext _db = null!;
    private FriendService _friends = null!;
    private SharedContentService _shares = null!;
    private EventParticipationService _participants = null!;
    private DirectChatService _chat = null!;
    private EventService _events = null!;
    private readonly FixedClock _clock = new();
    private readonly FakePeople _people = new();
    private readonly FakeStorage _storage = new();
    private static long _sequence = 60000;
    private readonly long _a = Interlocked.Add(ref _sequence, 10);
    private long B => _a + 1;
    private long C => _a + 2;
    public SocialExperienceTests(StorylinePostgresFixture fixture) => _fixture = fixture;
    public Task InitializeAsync()
    {
        _db = new(_fixture.Options);
        var outbox = new AnalysisOutbox(_db);
        _friends = new(_db, _people, outbox, _clock);
        _shares = new(_db, _friends, _storage, _clock);
        _participants = new(_db, _friends);
        _chat = new(_db, _friends, _shares, _people, _clock);
        _events = new(new EventRepository(_db), _clock, new MediaService(_db, _storage, outbox, _clock), outbox, _participants);
        return Task.CompletedTask;
    }
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();
    private async Task<Guid> Befriend(long a, long b)
    {
        var request = await _friends.RequestAsync(a, b.ToString(), default);
        await _friends.DecideAsync(b, request, "accept", default);
        return (await _friends.ListAsync(a, null, default)).Single(x => x.Person.Id == b.ToString()).Id;
    }
    [Fact]
    public async Task Visible_notifications_filter_before_paging_and_share_read_rules()
    {
        var controller = new SocialNotificationsController(_db, _clock)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", _a.ToString())], "test")) }
            }
        };
        for (var index = 0; index < 35; index++)
        {
            _db.SocialNotifications.Add(new() { UserId = _a, Kind = "mention", Text = $"共同记录{index}", CreatedAt = _clock.GetUtcNow() });
            _db.SocialNotifications.Add(new() { UserId = _a, Kind = "message-sync", Text = "内部事件", CreatedAt = _clock.GetUtcNow() });
        }
        _db.SocialNotifications.Add(new() { UserId = B, Kind = "mention", Text = "其他账号", CreatedAt = _clock.GetUtcNow() });
        await _db.SaveChangesAsync();
        var page = Assert.IsType<List<PassingTrace.Core.Social.SocialNotification>>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.List(default, visibleOnly: true)).Value);
        Assert.Equal(30, page.Count);
        Assert.All(page, item => { Assert.Equal(_a, item.UserId); Assert.Equal("mention", item.Kind); });
        var older = Assert.IsType<List<PassingTrace.Core.Social.SocialNotification>>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.List(default, page.Last().Id, true)).Value);
        Assert.Equal(5, older.Count);
        var summary = JsonSerializer.SerializeToElement(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Summary(default)).Value);
        Assert.Equal(35, summary.GetProperty("UnreadCount").GetInt32());
        await controller.Read(new(page.First().Id), default, visibleOnly: true);
        Assert.Equal(0, await _db.SocialNotifications.CountAsync(n => n.UserId == _a && n.Kind == "mention" && !n.Read));
        Assert.Equal(35, await _db.SocialNotifications.CountAsync(n => n.UserId == _a && n.Kind == "message-sync" && !n.Read));
        Assert.Equal(1, await _db.SocialNotifications.CountAsync(n => n.UserId == B && !n.Read));
        await controller.Read(new(page.First().Id), default, visibleOnly: true);
        Assert.Equal(1, await _db.SocialNotifications.CountAsync(n => n.UserId == _a && n.Kind == "notification-read-sync"));
        summary = JsonSerializer.SerializeToElement(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Summary(default)).Value);
        Assert.Equal(0, summary.GetProperty("UnreadCount").GetInt32());
        Assert.Equal("mention", summary.GetProperty("Latest").GetProperty("Kind").GetString());
        var compatible = Assert.IsType<List<PassingTrace.Core.Social.SocialNotification>>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.List(default)).Value);
        Assert.Contains(compatible, item => item.Kind == "notification-read-sync");
    }
    private Task<Event> Record(long owner, string title, string[]? people = null, EventKind kind = EventKind.Trace,
        DateTimeOffset? happened = null, bool undated = false, Guid[]? media = null) => _events.CreateAsync(new(owner, kind,
        title, "共同记录正文", kind == EventKind.Trace && !undated ? happened ?? _clock.GetUtcNow().AddDays(-1) : null,
        kind == EventKind.Plan ? _clock.GetUtcNow().AddDays(-1) : null, "Asia/Shanghai", Guid.NewGuid().ToString(), media,
        ParticipantIds: people), default);
    private Task<Event> Edit(Event e, string title, string[]? people = null) => _events.UpdateSourceAsync(new(e.UserId, e.Id,
        e.RowVersion, title, "修改后正文", e.HappenedAt, e.PlannedAt, e.Timezone, e.MediaAssets.Select(x => x.MediaAssetId).ToArray(),
        ParticipantIds: people), default);
    private async Task<Guid> Image(long owner)
    {
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            ObjectKey = Guid.NewGuid().ToString(),
            OriginalFileName = "一起.png",
            DeclaredMimeType = "image/png",
            VerifiedMimeType = "image/png",
            Kind = MediaKind.Image,
            Status = MediaAssetStatus.Ready,
            ExpectedSize = 4,
            ActualSize = 4,
            ExpectedSha256 = new string('a', 64),
            ActualSha256 = new string('a', 64),
            CreatedAt = _clock.GetUtcNow(),
            UpdatedAt = _clock.GetUtcNow(),
            ConfirmedAt = _clock.GetUtcNow(),
            UploadExpiresAt = _clock.GetUtcNow().AddHours(1)
        };
        _db.MediaAssets.Add(asset); await _db.SaveChangesAsync(); return asset.Id;
    }
    private SocialAiTools Ai(long user, string question = "最近和谁一起的记录最多")
    {
        var current = new CurrentUserContext(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "test")) }
        });
        var tools = new SocialAiTools(_db, current, _friends, _shares, _clock);
        tools.Configure(AssistantCalendarContext.Create(_clock.GetUtcNow(), "Asia/Shanghai"), question, false);
        return tools;
    }

    [Fact]
    public async Task Mention_is_live_share_is_fixed_and_third_user_cannot_read_either_or_media()
    {
        var f = await Befriend(_a, B);
        var image = await Image(_a);
        var record = await Record(_a, "晚餐旧版", [B.ToString()], media: [image]);
        var conversation = await _chat.OpenAsync(_a, f, default);
        var message = await _chat.SendAsync(_a, conversation, new(Guid.NewGuid(), "record", EventId: record.Id), default);
        Assert.Equal("晚餐旧版", (await _shares.MentionAsync(B, record.Id, default)).Title);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _shares.MentionAsync(C, record.Id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _shares.GetAsync(C, message.ShareId!.Value, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _shares.OpenMediaAsync(C, message.ShareId, null, image, default));
        await using var stream = (await _shares.OpenMediaAsync(B, message.ShareId, null, image, default)).Stream;
        Assert.Equal(4, stream.Length);
        Assert.Null(await _events.GetAsync(B, record.Id, default)); // Never loosen the private endpoint.
        record = await Edit(record, "晚餐新版"); // Old client omission preserves participants.
        Assert.Equal("晚餐新版", (await _shares.MentionAsync(B, record.Id, default)).Title);
        Assert.Equal("晚餐旧版", (await _shares.GetAsync(B, message.ShareId!.Value, default)).Document.Title);
        Assert.Contains(B.ToString(), JsonSerializer.Deserialize<string[]>(record.SourceRevisions.Last().ParticipantIdsJson)!);
        await Edit(record, "取消关联", []);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _shares.MentionAsync(B, record.Id, default));
        Assert.True((await _shares.GetAsync(B, message.ShareId.Value, default)).Available);
        await _shares.RevokeAsync(_a, message.ShareId.Value, default);
        Assert.False((await _shares.GetAsync(B, message.ShareId.Value, default)).Available);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _shares.OpenMediaAsync(B, message.ShareId, null, image, default));
    }

    [Fact]
    public async Task Delete_and_readd_never_restore_old_grants_or_conversation_sending()
    {
        var f = await Befriend(_a, B);
        var e = await Record(_a, "一起散步", [B.ToString()]);
        var conversation = await _chat.OpenAsync(_a, f, default);
        var sent = await _chat.SendAsync(_a, conversation, new(Guid.NewGuid(), "record", EventId: e.Id), default);
        await _friends.RemoveAsync(B, f, true, default);
        Assert.Empty(await _friends.ListAsync(_a, null, default));
        await Assert.ThrowsAsync<DomainValidationException>(() => _friends.RequestAsync(_a, B.ToString(), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _chat.SendAsync(_a, conversation, new(Guid.NewGuid(), "text", "你好"), default));
        await _db.UserBlocks.Where(x => x.UserId == B && x.BlockedUserId == _a).ExecuteDeleteAsync();
        var again = await Befriend(_a, B);
        Assert.NotEqual(f, again);
        Assert.False((await _shares.GetAsync(B, sent.ShareId!.Value, default)).Available);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _shares.MentionAsync(B, e.Id, default));
        Assert.NotEqual(conversation, await _chat.OpenAsync(_a, again, default));
    }

    [Fact]
    public async Task Private_preferences_and_confirmed_relationships_have_distinct_visibility()
    {
        var f = await Befriend(_a, B);
        await _friends.PreferenceAsync(_a, f, new("私人的小王", "恋人"), default);
        var mine = Assert.Single(await _friends.ListAsync(_a, "私人的", default));
        var theirs = Assert.Single(await _friends.ListAsync(B, null, default));
        Assert.Equal("恋人", mine.Label); Assert.Equal("朋友", theirs.Label); Assert.Empty(theirs.Remark);
        Assert.Null(mine.Relationship);
        Assert.Empty(await _friends.ListAsync(C, null, default));
        await _friends.RelationshipAsync(_a, f, "propose", "恋人", mine.Version, default);
        var pending = Assert.Single(await _friends.ListAsync(B, null, default));
        Assert.Null(pending.Relationship);
        await Assert.ThrowsAsync<DomainValidationException>(() => _friends.RelationshipAsync(_a, f, "accept", null, pending.Version, default));
        await _friends.RelationshipAsync(B, f, "accept", null, pending.Version, default);
        var confirmed = Assert.Single(await _friends.ListAsync(_a, null, default));
        Assert.Equal("恋人", confirmed.Relationship);
        await _friends.RelationshipAsync(_a, f, "clear", null, confirmed.Version, default);
        Assert.Null((await Ai(_a).GetMyFriendRelationship(f))!.Relationship);
    }

    [Fact]
    public async Task Counts_use_accessible_completed_distinct_records_not_shares_meetings_or_plans()
    {
        var f = await Befriend(_a, B);
        await Befriend(_a, C);
        await Record(_a, "自己与小王", [B.ToString()]);
        await Record(B, "小王与我", [_a.ToString()]);
        await Record(B, "没有提到我");
        await Record(_a, "四十天前", [B.ToString()], happened: _clock.GetUtcNow().AddDays(-40));
        await Record(_a, "还未执行", [B.ToString()], EventKind.Plan);
        await Record(_a, "没有日期", [B.ToString()], undated: true);
        var unrelated = await Record(B, "分享不代表我参与");
        var c = await _chat.OpenAsync(B, f, default);
        await _chat.SendAsync(B, c, new(Guid.NewGuid(), "record", EventId: unrelated.Id), default);
        await _chat.SendAsync(B, c, new(Guid.NewGuid(), "record", EventId: unrelated.Id), default);
        var ai = Ai(_a);
        var result = await ai.AggregateMyFriendActivities();
        Assert.Equal(_clock.GetUtcNow().AddDays(-30), result.From);
        Assert.Equal("共同记录条数", result.Unit);
        Assert.Equal(B.ToString(), result.Items[0].Friend.Person.Id);
        Assert.Equal(2, result.Items[0].RecordCount);
        Assert.Equal(1, result.Items[0].PlannedCount);
        Assert.Equal(1, result.Items[0].UndatedCount);
        Assert.Equal(2, result.Items[0].Records.Count);
        Assert.Contains(result.Items[0].Records, x => x.AccessPath != null && x.AuthorId == B.ToString());
        Assert.Empty(await ai.SearchSharedContent());
        Assert.Equal(2, (await Ai(_a, "好友分享了什么").SearchSharedContent()).Count);
        var month = await Ai(_a, "本月共同记录").AggregateMyFriendActivities();
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 16, 0, 0, TimeSpan.Zero), month.From);
    }

    [Fact]
    public async Task Messages_are_idempotent_paged_and_clear_only_the_callers_history()
    {
        var f = await Befriend(_a, B);
        var c = await _chat.OpenAsync(_a, f, default);
        var first = new SendDirectMessageInput(Guid.NewGuid(), "text", "第一条");
        var m = await _chat.SendAsync(_a, c, first, default);
        Assert.Equal(m.Id, (await _chat.SendAsync(_a, c, first, default)).Id);
        await Assert.ThrowsAsync<DomainValidationException>(() => _chat.SendAsync(_a, c, first with { Text = "不同内容" }, default));
        for (var i = 0; i < 34; i++) await _chat.SendAsync(_a, c, new(Guid.NewGuid(), "text", $"第{i}条"), default);
        var recent = await _chat.MessagesAsync(B, c, 30, null, null, default);
        Assert.Equal(30, recent.Items.Count); Assert.NotNull(recent.NextCursor);
        var older = await _chat.MessagesAsync(B, c, 30, long.Parse(recent.NextCursor!), null, default);
        Assert.Equal(5, older.Items.Count);
        Assert.Equal(35, (await _chat.SummaryAsync(B, c, default)).UnreadCount);
        await _chat.ReadAsync(B, c, recent.Items[^1].Id, false, default);
        Assert.Equal(0, (await _chat.SummaryAsync(B, c, default)).UnreadCount);
        Assert.Equal(recent.Items[^1].Id, (await _chat.SummaryAsync(_a, c, default)).PeerReadThroughId);
        await _chat.ReadAsync(B, c, 0, true, default);
        Assert.Equal(recent.Items.Last().Id, (await _chat.SummaryAsync(B, c, default)).ClearedThroughId);
        Assert.Empty((await _chat.MessagesAsync(B, c, 30, null, null, default)).Items);
        Assert.NotEmpty((await _chat.MessagesAsync(_a, c, 30, null, null, default)).Items);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _chat.MessagesAsync(C, c, 30, null, null, default));
        var next = await _chat.SendAsync(_a, c, new(Guid.NewGuid(), "text", "清除后新消息"), default);
        Assert.Equal(next.Id, Assert.Single((await _chat.MessagesAsync(B, c, 30, null, m.Id, default)).Items).Id);
    }

    [Fact]
    public async Task Recipient_can_decline_a_mention_and_evidence_loses_revoked_references()
    {
        var f = await Befriend(_a, B);
        var e = await Record(_a, "一起喝茶", [B.ToString()]);
        var ai = Ai(B); await ai.AggregateMyFriendActivities();
        var evidence = ai.Merge(new EvidenceBundle([], [], null, ""));
        var json = JsonSerializer.Serialize(evidence);
        Assert.Single((await SocialEvidenceGuard.ReadAsync(_db, B, json, default))!.Records);
        await _participants.DetachAsync(B, e.Id, default);
        Assert.Empty((await SocialEvidenceGuard.ReadAsync(_db, B, json, default))!.Records);
        _db.ChangeTracker.Clear();
        e = (await _events.GetAsync(_a, e.Id, default))!;
        await Assert.ThrowsAsync<DomainValidationException>(() => Edit(e, "试图重新标记", [B.ToString()]));
        _db.ChangeTracker.Clear();
        await _friends.RemoveAsync(_a, f, false, default);
        Assert.Empty((await SocialEvidenceGuard.ReadAsync(_db, B, json, default))!.Friends!);
    }

    [Fact]
    public async Task Storyline_share_contains_only_pinned_nodes_and_denies_deleted_nodes_media()
    {
        var f = await Befriend(_a, B);
        var image = await Image(_a);
        var one = await Record(_a, "起点原版", media: [image]);
        var two = await Record(_a, "终点");
        var unrelated = await Record(_a, "作者其他私密内容");
        var stage = Guid.NewGuid(); var n1 = Guid.NewGuid(); var n2 = Guid.NewGuid();
        var story = await new StorylineService(_db, new AnalysisOutbox(_db), _clock).CreateAsync(_a,
            new SaveStorylineRequest("旅行", "故事说明", "trip", StorylineStatus.Ongoing, null, [], [new(stage, "出发", 0)],
                [new(n1, "existing-event", one.Id, 1, null, stage, 0), new(n2, "existing-event", two.Id, 1, null, stage, 1)],
                [new(Guid.NewGuid(), n1, n2, StorylineRelationType.Sequence, "接着")], null), Guid.NewGuid().ToString(), default);
        await Edit(one, "起点改版");
        var c = await _chat.OpenAsync(_a, f, default);
        var message = await _chat.SendAsync(_a, c, new(Guid.NewGuid(), "storyline", StorylineId: story.Storyline.Id), default);
        var shared = await _shares.GetAsync(B, message.ShareId!.Value, default);
        Assert.Equal(2, shared.Document.Records.Count);
        Assert.Contains(shared.Document.Records, x => x.Title == "起点原版");
        Assert.DoesNotContain(shared.Document.Records, x => x.EventId == unrelated.Id);
        Assert.Single(shared.Document.Stages); Assert.Single(shared.Document.Edges);
        await _events.SoftDeleteAsync(_a, one.Id, one.RowVersion, default);
        Assert.False((await _shares.GetAsync(B, message.ShareId.Value, default)).Document.Records.First(x => x.EventId == one.Id).Available);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _shares.OpenMediaAsync(B, message.ShareId, null, image, default));
    }

    [Fact]
    public async Task Concurrent_revocation_rolls_back_stale_message_and_notifications()
    {
        var f = await Befriend(_a, B); var c = await _chat.OpenAsync(_a, f, default);
        await using var second = new TraceDbContext(_fixture.Options);
        var other = new FriendService(second, _people, new AnalysisOutbox(second), _clock);
        await other.RemoveAsync(B, f, false, default);
        // RequireAsync uses a fresh SQL predicate despite an old entity being tracked.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _chat.SendAsync(_a, c, new(Guid.NewGuid(), "text", "过期发送"), default));
        Assert.Empty(await _db.DirectMessages.Where(x => x.ConversationId == c).ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Legacy_records_without_participant_metadata_can_be_previewed_and_shared(string legacyParticipants)
    {
        var friendship = await Befriend(_a, B);
        var record = await Record(_a, "旧版记录");
        var source = await _db.SourceRevisions.SingleAsync(x => x.EventId == record.Id && x.Revision == 1);
        // AddSocialExperiences populated pre-existing revisions with an empty string.
        source.ParticipantIdsJson = legacyParticipants;
        await _db.SaveChangesAsync();
        var preview = await _shares.BuildAsync(_a, "record", record.Id, null, default);
        Assert.Empty(Assert.Single(preview.Records).Participants);

        var stage = Guid.NewGuid(); var node = Guid.NewGuid();
        var story = await new StorylineService(_db, new AnalysisOutbox(_db), _clock).CreateAsync(_a,
            new SaveStorylineRequest("旧记录故事线", null, "trip", StorylineStatus.Ongoing, null, [],
                [new(stage, "起点", 0)], [new(node, "existing-event", record.Id, 1, null, stage, 0)], [], null),
            Guid.NewGuid().ToString(), default);
        await Edit(record, "新版记录", [B.ToString()]);
        var storylinePreview = await _shares.BuildAsync(_a, "storyline", null, story.Storyline.Id, default);
        Assert.Equal("旧版记录", Assert.Single(storylinePreview.Records).Title);
        Assert.Empty(storylinePreview.Records[0].Participants);
        Assert.Equal([B.ToString()], (await _shares.BuildAsync(_a, "record", record.Id, null, default)).Records[0].Participants);

        var conversation = await _chat.OpenAsync(_a, friendship, default);
        var message = await _chat.SendAsync(_a, conversation,
            new(Guid.NewGuid(), "storyline", StorylineId: story.Storyline.Id), default);
        var shared = await _shares.GetAsync(B, message.ShareId!.Value, default);
        Assert.Equal("旧版记录", Assert.Single(shared.Document.Records).Title);
        Assert.Empty(shared.Document.Records[0].Participants);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _shares.GetAsync(C, message.ShareId.Value, default));
    }

    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 9, 22, 4, 0, 0, TimeSpan.Zero); }
    [Fact]
    public async Task Http_boundaries_reject_third_user_and_native_sse_resumes_after_cursor()
    {
        var f = await Befriend(_a, B); var image = await Image(_a);
        var e = await Record(_a, "授权正文", [B.ToString()], media: [image]);
        var c = await _chat.OpenAsync(_a, f, default);
        var share = await _chat.SendAsync(_a, c, new(Guid.NewGuid(), "record", EventId: e.Id), default);
        var latest = await _db.SocialNotifications.Where(n => n.UserId == B).OrderByDescending(n => n.Id).FirstAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(SocialExperienceTests).Assembly.GetName().Name
        });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(FriendsController).Assembly);
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, SocialTestAuthentication>("test", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<PassingTrace.Events.Api.Common.DomainExceptionHandler>();
        builder.Services.AddScoped(_ => new TraceDbContext(_fixture.Options));
        builder.Services.AddSingleton<ISocialIdentityClient>(_people); builder.Services.AddSingleton<IObjectStorage>(_storage);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddScoped<IAnalysisOutbox, AnalysisOutbox>();
        builder.Services.AddScoped<FriendService>(); builder.Services.AddScoped<SharedContentService>();
        builder.Services.AddScoped<EventParticipationService>(); builder.Services.AddScoped<DirectChatService>();
        await using var app = builder.Build();
        app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/friends")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", C.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/friends/shared-records/{e.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/shares/{share.ShareId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/shares/{share.ShareId}/media/{image}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/conversations/{c}/messages")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User"); client.DefaultRequestHeaders.Add("X-Test-User", B.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/friends/shared-records/{e.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/shares/{share.ShareId}/media/{image}")).StatusCode);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using (var response = await client.GetAsync($"/api/v1/notifications/stream?after={latest.Id - 1}", HttpCompletionOption.ResponseHeadersRead, timeout.Token))
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
            var lines = new List<string>();
            while (true) { var line = await reader.ReadLineAsync(timeout.Token); if (string.IsNullOrEmpty(line)) break; lines.Add(line); }
            Assert.Contains(lines, x => x == $"id: {latest.Id}");
            Assert.Contains(lines, x => x.Contains("notification"));
        }
        using (var response = await client.GetAsync($"/api/v1/notifications/stream?after={latest.Id}", HttpCompletionOption.ResponseHeadersRead, timeout.Token))
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
            var lines = new List<string>();
            while (true) { var line = await reader.ReadLineAsync(timeout.Token); if (string.IsNullOrEmpty(line)) break; lines.Add(line); }
            Assert.DoesNotContain(lines, x => x.StartsWith("id:"));
            Assert.Contains(lines, x => x.Contains("heartbeat"));
        }
        // Exercise MVC body binding, not only direct service calls: record validation
        // attributes belong on constructor parameters, not generated properties.
        client.DefaultRequestHeaders.Remove("X-Test-User"); client.DefaultRequestHeaders.Add("X-Test-User", _a.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/shares/preview",
            new SendDirectMessageInput(Guid.NewGuid(), "record", EventId: e.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/conversations/{c}/messages",
            new SendDirectMessageInput(Guid.NewGuid(), "text", "实际 HTTP 发送测试"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/conversations/{c}/messages",
            new SendDirectMessageInput(Guid.NewGuid(), "text", new string('x', 8001)))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/friends/{f}/preference",
            new PreferenceInput("好友备注", "同学"))).StatusCode);
        var httpFriends = (await client.GetFromJsonAsync<FriendView[]>("/api/v1/friends"))!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/friends/{f}/relationship",
            new RelationshipInput("亲密朋友", httpFriends.Single(x => x.Id == f).Version))).StatusCode);
        var requestResponse = await client.PostAsJsonAsync("/api/v1/friend-requests", new FriendRequestInput(C.ToString()));
        Assert.Equal(HttpStatusCode.OK, requestResponse.StatusCode);
        var requestId = (await requestResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/friend-requests/{requestId}/decision",
            new DecisionInput("withdraw"))).StatusCode);
        await app.StopAsync();
    }

    private sealed class SocialTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].ToString();
            if (!long.TryParse(user, out _)) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user)], Scheme.Name)), Scheme.Name)));
        }
    }
    [Fact]
    public async Task Card_status_checks_revalidate_old_shares_and_are_scoped_to_conversation()
    {
        var f = await Befriend(_a, B); var c = await _chat.OpenAsync(_a, f, default);
        var e = await Record(_a, "共享卡片");
        var m = await _chat.SendAsync(_a, c, new(Guid.NewGuid(), "record", EventId: e.Id), default);
        Assert.True(Assert.Single(await _chat.ShareStatusesAsync(B, c, [m.ShareId!.Value], default)).Available);
        await _shares.RevokeAsync(_a, m.ShareId.Value, default);
        var status = Assert.Single(await _chat.ShareStatusesAsync(B, c, [m.ShareId.Value], default));
        Assert.False(status.Available); Assert.Equal("内容已不可查看", status.Title);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _chat.ShareStatusesAsync(C, c, [m.ShareId.Value], default));
        Assert.Empty(await _chat.ShareStatusesAsync(B, c, [Guid.NewGuid()], default));
    }

    [Fact]
    public async Task Social_demo_is_development_only_and_does_not_reset_edits_or_revoked_grants()
    {
        _people.DevelopmentIds = [B, C, _a + 3];
        var env = new DemoEnvironment { EnvironmentName = Environments.Production };
        var seeder = new SocialDemoSeeder(_db, _people, _friends, _events,
            new StorylineService(_db, new AnalysisOutbox(_db), _clock), _chat, _clock, env,
            Options.Create(new DevelopmentDemoOptions { Enabled = true }));
        await seeder.SeedAsync(_a, default);
        Assert.Empty(await _friends.ListAsync(_a, null, default));
        env.EnvironmentName = Environments.Development;
        await seeder.SeedAsync(_a, default);
        Assert.Equal(2, (await _friends.ListAsync(_a, null, default)).Count);
        Assert.Single((await _shares.JointRecordsAsync(_a, 20, null, default)).Items);
        var summary = Assert.Single((await _chat.ListAsync(_a, 20, null, default)).Items);
        Assert.Equal(3, (await _chat.MessagesAsync(_a, summary.Id, 30, null, null, default)).Items.Count);
        var initialCount = await _db.Events.CountAsync(e => e.UserId == _a);
        var own = await _db.Events.FirstAsync(e => e.UserId == _a && e.EventKind == EventKind.Trace);
        await Edit(own, "我自己修改了演示记录");
        await _friends.RemoveAsync(_a, summary.FriendshipId, false, default);
        await seeder.SeedAsync(_a, default);
        Assert.Equal(initialCount, await _db.Events.CountAsync(e => e.UserId == _a));
        Assert.Equal("我自己修改了演示记录", (await _events.GetAsync(_a, own.Id, default))!.Title);
        Assert.Single(await _friends.ListAsync(_a, null, default));
        Assert.Empty((await _shares.JointRecordsAsync(_a + 3, 20, null, default)).Items);
    }

    [Fact]
    public async Task Friends_package_uses_real_mcp_validation_and_keeps_same_name_candidates()
    {
        await Befriend(_a, B); await Befriend(_a, C);
        await Record(_a, "一起散步", [B.ToString()]);
        var state = Ai(_a);
        await using var session = await InternalMcpToolSession.CreateAsync(new FriendsCapabilityPackage(state).CreateTools().OfType<AIFunction>());
        Assert.Equal(4, session.Tools.Count);
        var search = session.Tools.OfType<AIFunction>().Single(x => x.Name == "SearchMyFriends");
        Assert.False(search.JsonSchema.GetProperty("properties").TryGetProperty("userId", out _));
        var found = Assert.IsType<JsonElement>(await search.InvokeAsync(new AIFunctionArguments { ["query"] = "小王" }));
        Assert.Equal(2, found.GetProperty("structuredContent").GetArrayLength());
        var aggregate = session.Tools.OfType<AIFunction>().Single(x => x.Name == "AggregateMyFriendActivities");
        var result = Assert.IsType<JsonElement>(await aggregate.InvokeAsync(new AIFunctionArguments { ["limit"] = 10 }));
        Assert.Equal("共同记录条数", result.GetProperty("structuredContent").GetProperty("unit").GetString());
        Assert.Equal(1, result.GetProperty("structuredContent").GetProperty("items")[0].GetProperty("recordCount").GetInt64());
        var invalid = Assert.IsType<JsonElement>(await aggregate.InvokeAsync(new AIFunctionArguments { ["limit"] = 999 }));
        Assert.True(invalid.GetProperty("isError").GetBoolean());
        Assert.Equal(2, state.Merge(new EvidenceBundle([], [], null, "")).Friends!.Count);
    }

    [Fact]
    public async Task Follow_up_keeps_rank_order_after_summary_and_revalidates_revoked_context()
    {
        await Befriend(_a, B); var second = await Befriend(_a, C);
        await Record(_a, "和小王散步", [B.ToString()]);
        var ai = Ai(_a); await ai.AggregateMyFriendActivities();
        var now = _clock.GetUtcNow();
        var conversation = new AiConversation { Id = Guid.NewGuid(), UserId = _a, Title = "朋友", CreatedAt = now, UpdatedAt = now };
        var message = new AiMessage
        {
            Conversation = conversation,
            UserId = _a,
            Role = AiMessageRole.Assistant,
            Content = "第一位散步好友，第二位另一位小王。",
            CreatedAt = now,
            ExpiresAt = now.AddDays(1),
            DataWatermark = await _db.UserDataWatermarks.Where(x => x.UserId == _a).Select(x => x.Version).SingleAsync(),
            EvidenceSnapshotJson = JsonSerializer.Serialize(ai.Merge(new EvidenceBundle([], [], null, "")))
        };
        _db.AiMessages.Add(message); await _db.SaveChangesAsync();
        _db.ConversationSummaries.Add(new ConversationSummary
        {
            Conversation = conversation,
            UserId = _a,
            Content = "旧的好友摘要",
            ThroughMessageId = message.Id,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();
        var context = await ConversationContextSnapshot.LoadAsync(_db, _a, conversation.Id, long.MaxValue, now, default);
        Assert.Empty(context.Summary);
        Assert.Equal(message.Content, Assert.Single(context.RecentMessages).Content);
        Assert.Equal(second, context.RecentFriends![1].Id);
        Assert.Equal("我和第二位上次去哪了？", context.BuildPromptMessages("我和第二位上次去哪了？").Last().Text);
        await _friends.RemoveAsync(_a, second, false, default);
        context = await ConversationContextSnapshot.LoadAsync(_db, _a, conversation.Id, long.MaxValue, now, default);
        Assert.DoesNotContain(context.RecentFriends!, x => x.Id == second);
        Assert.Contains("已发生变化", Assert.Single(context.RecentMessages).Content);
    }

    private sealed class DemoEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "SocialTests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class FakePeople : ISocialIdentityClient
    {
        public long[] DevelopmentIds { get; set; } = [];
        public async Task<IReadOnlyList<PersonProfile>> DevelopmentPeopleAsync(CancellationToken ct) => (await ProfilesAsync(DevelopmentIds, ct)).Values.ToArray();
        public Task<PersonProfile?> ResolveAsync(string code, CancellationToken ct) => Task.FromResult<PersonProfile?>(new(code, "小王", "公开简介", false, code));
        public Task<IReadOnlyDictionary<long, PersonProfile>> ProfilesAsync(IEnumerable<long> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<long, PersonProfile>>(ids.Distinct().ToDictionary(id => id, id => new PersonProfile(id.ToString(), "小王", "公开简介", false, id.ToString())));
    }
    private sealed class FakeStorage : IObjectStorage
    {
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3, 4]));
        public Task EnsureBucketAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<string> CreateMultipartUploadAsync(string a, string b, CancellationToken ct) => throw new NotSupportedException();
        public Task<Uri> CreateUploadUrlAsync(string a, string b, DateTimeOffset c, CancellationToken ct) => throw new NotSupportedException();
        public Task<Uri> CreatePartUploadUrlAsync(string a, string b, int c, DateTimeOffset d, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> UploadPartAsync(string a, string b, int c, Stream d, long e, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteMultipartUploadAsync(string a, string b, IReadOnlyList<CompletedPart> c, CancellationToken ct) => throw new NotSupportedException();
        public Task AbortMultipartUploadAsync(string a, string b, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredObjectInfo> GetInfoAsync(string a, CancellationToken ct) => throw new NotSupportedException();
        public Task PutAsync(string a, Stream b, string c, long d, CancellationToken ct) => throw new NotSupportedException();
        public Task<Uri> CreateDownloadUrlAsync(string a, string b, string c, bool d, DateTimeOffset e, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string a, CancellationToken ct) => Task.CompletedTask;
    }
}
