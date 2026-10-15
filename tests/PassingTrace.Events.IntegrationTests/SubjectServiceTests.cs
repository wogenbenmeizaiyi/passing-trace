using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Events;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Media;
using PassingTrace.Core.Subjects;
using PassingTrace.Events.Api.Ai.Mutations;
using PassingTrace.Events.Api.Ai.Assistant;
using PassingTrace.Events.Api.Ai.Tools.Mutations;
using PassingTrace.Events.Api.Ai.Assistant.Context;
using PassingTrace.Events.Api.Ai.Evidence;
using PassingTrace.Events.Api.Common;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Media;
using PassingTrace.Events.Api.Storylines;
using PassingTrace.Events.Api.Subjects;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence;
using PassingTrace.Infrastructure.Persistence.Ai;
using PassingTrace.Infrastructure.Persistence.Subjects;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class SubjectServiceTests(StorylinePostgresFixture fixture) : IClassFixture<StorylinePostgresFixture>
{
    private static long _nextUser = 800000;
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
    private static Dictionary<string, JsonElement> Value(Guid field, object value) => new() { [field.ToString()] = JsonSerializer.SerializeToElement(value) };
    private TraceDbContext Db() => new(fixture.Options);
    private static SubjectService Service(TraceDbContext db) => new(new SubjectRepository(db), new EmptyMedia(), new AnalysisOutbox(db), new Clock());
    private static EventService Events(TraceDbContext db, SubjectService service) => new(new EventRepository(db), new Clock(), new EmptyMedia(), new AnalysisOutbox(db), subjects: service);
    private static long User() => Interlocked.Increment(ref _nextUser);
    private static Task<SubjectResponse> Add(SubjectService service, long user, Guid parent, string name = "猫", SubjectKind kind = SubjectKind.Pet) =>
        service.CreateAsync(user, new(kind, name, [new(parent, "饲养")], Timezone: "Asia/Shanghai"), null, default);
    private static Task<SubjectTimelineResponse> Timeline(SubjectService service, long user, Guid id) => service.TimelineAsync(user, id, "month", null, null, null, null, null, 50, null, default);

    [Fact]
    public async Task Self_is_unique_even_with_concurrent_initialization_and_cannot_end_delete_or_rename()
    {
        var user = User();
        var ids = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ => { await using var db = Db(); return (await Service(db).EnsureSelfAsync(user, default)).Id; }));
        Assert.Single(ids.Distinct());
        await using var check = Db(); var service = Service(check); var self = await service.GetAsync(user, ids[0], default);
        await Assert.ThrowsAsync<DomainValidationException>(() => service.DeleteAsync(user, self.Id, self.Version, default));
        await Assert.ThrowsAsync<DomainValidationException>(() => service.UpdateAsync(user, self.Id, self.Version, new(Name: "假自己"), default));
        await Assert.ThrowsAsync<DomainValidationException>(() => service.LifecycleAsync(user, self.Id, self.Version, new("end", Now, "deceased"), default));
    }

    [Fact]
    public async Task Two_sources_are_isolated_and_explicit_markers_share_live_source_without_social_participants()
    {
        var user = User(); await using var db = Db(); var service = Service(db); var events = Events(db, service);
        var self = await service.EnsureSelfAsync(user, default); var friend = await Add(service, user, self.Id, "朋友", SubjectKind.Person); var cat = await Add(service, user, friend.Id);
        var entry = await service.CreateEntryAsync(user, friend.Id, new(SubjectEntryKind.Record, "朋友带猫接生", HappenedAt: Now, MarkedSubjectIds: [cat.Id]), "birth", default);
        var plan = await service.CreateEntryAsync(user, cat.Id, new(SubjectEntryKind.Plan, "下月疫苗", PlannedAt: Now.AddMonths(1)), "vaccine", default);
        Assert.Empty(await db.Events.Where(x => x.UserId == user).ToArrayAsync());
        Assert.DoesNotContain((await Timeline(service, user, self.Id)).Groups.SelectMany(x => x.Items), x => x.SourceId == entry.Id.ToString() || x.SourceId == plan.Id.ToString());
        foreach (var id in new[] { friend.Id, cat.Id }) Assert.Single((await Timeline(service, user, id)).Groups.SelectMany(x => x.Items), x => x.SourceId == entry.Id.ToString());
        var original = await events.CreateAsync(new(user, EventKind.Trace, "我去修车", null, Now, null, "Asia/Shanghai", "repair", SubjectIds: [cat.Id]), default);
        Assert.Empty(original.Participants);
        Assert.Contains((await Timeline(service, user, cat.Id)).Groups.SelectMany(x => x.Items), x => x.SourceType == "Event" && x.SourceId == original.Id.ToString());
        await events.UpdateSourceAsync(new(user, original.Id, original.RowVersion, "我和猫去检查", null, Now, null, "Asia/Shanghai"), default);
        Assert.Contains((await Timeline(service, user, cat.Id)).Groups.SelectMany(x => x.Items), x => x.Title == "我和猫去检查");
        var updated = await service.UpdateEntryAsync(user, entry.Id, entry.Version, new(Title: "朋友带猫完成接生", MarkedSubjectIds: []), default);
        Assert.DoesNotContain((await Timeline(service, user, cat.Id)).Groups.SelectMany(x => x.Items), x => x.SourceId == entry.Id.ToString());
        Assert.Equal("朋友带猫完成接生", (await service.GetEntryAsync(user, updated.Id, default)).Title);
        Assert.Equal(1, await db.Events.CountAsync(x => x.UserId == user));
    }

    [Fact]
    public async Task References_to_deleted_content_remain_invalid_and_subject_delete_preserves_entries()
    {
        var user = User(); await using var db = Db(); var service = Service(db); var self = await service.EnsureSelfAsync(user, default);
        var cat = await Add(service, user, self.Id); var friend = await Add(service, user, self.Id, "朋友");
        var entry = await service.CreateEntryAsync(user, cat.Id, new(Title: "看病", MarkedSubjectIds: [friend.Id]), null, default);
        await service.DeleteEntryAsync(user, entry.Id, entry.Version, default);
        Assert.Contains((await Timeline(service, user, friend.Id)).Groups.SelectMany(x => x.Items), x => x.SourceId == entry.Id.ToString() && x.Invalid && x.Content == null);
        var plan = await service.CreateEntryAsync(user, cat.Id, new(SubjectEntryKind.Plan, "保养"), null, default);
        var current = await service.GetAsync(user, cat.Id, default); await service.DeleteAsync(user, cat.Id, current.Version, default);
        Assert.Null((await db.SubjectEntries.SingleAsync(x => x.Id == plan.Id)).DeletedAt);
        Assert.True((await service.GetEntryAsync(user, plan.Id, default)).SourceSubjectDeleted);
    }

    [Fact]
    public async Task Graph_allows_long_chains_cycles_and_historical_links_but_rejects_bridge_deletion_without_staged_changes()
    {
        var user = User(); await using var db = Db(); var service = Service(db); var self = await service.EnsureSelfAsync(user, default);
        var friend = await Add(service, user, self.Id, "朋友"); var mother = await Add(service, user, friend.Id, "母猫"); var kitten = await Add(service, user, mother.Id, "幼猫");
        var graph = await service.GraphAsync(user, default); var bridge = graph.Relations.Single(x => x.FromSubjectId == friend.Id && x.ToSubjectId == self.Id);
        await service.UpdateRelationAsync(user, bridge.Id, bridge.Revision, new(EndedAt: Now), default);
        friend = await service.GetAsync(user, friend.Id, default);
        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => service.DeleteAsync(user, friend.Id, friend.Version, default)); Assert.Contains("母猫", exception.Message);
        Assert.Null((await db.Subjects.SingleAsync(x => x.Id == friend.Id)).DeletedAt);
        var currentBridge = await service.GetRelationAsync(user, bridge.Id, default);
        await Assert.ThrowsAsync<DomainValidationException>(() => service.DeleteRelationAsync(user, bridge.Id, currentBridge.Revision, default));
        Assert.Null((await db.SubjectRelations.SingleAsync(x => x.Id == bridge.Id)).RemovedAt);
        kitten = await service.GetAsync(user, kitten.Id, default); await service.AddRelationAsync(user, kitten.Id, kitten.Version, new(self.Id, "饲养"), default);
        friend = await service.GetAsync(user, friend.Id, default); await service.DeleteAsync(user, friend.Id, friend.Version, default);
        Assert.Equal(3, (await service.GraphAsync(user, default)).Nodes.Count);
    }

    [Fact]
    public async Task Field_history_backfill_edit_delete_and_plan_actual_values_recompute_current_state()
    {
        var user = User(); await using var db = Db(); var service = Service(db); var self = await service.EnsureSelfAsync(user, default); var cat = await Add(service, user, self.Id);
        var weight = cat.Fields.Single(x => x.Key == "weight");
        var newer = await service.CreateEntryAsync(user, cat.Id, new(Title: "现在体重", HappenedAt: Now, FieldChanges: Value(weight.Id, 4)), null, default);
        await service.CreateEntryAsync(user, cat.Id, new(Title: "补录去年", HappenedAt: Now.AddYears(-1), FieldChanges: Value(weight.Id, 2)), null, default);
        Assert.Equal(4, (await service.GetAsync(user, cat.Id, default)).Values[weight.Id.ToString()].GetInt32());
        var plan = await service.CreateEntryAsync(user, cat.Id, new(SubjectEntryKind.Plan, "期望增重", PlannedAt: Now.AddMonths(1), FieldChanges: Value(weight.Id, 5)), null, default);
        Assert.Equal(4, (await service.GetAsync(user, cat.Id, default)).Values[weight.Id.ToString()].GetInt32());
        await Assert.ThrowsAsync<DomainValidationException>(() => service.DecideEntryAsync(user, plan.Id, plan.Version, new("complete", Now), default));
        plan = await service.DecideEntryAsync(user, plan.Id, plan.Version, new("complete", Now.AddHours(1), Value(weight.Id, 6)), default);
        Assert.Equal(6, (await service.GetAsync(user, cat.Id, default)).Values[weight.Id.ToString()].GetInt32());
        await service.DeleteEntryAsync(user, plan.Id, plan.Version, default);
        Assert.Equal(4, (await service.GetAsync(user, cat.Id, default)).Values[weight.Id.ToString()].GetInt32());
        await service.DeleteEntryAsync(user, newer.Id, newer.Version, default);
        cat = await service.GetAsync(user, cat.Id, default); Assert.Equal(2, cat.Values[weight.Id.ToString()].GetInt32());
        await Assert.ThrowsAsync<DomainValidationException>(() => service.UpdateAsync(user, cat.Id, cat.Version, new(Fields: cat.Fields.Select(x => x.Id == weight.Id ? x with { Unit = "g" } : x).ToArray()), default));
    }

    [Fact]
    public async Task Lifecycle_keeps_plans_by_default_cancels_only_owned_selected_plans_and_death_requires_correction()
    {
        var user = User(); await using var db = Db(); var service = Service(db); var self = await service.EnsureSelfAsync(user, default); var cat = await Add(service, user, self.Id); var friend = await Add(service, user, self.Id, "朋友");
        var owned = await service.CreateEntryAsync(user, cat.Id, new(SubjectEntryKind.Plan, "猫的疫苗", MarkedSubjectIds: [friend.Id]), null, default);
        var referenced = await service.CreateEntryAsync(user, friend.Id, new(SubjectEntryKind.Plan, "朋友来探望", MarkedSubjectIds: [cat.Id]), null, default);
        Assert.Single((await service.PreviewLifecycleAsync(user, cat.Id, default)).Plans);
        cat = await service.GetAsync(user, cat.Id, default); cat = await service.LifecycleAsync(user, cat.Id, cat.Version, new("end", Now, "relationship-ended"), default);
        Assert.Equal(SubjectEntryState.Planned, (await service.GetEntryAsync(user, owned.Id, default)).State);
        cat = await service.LifecycleAsync(user, cat.Id, cat.Version, new("resume", Now.AddSeconds(1)), default);
        cat = await service.LifecycleAsync(user, cat.Id, cat.Version, new("end", Now.AddSeconds(2), "deceased", CancelPlans: [new(owned.Id, owned.Version)]), default);
        Assert.Equal(SubjectEntryState.Cancelled, (await service.GetEntryAsync(user, owned.Id, default)).State);
        Assert.Equal(SubjectEntryState.Planned, (await service.GetEntryAsync(user, referenced.Id, default)).State);
        await Assert.ThrowsAsync<DomainValidationException>(() => service.LifecycleAsync(user, cat.Id, cat.Version, new("resume", Now.AddSeconds(3)), default));
        var end = await db.SubjectMilestones.SingleAsync(x => x.SubjectId == cat.Id && x.Reason == "deceased");
        cat = await service.LifecycleAsync(user, cat.Id, cat.Version, new("correct", Now.AddSeconds(3), CorrectsId: end.Id, UndoEnd: true), default);
        Assert.Equal(SubjectState.Active, cat.State); Assert.Equal(SubjectEntryState.Cancelled, (await service.GetEntryAsync(user, owned.Id, default)).State);
    }

    [Fact]
    public async Task Ownership_versions_idempotency_and_failed_creation_are_enforced()
    {
        var user = User(); await using var db = Db(); var service = Service(db); var self = await service.EnsureSelfAsync(user, default);
        var request = new CreateSubjectRequest(SubjectKind.Item, "汽车", [new(self.Id, "拥有")], ItemType: "vehicle");
        var car = await service.CreateAsync(user, request, "car", default); var again = await service.CreateAsync(user, request, "car", default); Assert.Equal(car.Id, again.Id);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.CreateAsync(user, request with { Name = "房屋" }, "car", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(user + 100, car.Id, default));
        await Assert.ThrowsAsync<ConcurrencyException>(() => service.UpdateAsync(user, car.Id, 999, new(Name: "错版本"), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(user, new(SubjectKind.Pet, "不能孤立", [new(Guid.NewGuid())]), null, default));
        Assert.Equal(2, await db.Subjects.CountAsync(x => x.UserId == user));
        Assert.Empty(await db.Events.Where(x => x.UserId == user).ToArrayAsync());
    }

    [Theory]
    [InlineData("cancel", "Cancelled")]
    [InlineData("confirm", "Succeeded")]
    [InlineData("edit", "Conflict")]
    public async Task Subject_deletion_needs_button_and_returns_repeatable_durable_result(string decision, string state)
    {
        var user = User(); await using var db = Db(); var service = Service(db); var self = await service.EnsureSelfAsync(user, default); var cat = await Add(service, user, self.Id);
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "test")) } };
        var events = Events(db, service); var mutations = new AiMutationService(new AiMutationRepository(db), new EventRepository(db), events,
            new StorylineService(db, new AnalysisOutbox(db), new Clock()), new AiConversationRepository(db), new CurrentUserContext(accessor), new Clock(), service);
        await Assert.ThrowsAsync<DomainValidationException>(() => mutations.RequestManualDeleteAsync("Subject", self.Id, Guid.NewGuid(), default));
        var request = await mutations.RequestManualDeleteAsync("Subject", cat.Id, Guid.NewGuid(), default);
        Assert.Null((await db.Subjects.SingleAsync(x => x.Id == cat.Id)).DeletedAt);
        if (decision == "edit") await service.UpdateAsync(user, cat.Id, cat.Version, new(Name: "更新的猫"), default);
        var result = await mutations.DecideAsync(request.ConversationId, request.Id, decision == "edit" ? "confirm" : decision, default);
        Assert.Equal(state, result.State); var repeated = await mutations.DecideAsync(request.ConversationId, request.Id, "confirm", default); Assert.Equal(result.Result.Message.Id, repeated.Result.Message.Id);
        Assert.Equal(state == "Succeeded", (await db.Subjects.SingleAsync(x => x.Id == cat.Id)).DeletedAt != null);
    }

    [Fact]
    public async Task Concurrent_edge_removals_and_endpoint_replacement_cannot_disconnect_the_graph()
    {
        var user = User(); Guid selfId, friendId, catId; Guid first, second;
        await using (var db = Db())
        {
            var service = Service(db); selfId = (await service.EnsureSelfAsync(user, default)).Id;
            var friend = await Add(service, user, selfId, "朋友"); friendId = friend.Id;
            var cat = await Add(service, user, friendId); catId = cat.Id;
            await service.AddRelationAsync(user, catId, cat.Version, new(selfId, "饲养"), default);
            var graph = await service.GraphAsync(user, default);
            first = graph.Relations.Single(x => x.FromSubjectId == friendId && x.ToSubjectId == selfId).Id;
            second = graph.Relations.Single(x => x.FromSubjectId == catId && x.ToSubjectId == selfId).Id;
        }
        var results = await Task.WhenAll(new[] { first, second }.Select(async id =>
        {
            await using var db = Db();
            try { return await Service(db).DeleteRelationAsync(user, id, 1, default); }
            catch (DomainValidationException) { return false; }
        }));
        Assert.Single(results, x => x);
        await using var check = Db(); var serviceCheck = Service(check); var remaining = await serviceCheck.GraphAsync(user, default);
        Assert.Equal(3, remaining.Nodes.Count); Assert.Equal(2, remaining.Relations.Count);
        var rootEdge = remaining.Relations.Single(x => x.FromSubjectId == selfId || x.ToSubjectId == selfId);
        var newTarget = rootEdge.FromSubjectId == catId ? friendId : catId;
        await Assert.ThrowsAsync<DomainValidationException>(() => serviceCheck.UpdateRelationAsync(user, rootEdge.Id, rootEdge.Revision, new(ToSubjectId: newTarget), default));
        Assert.Equal(selfId, (await serviceCheck.GetRelationAsync(user, rootEdge.Id, default)).ToSubjectId);
    }

    [Fact]
    public async Task Timeline_is_deduplicated_grouped_in_user_timezone_and_cursor_filtered()
    {
        var user = User(); await using var db = Db(); var service = Service(db); var self = await service.EnsureSelfAsync(user, default);
        var cat = await Add(service, user, self.Id);
        var midnight = new DateTimeOffset(2026, 9, 1, 16, 30, 0, TimeSpan.Zero);
        var record = await service.CreateEntryAsync(user, cat.Id, new(Title: "跨日", HappenedAt: midnight, MarkedSubjectIds: [self.Id]), null, default);
        var plan = await service.CreateEntryAsync(user, cat.Id, new(SubjectEntryKind.Plan, "月底保养", PlannedAt: midnight.AddDays(1)), null, default);
        var first = await service.TimelineAsync(user, cat.Id, "day", "Asia/Shanghai", midnight, midnight.AddDays(2), null, null, 1, null, default);
        Assert.Equal("2026-09-03", Assert.Single(first.Groups).Key); Assert.Equal(plan.Id.ToString(), Assert.Single(first.Groups[0].Items).SourceId);
        var second = await service.TimelineAsync(user, cat.Id, "day", "Asia/Shanghai", midnight, midnight.AddDays(2), null, null, 1, first.NextCursor, default);
        Assert.Equal("2026-09-02", Assert.Single(second.Groups).Key); Assert.Equal(record.Id.ToString(), Assert.Single(second.Groups[0].Items).SourceId);
        Assert.Null(second.NextCursor);
        var onlyPlans = await service.TimelineAsync(user, cat.Id, "month", null, null, null, "Plan", "Planned", 50, null, default);
        Assert.Single(onlyPlans.Groups.SelectMany(x => x.Items));
    }

    private static (AiMutationService Mutations, CurrentUserContext User) Mutations(TraceDbContext db, long userId, SubjectService service)
    {
        var accessor = new FixedUserAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")) } };
        var user = new CurrentUserContext(accessor);
        return (new AiMutationService(new AiMutationRepository(db), new EventRepository(db), Events(db, service),
            new StorylineService(db, new AnalysisOutbox(db), new Clock()), new AiConversationRepository(db), user, new Clock(), service), user);
    }

    [Fact]
    public async Task Approval_http_decisions_bind_confirm_cancel_and_reject_invalid_or_retargeted_requests()
    {
        var user = User();
        await using var db = Db();
        var service = Service(db);
        var self = await service.EnsureSelfAsync(user, default);
        var cat = await Add(service, user, self.Id);
        await service.AddRelationAsync(user, cat.Id, cat.Version, new(self.Id, "陪伴"), default);
        var graph = await service.GraphAsync(user, default);
        var removable = graph.Relations.Single(x => x.Label == "陪伴");
        var bridge = graph.Relations.Single(x => x.Label == "饲养");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(SubjectServiceTests).Assembly.GetName().Name
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(AssistantController).Assembly).AddControllersAsServices();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, ApprovalTestAuthentication>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<DomainExceptionHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(_ => new TraceDbContext(fixture.Options));
        builder.Services.AddScoped(sp =>
        {
            var requestDb = sp.GetRequiredService<TraceDbContext>();
            var subjectService = Service(requestDb);
            return new AiMutationService(new AiMutationRepository(requestDb), new EventRepository(requestDb), Events(requestDb, subjectService),
                new StorylineService(requestDb, new AnalysisOutbox(requestDb), new Clock()), new AiConversationRepository(requestDb),
                new CurrentUserContext(sp.GetRequiredService<IHttpContextAccessor>()), new Clock(), subjectService);
        });
        // Approval actions use the real MVC binding and mutation service without invoking the model service.
        builder.Services.AddScoped(sp => new AssistantController(null!, NullLogger<AssistantController>.Instance, sp.GetRequiredService<AiMutationService>()));
        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());

        async Task<AiApprovalRequest> Request(Guid target)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/subjects/delete-requests", new SubjectDeleteRequest("SubjectRelation", target, Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<AiApprovalRequest>())!;
        }
        static string Path(AiApprovalRequest approval) => $"/api/v1/ai/conversations/{approval.ConversationId}/approvals/{approval.Id}/decision";
        async Task<AiApprovalDecisionResponse> Decide(AiApprovalRequest approval, string decision)
        {
            using var response = await client.PostAsJsonAsync(Path(approval), new { decision });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<AiApprovalDecisionResponse>())!;
        }

        var cancelled = await Request(removable.Id);
        Assert.Equal("Cancelled", (await Decide(cancelled, "cancel")).State);
        Assert.Equal("Cancelled", (await Decide(cancelled, "confirm")).State);
        Assert.Null((await db.SubjectRelations.AsNoTracking().SingleAsync(x => x.Id == removable.Id)).RemovedAt);

        var pending = await Request(removable.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path(pending), new { decision = "yes" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path(pending), new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path(pending), new { decision = "confirm", targetId = bridge.Id })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", (user + 100).ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(Path(pending), new { decision = "confirm" })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        var deleted = await Decide(pending, "confirm");
        Assert.Equal("Succeeded", deleted.State);
        Assert.Equal(deleted.Result.OperationId, (await Decide(pending, "confirm")).Result.OperationId);
        Assert.NotNull((await db.SubjectRelations.AsNoTracking().SingleAsync(x => x.Id == removable.Id)).RemovedAt);
        var conflict = await Decide(await Request(bridge.Id), "confirm");
        Assert.Equal("Conflict", conflict.State);
        Assert.Contains(cat.Name, conflict.Result.Message.Content);
        Assert.Null((await db.SubjectRelations.AsNoTracking().SingleAsync(x => x.Id == bridge.Id)).RemovedAt);
        await app.StopAsync();
    }

    private sealed class ApprovalTestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
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
    public async Task Ai_subject_writes_are_explicit_idempotent_and_have_durable_real_links_after_answer_failure()
    {
        var user = User(); await using var db = Db(); var service = Service(db); var self = await service.EnsureSelfAsync(user, default); var cat = await Add(service, user, self.Id);
        var conversation = new AiConversation { Id = Guid.NewGuid(), UserId = user, Title = "人物操作", CreatedAt = Now, UpdatedAt = Now };
        var message = new AiMessage { UserId = user, Conversation = conversation, Role = AiMessageRole.User, Content = "给猫添加一条打疫苗计划，并为猫与自己添加陪伴关系", CreatedAt = Now };
        db.AiMessages.Add(message); await db.SaveChangesAsync();
        var (mutations, context) = Mutations(db, user, service);
        var tools = new PersonalMutationTools(mutations, Events(db, service), new StorylineService(db, new AnalysisOutbox(db), new Clock()), context, service);
        tools.Configure(conversation.Id, message.Id, AssistantCalendarContext.Create(Now), "猫今天体重 4kg");
        await Assert.ThrowsAsync<MutationIntentRequiredException>(() => tools.CreateMySubjectEntryAsync(cat.Id, new(Title: "不应自动写入")));
        tools.Configure(conversation.Id, message.Id, AssistantCalendarContext.Create(Now), message.Content);
        var request = new SubjectEntryRequest(SubjectEntryKind.Plan, "下月打疫苗", PlannedAt: Now.AddMonths(1));
        var result = await tools.CreateMySubjectEntryAsync(cat.Id, request);
        var repeated = await tools.CreateMySubjectEntryAsync(cat.Id, request);
        Assert.Equal(result.OperationId, repeated.OperationId);
        var evidence = Assert.Single(Assert.IsType<EvidenceBundle>(result.Message.Evidence).SubjectEntries!);
        Assert.Contains($"[SubjectEntry #{evidence.EntryId}]", result.Message.Content);
        Assert.Equal(cat.Id, evidence.SubjectId);
        Assert.Single(tools.DrainEvents());
        // A failure delivering the model's later prose does not roll back an already committed receipt.
        await using var reopened = Db();
        Assert.Single(await reopened.SubjectEntries.Where(x => x.UserId == user && x.Title == request.Title).ToArrayAsync());
        Assert.Single(await reopened.Set<AiMutationOperation>().Where(x => x.UserId == user && x.State == AiMutationState.Succeeded).ToArrayAsync());
        Assert.Contains(await reopened.AiMessages.Where(x => x.UserId == user).Select(x => x.Content).ToArrayAsync(), x => x.Contains(evidence.EntryId.ToString()));
        Assert.Empty(await reopened.Events.Where(x => x.UserId == user).ToArrayAsync());
        var related = await tools.RelateMySubjectsAsync(cat.Id, new(self.Id, "陪伴"));
        var repeatedRelation = await tools.RelateMySubjectsAsync(cat.Id, new(self.Id, "陪伴"));
        Assert.Equal(related.OperationId, repeatedRelation.OperationId);
        Assert.Contains("已更新", related.Message.Content);
        Assert.Equal(cat.Id, Assert.Single(Assert.IsType<EvidenceBundle>(related.Message.Evidence).Subjects!).SubjectId);
        Assert.Single((await service.GraphAsync(user, default)).Relations, x => x.Label == "陪伴");
    }

    [Fact]
    public async Task Failed_receipt_transaction_rolls_back_subject_content_and_versions()
    {
        var user = User(); await using var db = Db(); var service = Service(db); var self = await service.EnsureSelfAsync(user, default); var cat = await Add(service, user, self.Id);
        var conversation = new AiConversation { Id = Guid.NewGuid(), UserId = user, Title = "回滚", CreatedAt = Now, UpdatedAt = Now };
        var message = new AiMessage { UserId = user, Conversation = conversation, Role = AiMessageRole.User, Content = "创建计划", CreatedAt = Now };
        db.AiMessages.Add(message); await db.SaveChangesAsync(); var (mutations, _) = Mutations(db, user, service);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mutations.WriteAsync(conversation.Id, message.Id, "CreateMySubjectEntry", new { title = "回滚内容" }, async (key, token) =>
        { await service.CreateEntryAsync(user, cat.Id, new(Title: "回滚内容"), key, token); throw new InvalidOperationException("receipt unavailable"); }, default));
        await using var check = Db();
        Assert.False(await check.SubjectEntries.AnyAsync(x => x.UserId == user && x.Title == "回滚内容"));
        Assert.Equal(cat.Version, (await Service(check).GetAsync(user, cat.Id, default)).Version);
        Assert.False(await check.Set<AiMutationOperation>().AnyAsync(x => x.UserId == user));
    }

    [Fact]
    public async Task Approvals_cannot_cross_users_expire_or_disconnect_graph_and_entry_delete_recomputes_fields()
    {
        var user = User(); await using var db = Db(); var service = Service(db); var self = await service.EnsureSelfAsync(user, default); var friend = await Add(service, user, self.Id, "朋友"); var cat = await Add(service, user, friend.Id);
        var (mutations, _) = Mutations(db, user, service);
        var bridge = (await service.GraphAsync(user, default)).Relations.Single(x => x.FromSubjectId == friend.Id && x.ToSubjectId == self.Id);
        var bridgeRequest = await mutations.RequestManualDeleteAsync("SubjectRelation", bridge.Id, Guid.NewGuid(), default);
        var blocked = await mutations.DecideAsync(bridgeRequest.ConversationId, bridgeRequest.Id, "confirm", default);
        Assert.Equal("Conflict", blocked.State); Assert.Null((await service.GetRelationAsync(user, bridge.Id, default)).RemovedAt);
        var weight = cat.Fields.Single(x => x.Key == "weight");
        var entry = await service.CreateEntryAsync(user, cat.Id, new(Title: "体重更新", FieldChanges: Value(weight.Id, 4)), null, default);
        var pending = await mutations.RequestManualDeleteAsync("SubjectEntry", entry.Id, Guid.NewGuid(), default);
        var (other, _) = Mutations(db, user + 100, service);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => other.DecideAsync(pending.ConversationId, pending.Id, "confirm", default));
        var deleted = await mutations.DecideAsync(pending.ConversationId, pending.Id, "confirm", default);
        Assert.Equal("Succeeded", deleted.State); Assert.Empty((await service.GetAsync(user, cat.Id, default)).Values);
        var expiring = await mutations.RequestManualDeleteAsync("Subject", cat.Id, Guid.NewGuid(), default);
        var operation = await db.Set<AiMutationOperation>().SingleAsync(x => x.Id == expiring.Id); operation.ExpiresAt = Now.AddSeconds(-1); await db.SaveChangesAsync();
        var expired = await mutations.DecideAsync(expiring.ConversationId, expiring.Id, "confirm", default);
        Assert.Equal("Expired", expired.State); Assert.Null((await db.Subjects.SingleAsync(x => x.Id == cat.Id)).DeletedAt);
    }

    [Fact]
    public async Task Media_is_private_and_current_historical_and_deleted_dossier_references_prevent_asset_deletion()
    {
        var user = User(); await using var db = Db();
        var repository = new SubjectRepository(db); var outbox = new AnalysisOutbox(db);
        var media = new MediaService(db, new UnusedObjectStorage(), outbox, new Clock(), repository);
        var service = new SubjectService(repository, media, outbox, new Clock());
        var self = await service.EnsureSelfAsync(user, default);
        var own = new MediaAsset { Id = Guid.NewGuid(), UserId = user, ObjectKey = "subject-model", OriginalFileName = "cat.glb", Kind = MediaKind.Model, Status = MediaAssetStatus.Ready, DeclaredMimeType = "model/gltf-binary", CreatedAt = Now, UpdatedAt = Now };
        var other = new MediaAsset { Id = Guid.NewGuid(), UserId = user + 100, ObjectKey = "other-model", OriginalFileName = "other.glb", Kind = MediaKind.Model, Status = MediaAssetStatus.Ready, DeclaredMimeType = "model/gltf-binary", CreatedAt = Now, UpdatedAt = Now };
        db.MediaAssets.AddRange(own, other); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() => service.CreateAsync(user, new(SubjectKind.Pet, "越权附件", [new(self.Id)], MediaIds: [other.Id]), null, default));
        var cat = await service.CreateAsync(user, new(SubjectKind.Pet, "私有模型", [new(self.Id)], MediaIds: [own.Id], CoverMediaId: own.Id), null, default);
        await Assert.ThrowsAsync<DomainValidationException>(() => media.DeleteAsync(user, own.Id, default));
        cat = await service.UpdateAsync(user, cat.Id, cat.Version, new(MediaIds: [], ClearCover: true), default);
        Assert.True(await repository.HasReferenceAsync(user, own.Id, default));
        Assert.False(await repository.HasReferenceAsync(user + 100, own.Id, default));
        var entry = await service.CreateEntryAsync(user, cat.Id, new(Title: "模型变化", MediaIds: [own.Id]), null, default);
        entry = await service.UpdateEntryAsync(user, entry.Id, entry.Version, new(MediaIds: []), default);
        await service.DeleteEntryAsync(user, entry.Id, entry.Version, default);
        cat = await service.GetAsync(user, cat.Id, default); await service.DeleteAsync(user, cat.Id, cat.Version, default);
        await Assert.ThrowsAsync<DomainValidationException>(() => media.DeleteAsync(user, own.Id, default));
        Assert.Contains(await db.SubjectMediaReferences.Where(x => x.UserId == user && x.MediaId == own.Id).ToArrayAsync(), x => x.TargetType == "SubjectEntry");
        Assert.Null((await db.MediaAssets.SingleAsync(x => x.Id == own.Id)).DeletedAt);
    }

    private sealed class UnusedObjectStorage : IObjectStorage
    {
        private static Exception Unused() => new InvalidOperationException("被历史引用的附件不得触及对象存储。");
        public Task EnsureBucketAsync(CancellationToken ct) => throw Unused();
        public Task<string> CreateMultipartUploadAsync(string key, string type, CancellationToken ct) => throw Unused();
        public Task<Uri> CreateUploadUrlAsync(string key, string type, DateTimeOffset expiry, CancellationToken ct) => throw Unused();
        public Task<Uri> CreatePartUploadUrlAsync(string key, string upload, int part, DateTimeOffset expiry, CancellationToken ct) => throw Unused();
        public Task<string> UploadPartAsync(string key, string upload, int part, Stream data, long size, CancellationToken ct) => throw Unused();
        public Task CompleteMultipartUploadAsync(string key, string upload, IReadOnlyList<CompletedPart> parts, CancellationToken ct) => throw Unused();
        public Task AbortMultipartUploadAsync(string key, string upload, CancellationToken ct) => throw Unused();
        public Task<StoredObjectInfo> GetInfoAsync(string key, CancellationToken ct) => throw Unused();
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => throw Unused();
        public Task PutAsync(string key, Stream data, string type, long size, CancellationToken ct) => throw Unused();
        public Task<Uri> CreateDownloadUrlAsync(string key, string name, string type, bool inline, DateTimeOffset expiry, CancellationToken ct) => throw Unused();
        public Task DeleteAsync(string key, CancellationToken ct) => throw Unused();
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FixedUserAccessor : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
    private sealed class EmptyMedia : IEventMediaService
    {
        public Task<IReadOnlyList<MediaAsset>> ResolveAsync(long userId, IReadOnlyList<Guid>? mediaIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MediaAsset>>([]);
        public void ReplaceCurrent(Event evt, SourceRevision revision, IReadOnlyList<MediaAsset> media, DateTimeOffset now) { }
    }
}
