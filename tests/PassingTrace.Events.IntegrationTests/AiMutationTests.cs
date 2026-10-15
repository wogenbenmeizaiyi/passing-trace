using PassingTrace.Events.Api.Ai.Assistant;
using PassingTrace.Events.Api.Ai.Assistant.Context;
using PassingTrace.Events.Api.Ai.Evidence;
using PassingTrace.Events.Api.Ai.Models;
using PassingTrace.Events.Api.Ai.Mutations;
using PassingTrace.Events.Api.Ai.Tools.Mutations;
using PassingTrace.Events.Api.Ai.Tools.Queries;
using PassingTrace.Events.Api.Common;
using System.Security.Claims;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Core.Social;
using PassingTrace.Events.Api.Ai.Amap;
using PassingTrace.Events.Api.Ai.Capabilities;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Media;
using PassingTrace.Events.Api.Social;
using PassingTrace.Events.Api.Storylines;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence;
using PassingTrace.Infrastructure.Persistence.Ai;
using Xunit;
using StackExchange.Redis;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AiMutationTests(StorylinePostgresFixture fixture) : IClassFixture<StorylinePostgresFixture>
{
    [Theory]
    [InlineData("Trace", EventStatus.Completed)]
    [InlineData("Plan", EventStatus.Planned)]
    public async Task Create_has_durable_clickable_receipt_outbox_and_same_turn_idempotency(string kind, EventStatus status)
    {
        await using var h = await CreateHarnessAsync("帮我创建一条记录和计划");
        var result = await h.Tools.CreateMyRecordAsync(kind, "跑步", "按刚才讨论的安排");
        var repeated = await h.Tools.CreateMyRecordAsync(kind, "跑步", "按刚才讨论的安排");
        Assert.Equal(result.OperationId, repeated.OperationId);
        Assert.Equal(result.Message.Id, repeated.Message.Id);
        Assert.Single(h.Tools.DrainEvents());
        var id = long.Parse(result.Targets.Single().Id);
        await using var fresh = new TraceDbContext(fixture.Options);
        var evt = await fresh.Events.Include(x => x.SourceRevisions).SingleAsync(x => x.Id == id);
        Assert.Equal(status, evt.Status);
        Assert.Equal("Asia/Shanghai", evt.Timezone);
        Assert.Single(evt.SourceRevisions);
        Assert.Contains($"[Event #{id}]", result.Message.Content);
        Assert.True(await fresh.AiMessages.AnyAsync(x => x.Id == result.Message.Id));
        Assert.True(await fresh.Set<AiMutationOperation>().AnyAsync(x => x.Id == result.OperationId && x.ResultJson != null));
        Assert.True(await fresh.OutboxMessages.AnyAsync(x => x.EventId == id));
        Assert.True(await fresh.UserDataWatermarks.AnyAsync(x => x.UserId == h.UserId && x.Version > 0));
        var context = await ConversationContextSnapshot.LoadAsync(new AiConversationRepository(fresh), h.UserId, h.ConversationId, long.MaxValue, h.Clock.GetUtcNow(), default);
        var receipt = Assert.Single(context.RecentMessages, x => x.Content.StartsWith("已创建"));
        Assert.Contains("跑步", receipt.Content);
        Assert.Contains(id.ToString(), receipt.Content);
    }

    [Theory]
    [InlineData("今天跑步五公里，真开心")]
    [InlineData("总结刚才的安排")]
    [InlineData("不要创建记录")]
    [InlineData("如何删除记录")]
    [InlineData("确定")]
    [InlineData("我今天创建了记录，很开心")]
    [InlineData("别帮我创建记录，只总结聊天")]
    [InlineData("不需要你保存计划")]
    [InlineData("建议创建故事线来管理计划")]
    public async Task Ordinary_messages_and_text_confirmation_cannot_write(string question)
    {
        await using var h = await CreateHarnessAsync(question);
        await Assert.ThrowsAsync<MutationIntentRequiredException>(() => h.Tools.CreateMyRecordAsync("Trace", "不能创建"));
        await Assert.ThrowsAsync<MutationIntentRequiredException>(() => h.Tools.RequestDeleteMyRecordAsync(1));
        Assert.False(h.Tools.HasOperations);
        Assert.False(await h.Db.Set<AiMutationOperation>().AnyAsync(x => x.UserId == h.UserId));
    }

    [Theory]
    [InlineData("加上")]
    [InlineData("先不用删，你先加")]
    public async Task Colloquial_add_request_can_create_storyline_without_requesting_deletion(string question)
    {
        await using var h = await CreateHarnessAsync(question);
        Assert.True(PersonalMutationTools.MayWrite(question));

        var result = await h.Tools.CreateMyStorylineAsync("西溪湿地一日游", [new(NewPlan: new("福堤漫步"))]);

        Assert.Equal("Storyline", result.Targets[0].Type);
        await Assert.ThrowsAsync<MutationIntentRequiredException>(() => h.Tools.RequestDeleteMyStorylineAsync(Guid.Parse(result.Targets[0].Id)));
        Assert.Empty(await h.Mutations.ListApprovalsAsync(h.ConversationId, default));
    }

    [Theory]
    [InlineData("对就这样")]
    [InlineData("确定")]
    public async Task Short_confirmation_creates_seven_plan_storyline_through_agent_and_bypasses_cache(string question)
    {
        await using var h = await CreateHarnessAsync(question);
        h.Db.AiMessages.AddRange(
            new AiMessage
            {
                ConversationId = h.ConversationId,
                UserId = h.UserId,
                Role = AiMessageRole.User,
                Content = "帮我创建西溪湿地故事线，拆成7个计划",
                CreatedAt = h.Clock.GetUtcNow()
            },
            new AiMessage
            {
                ConversationId = h.ConversationId,
                UserId = h.UserId,
                Role = AiMessageRole.Assistant,
                Content = "确认这7个环节可以吗？你回一句我就建故事线。",
                CreatedAt = h.Clock.GetUtcNow()
            });
        await h.Db.SaveChangesAsync();
        using var model = new StorylineMutationModel();
        var service = CreateAssistant(h, model);
        var stream = new List<AssistantStreamEvent>();

        await foreach (var item in service.SendAsync(h.ConversationId, question, default)) stream.Add(item);

        var result = Assert.IsType<AiMutationResult>(Assert.Single(stream, item => item.Type == "mutation-result").Data);
        Assert.Equal(8, result.Targets.Count);
        Assert.Contains("[Storyline #", result.Message.Content);
        Assert.Equal(7, result.Targets.Count(target => target.Type == "Plan"));
        var story = await h.Stories.GetAsync(h.UserId, Guid.Parse(result.Targets[0].Id), null, default);
        Assert.Equal(7, story.Nodes.Count);
        Assert.Equal(6, story.Edges.Count);
        Assert.Equal("done", stream[^1].Type);
        Assert.Empty(await h.Mutations.ListApprovalsAsync(h.ConversationId, default));
        var context = await ConversationContextSnapshot.LoadAsync(new AiConversationRepository(h.Db), h.UserId,
            h.ConversationId, long.MaxValue, h.Clock.GetUtcNow(), default);
        Assert.Contains(context.RecentMessages, message => message.IsMutationReceipt);
        Assert.False(PersonalMutationTools.MayWrite("确定", context.RecentMessages));
    }

    [Fact]
    public async Task Short_confirmation_of_edit_cannot_grant_creation_or_deletion()
    {
        await using var h = await CreateHarnessAsync("创建计划");
        var created = await h.Tools.CreateMyRecordAsync("Plan", "旧标题");
        var history = new ConversationContextMessage[] {
            new(1, AiMessageRole.User, "修改计划标题为新标题"),
            new(2, AiMessageRole.Assistant, "确认要修改标题为新标题吗？"),
        };
        h.Tools.Configure(h.ConversationId, h.MessageId, AssistantCalendarContext.Create(h.Clock.GetUtcNow()), "对就这样", history);

        var updated = await h.Tools.UpdateMyRecordAsync(long.Parse(created.Targets[0].Id), new(Title: "新标题"));

        Assert.Equal("新标题", updated.Targets[0].Title);
        await Assert.ThrowsAsync<MutationIntentRequiredException>(() => h.Tools.CreateMyRecordAsync("Plan", "不能额外创建"));
        await Assert.ThrowsAsync<MutationIntentRequiredException>(() => h.Tools.RequestDeleteMyRecordAsync(long.Parse(created.Targets[0].Id)));
    }

    [Fact]
    public async Task Mcp_intent_refusal_preserves_safe_reason_instead_of_reporting_query_failure()
    {
        await using var h = await CreateHarnessAsync("总结一下这个安排");
        var package = new PersonalMutationsCapabilityPackage(h.Tools);
        await using var mcp = await InternalMcpToolSession.CreateAsync(package.CreateTools().OfType<AIFunction>(),
            writeTools: package.WriteTools.ToHashSet());
        var create = mcp.Tools.OfType<AIFunction>().Single(tool => tool.Name == "CreateMyRecord");

        var error = await Assert.ThrowsAsync<AssistantToolInvocationException>(() =>
            create.InvokeAsync(new() { ["kind"] = "Plan", ["title"] = "不能自动保存" }).AsTask());

        Assert.Equal("mutation_intent_required", error.ErrorCode);
        Assert.True(error.IsWriteTool);
        var presented = PassingTrace.Events.Api.Ai.Assistant.Presentation.AssistantErrorPresenter.Present(error);
        Assert.Equal("mutation_intent_required", presented.Code);
        Assert.DoesNotContain("查询", presented.Message);
        Assert.False(presented.Retryable);
        Assert.False(await h.Db.Events.AnyAsync(evt => evt.UserId == h.UserId));
    }

    [Fact]
    public async Task Patch_preserves_source_fields_media_locations_classification_and_participants()
    {
        await using var h = await CreateHarnessAsync("创建记录，然后修改标题");
        var created = await h.Tools.CreateMyRecordAsync("Trace", "原标题", "原文", h.Clock.GetUtcNow());
        var id = long.Parse(created.Targets.Single().Id);
        var evt = await h.Db.Events.Include(x => x.SourceRevisions).SingleAsync(x => x.Id == id);
        var media = new MediaAsset
        {
            Id = Guid.NewGuid(),
            UserId = h.UserId,
            Kind = MediaKind.Image,
            Status = MediaAssetStatus.Ready,
            OriginalFileName = "photo.png",
            DeclaredMimeType = "image/png",
            ObjectKey = "tests/photo",
            ExpectedSize = 1,
            CreatedAt = h.Clock.GetUtcNow(),
            UpdatedAt = h.Clock.GetUtcNow(),
        };
        h.Db.MediaAssets.Add(media);
        await h.Db.SaveChangesAsync();
        await h.Events.UpdateSourceAsync(new(h.UserId, id, evt.RowVersion, evt.Title, evt.RawContent, evt.HappenedAt,
            null, evt.Timezone, [media.Id], new(null, [new(null, "测试标签")], null),
            [new("杭州", null, null, null, null, null, null, null, null, null, null, null, EventLocationSource.ManualText, null)]), default);
        evt.Participants.Add(new EventParticipant { UserId = h.UserId + 1, FriendshipId = Guid.NewGuid(), Active = true });
        await h.Db.SaveChangesAsync();
        var beforeRevision = evt.CurrentSourceRevision;
        var result = await h.Tools.UpdateMyRecordAsync(id, new(Title: "新标题"));
        var repeated = await h.Tools.UpdateMyRecordAsync(id, new(Title: "新标题"));
        Assert.Equal(result.OperationId, repeated.OperationId);
        await using var fresh = new TraceDbContext(fixture.Options);
        var saved = await new EventRepository(fresh).FindAsync(h.UserId, id, default);
        Assert.Equal(beforeRevision + 1, saved!.CurrentSourceRevision);
        Assert.Equal("新标题", saved.Title);
        Assert.Equal("原文", saved.RawContent);
        Assert.NotNull(saved.HappenedAt);
        Assert.Equal(media.Id, Assert.Single(saved.MediaAssets).MediaAssetId);
        Assert.Equal(h.UserId + 1, Assert.Single(saved.Participants).UserId);
        var source = saved.SourceRevisions.Single(x => x.Revision == saved.CurrentSourceRevision);
        Assert.Contains(source.Labels, x => x.DisplayName == "测试标签");
        Assert.Contains(source.Locations, x => x.Name == "杭州");
        Assert.Contains((h.UserId + 1).ToString(), source.ParticipantIdsJson);
    }

    [Fact]
    public async Task Storyline_creates_inline_plans_and_editing_nodes_does_not_delete_records()
    {
        await using var h = await CreateHarnessAsync("创建记录和故事线，再编辑添加计划并移除节点");
        var record = await h.Tools.CreateMyRecordAsync("Trace", "准备好了");
        var id = long.Parse(record.Targets.Single().Id);
        var result = await h.Tools.CreateMyStorylineAsync("周末", [new(id), new(NewPlan: new("去公园"))]);
        var repeated = await h.Tools.CreateMyStorylineAsync("周末", [new(id), new(NewPlan: new("去公园"))]);
        Assert.Equal(result.OperationId, repeated.OperationId);
        Assert.Equal(2, result.Targets.Count);
        Assert.Contains("[Storyline #", result.Message.Content);
        Assert.Contains("[Event #", result.Message.Content);
        var storyId = Guid.Parse(result.Targets[0].Id);
        var appended = await h.Tools.UpdateMyStorylineAsync(storyId, new("add-plan", NewPlan: new("回家休息")));
        Assert.Equal(2, appended.Targets.Count);
        var story = await h.Stories.GetAsync(h.UserId, storyId, null, default);
        var leaf = story.Nodes.Single(x => x.Title == "回家休息");
        await h.Tools.UpdateMyStorylineAsync(storyId, new("remove-node", NodeKey: leaf.Key));
        Assert.NotNull(await h.Mutations.ReadRecordAsync(leaf.EventId, default));
        var updated = await h.Tools.UpdateMyStorylineAsync(storyId, new("update-metadata", Title: "周末安排"));
        Assert.Equal("周末安排", updated.Targets[0].Title);
    }

    [Fact]
    public async Task Storyline_nodes_support_existing_records_stage_moves_sync_and_validated_reconnection()
    {
        await using var h = await CreateHarnessAsync("创建记录，修改故事线阶段并同步或移除节点");
        var first = await h.Tools.CreateMyRecordAsync("Trace", "出发");
        var second = await h.Tools.CreateMyRecordAsync("Trace", "抵达");
        var third = await h.Tools.CreateMyRecordAsync("Trace", "返程");
        var firstId = long.Parse(first.Targets[0].Id);
        var secondId = long.Parse(second.Targets[0].Id);
        var thirdId = long.Parse(third.Targets[0].Id);
        var stage = Guid.NewGuid();
        var nextStage = Guid.NewGuid();
        var firstNode = Guid.NewGuid();
        var secondNode = Guid.NewGuid();
        var created = await h.Stories.CreateAsync(h.UserId, new("旅程", null, "trip", PassingTrace.Core.Storylines.StorylineStatus.Ongoing,
            null, null, [new(stage, "去程", 0), new(nextStage, "回程", 1)],
            [new(firstNode, "existing-event", firstId, null, null, stage, 0), new(secondNode, "existing-event", secondId, null, null, stage, 1)],
            [new(Guid.NewGuid(), firstNode, secondNode, PassingTrace.Core.Storylines.StorylineRelationType.Sequence, null)], null), null, default);
        var storyId = created.Storyline.Id;
        await h.Tools.UpdateMyStorylineAsync(storyId, new("move-node-to-stage", NodeKey: secondNode, StageKey: nextStage));
        await h.Tools.UpdateMyRecordAsync(firstId, new(Title: "重新出发"));
        await h.Tools.UpdateMyStorylineAsync(storyId, new("sync-node", NodeKey: firstNode));
        await h.Tools.UpdateMyStorylineAsync(storyId, new("add-existing-event", EventId: thirdId, ParentNodeKey: secondNode, StageKey: nextStage));
        var story = await h.Stories.GetAsync(h.UserId, storyId, null, default);
        Assert.Equal(2, story.Nodes.Single(x => x.Key == firstNode).SourceRevision);
        Assert.Equal("重新出发", story.Nodes.Single(x => x.Key == firstNode).Title);
        Assert.Equal(nextStage, story.Nodes.Single(x => x.Key == secondNode).StageKey);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Tools.UpdateMyStorylineAsync(storyId, new("remove-node", NodeKey: secondNode)));
        await h.Tools.UpdateMyStorylineAsync(storyId, new("remove-node-and-reconnect", NodeKey: secondNode));
        var final = await h.Stories.GetAsync(h.UserId, storyId, null, default);
        Assert.Equal(2, final.Nodes.Count);
        Assert.Single(final.Edges);
        Assert.Equal(firstNode, final.Edges[0].SourceNodeKey);
        Assert.Equal(thirdId, final.Nodes.Single(x => x.Key == final.Edges[0].TargetNodeKey).EventId);
        Assert.NotNull(await h.Mutations.ReadRecordAsync(secondId, default));
    }

    [Fact]
    public void Decision_json_rejects_target_substitution()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AiApprovalDecisionRequest>(
            "{\"decision\":\"confirm\",\"targetId\":\"999\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task Invalid_storyline_rolls_back_plans_indexes_outbox_and_receipts()
    {
        await using var h = await CreateHarnessAsync("创建故事线");
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Tools.CreateMyStorylineAsync("不能保存",
            [new(NewPlan: new("临时计划")), new(NewPlan: new(" "))]));
        await using var fresh = new TraceDbContext(fixture.Options);
        Assert.False(await fresh.Events.AnyAsync(x => x.UserId == h.UserId));
        Assert.False(await fresh.Storylines.AnyAsync(x => x.UserId == h.UserId));
        Assert.False(await fresh.Set<AiMutationOperation>().AnyAsync(x => x.UserId == h.UserId));
        Assert.False(await fresh.OutboxMessages.AnyAsync(x => x.UserId == h.UserId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipt_failure_rolls_back_business_changes_and_authorization_completion(bool deleting)
    {
        await using var h = await CreateHarnessAsync("创建记录然后删除");
        AiApprovalRequest? approval = null;
        if (deleting)
        {
            var created = await h.Tools.CreateMyRecordAsync("Trace", "保留原内容");
            approval = await h.Tools.RequestDeleteMyRecordAsync(long.Parse(created.Targets[0].Id));
        }
        var beforeEvents = await h.Db.Events.CountAsync(x => x.UserId == h.UserId);
        var beforeMessages = await h.Db.AiMessages.CountAsync(x => x.UserId == h.UserId);
        var beforeOutbox = await h.Db.OutboxMessages.CountAsync(x => x.UserId == h.UserId);
        var beforeWatermark = await h.Db.UserDataWatermarks.Where(x => x.UserId == h.UserId).Select(x => (long?)x.Version).SingleOrDefaultAsync();
        var failing = new AiMutationService(new TestMutationRepository(new AiMutationRepository(h.Db), failReceipt: true), new EventRepository(h.Db),
            h.Events, h.Stories, new AiConversationRepository(h.Db), h.User, h.Clock);
        if (deleting)
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.DecideAsync(h.ConversationId, approval!.Id, "confirm", default));
        else
        {
            var tools = new PersonalMutationTools(failing, h.Events, h.Stories, h.User);
            tools.Configure(h.ConversationId, h.MessageId, AssistantCalendarContext.Create(h.Clock.GetUtcNow()), "创建记录");
            await Assert.ThrowsAsync<InvalidOperationException>(() => tools.CreateMyRecordAsync("Trace", "不会部分保存"));
        }
        await using var fresh = new TraceDbContext(fixture.Options);
        Assert.Equal(beforeEvents, await fresh.Events.CountAsync(x => x.UserId == h.UserId));
        Assert.Equal(beforeMessages, await fresh.AiMessages.CountAsync(x => x.UserId == h.UserId));
        Assert.Equal(beforeOutbox, await fresh.OutboxMessages.CountAsync(x => x.UserId == h.UserId));
        Assert.Equal(beforeWatermark, await fresh.UserDataWatermarks.Where(x => x.UserId == h.UserId).Select(x => (long?)x.Version).SingleOrDefaultAsync());
        Assert.False(await fresh.Events.AnyAsync(x => x.UserId == h.UserId && x.DeletedAt != null));
        if (deleting)
            Assert.Equal(AiMutationState.Pending, (await fresh.Set<AiMutationOperation>().SingleAsync(x => x.Id == approval!.Id)).State);
        else Assert.False(await fresh.Set<AiMutationOperation>().AnyAsync(x => x.UserId == h.UserId));
    }

    [Fact]
    public async Task Concurrent_confirmations_commit_one_deletion_and_one_receipt()
    {
        await using var h = await CreateHarnessAsync("创建记录然后删除");
        var created = await h.Tools.CreateMyRecordAsync("Trace", "仅删除一次");
        var request = await h.Tools.RequestDeleteMyRecordAsync(long.Parse(created.Targets[0].Id));
        await using var first = CreateServices(new TraceDbContext(fixture.Options), h.UserId, h.ConversationId, h.MessageId, h.Clock, "删除记录");
        await using var second = CreateServices(new TraceDbContext(fixture.Options), h.UserId, h.ConversationId, h.MessageId, h.Clock, "删除记录");
        var results = await Task.WhenAll(first.Mutations.DecideAsync(h.ConversationId, request.Id, "confirm", default),
            second.Mutations.DecideAsync(h.ConversationId, request.Id, "confirm", default));
        Assert.All(results, x => Assert.Equal("Succeeded", x.State));
        Assert.Equal(results[0].Result.Message.Id, results[1].Result.Message.Id);
        await using var fresh = new TraceDbContext(fixture.Options);
        Assert.Equal(1, await fresh.OutboxMessages.CountAsync(x => x.UserId == h.UserId && x.MessageType == "event.deleted"));
        Assert.Equal(1, await fresh.AiMessages.CountAsync(x => x.UserId == h.UserId && x.Content.StartsWith("已删除")));
    }

    [Fact]
    public async Task Confirmation_refreshes_previously_loaded_target_and_deleted_conversation_invalidates_grant()
    {
        await using var h = await CreateHarnessAsync("创建记录然后删除");
        var created = await h.Tools.CreateMyRecordAsync("Trace", "待核实");
        var id = long.Parse(created.Targets[0].Id);
        var request = await h.Tools.RequestDeleteMyRecordAsync(id);
        await using (var editor = CreateServices(new TraceDbContext(fixture.Options), h.UserId, h.ConversationId, h.MessageId, h.Clock, "修改标题"))
            await editor.Tools.UpdateMyRecordAsync(id, new(Title: "已经变更"));
        var result = await h.Mutations.DecideAsync(h.ConversationId, request.Id, "confirm", default);
        Assert.Equal("Conflict", result.State);
        Assert.Null((await h.Mutations.ReadRecordAsync(id, default)).DeletedAt);
        var conversation = await h.Db.AiConversations.SingleAsync(x => x.Id == h.ConversationId);
        conversation.DeletedAt = h.Clock.GetUtcNow();
        await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => h.Mutations.DecideAsync(h.ConversationId, request.Id, "confirm", default));
    }

    [Fact]
    public async Task Plan_patch_changes_or_clears_only_supplied_fields()
    {
        await using var h = await CreateHarnessAsync("创建计划然后修改时间");
        var created = await h.Tools.CreateMyRecordAsync("Plan", "原计划", "原说明", plannedAt: h.Clock.GetUtcNow());
        var id = long.Parse(created.Targets[0].Id);
        await h.Tools.UpdateMyRecordAsync(id, new(PlannedAt: h.Clock.GetUtcNow().AddDays(1), ClearFields: ["rawContent"]));
        var saved = await h.Mutations.ReadRecordAsync(id, default);
        Assert.Equal("原计划", saved.Title);
        Assert.Null(saved.RawContent);
        Assert.Equal(h.Clock.GetUtcNow().AddDays(1), saved.PlannedAt);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Tools.UpdateMyRecordAsync(id, new(HappenedAt: h.Clock.GetUtcNow())));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Tools.UpdateMyRecordAsync(id, new(Title: "新名", ClearFields: ["title"])));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Tools.UpdateMyRecordAsync(id, new()));
    }

    [Fact]
    public async Task Delete_requires_separate_confirmation_is_durable_and_replay_safe()
    {
        await using var h = await CreateHarnessAsync("创建记录，然后删除它");
        var created = await h.Tools.CreateMyRecordAsync("Trace", "待删除");
        var id = long.Parse(created.Targets.Single().Id);
        var request = await h.Tools.RequestDeleteMyRecordAsync(id);
        var repeatedRequest = await h.Tools.RequestDeleteMyRecordAsync(id);
        Assert.Equal(request.Id, repeatedRequest.Id);
        Assert.Null((await h.Mutations.ReadRecordAsync(id, default)).DeletedAt);
        Assert.Single(await h.Mutations.ListApprovalsAsync(h.ConversationId, default));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Mutations.DecideAsync(h.ConversationId, request.Id, "yes", default));
        var confirmed = await h.Mutations.DecideAsync(h.ConversationId, request.Id, "confirm", default);
        var repeated = await h.Mutations.DecideAsync(h.ConversationId, request.Id, "confirm", default);
        Assert.Equal("Succeeded", confirmed.State);
        Assert.Equal(confirmed.Result.Message.Id, repeated.Result.Message.Id);
        Assert.Empty(await h.Mutations.ListApprovalsAsync(h.ConversationId, default));
        await using var fresh = new TraceDbContext(fixture.Options);
        Assert.NotNull((await fresh.Events.SingleAsync(x => x.Id == id)).DeletedAt);
        Assert.True(await fresh.AiMessages.AnyAsync(x => x.Id == confirmed.Result.Message.Id));
    }

    [Theory]
    [InlineData("cancel", "Cancelled")]
    [InlineData("expire", "Expired")]
    [InlineData("edit", "Conflict")]
    public async Task Cancel_expiry_and_changed_version_never_delete(string action, string expected)
    {
        await using var h = await CreateHarnessAsync("创建计划，修改或删除计划");
        var created = await h.Tools.CreateMyRecordAsync("Plan", "跑步计划");
        var id = long.Parse(created.Targets.Single().Id);
        var request = await h.Tools.RequestDeleteMyRecordAsync(id);
        Assert.Equal("Plan", request.TargetType);
        if (action == "expire") h.Clock.Advance(TimeSpan.FromMinutes(16));
        if (action == "edit") await h.Tools.UpdateMyRecordAsync(id, new(Title: "更新后的计划"));
        var decided = await h.Mutations.DecideAsync(h.ConversationId, request.Id, action == "cancel" ? "cancel" : "confirm", default);
        Assert.Equal(expected, decided.State);
        await using var fresh = new TraceDbContext(fixture.Options);
        Assert.Null((await fresh.Events.SingleAsync(x => x.Id == id)).DeletedAt);
    }

    [Fact]
    public async Task Authorization_expiring_while_waiting_for_target_lock_never_deletes()
    {
        await using var h = await CreateHarnessAsync("创建记录然后删除");
        var created = await h.Tools.CreateMyRecordAsync("Trace", "等待锁期间到期");
        var id = long.Parse(created.Targets[0].Id);
        var approval = await h.Tools.RequestDeleteMyRecordAsync(id);
        var repository = new TestMutationRepository(new AiMutationRepository(h.Db), afterLock: () => h.Clock.Advance(TimeSpan.FromMinutes(16)));
        var service = new AiMutationService(repository, new EventRepository(h.Db), h.Events, h.Stories,
            new AiConversationRepository(h.Db), h.User, h.Clock);
        var result = await service.DecideAsync(h.ConversationId, approval.Id, "confirm", default);
        Assert.Equal("Expired", result.State);
        await using var fresh = new TraceDbContext(fixture.Options);
        Assert.Null((await fresh.Events.SingleAsync(x => x.Id == id)).DeletedAt);
    }

    [Fact]
    public async Task Foreign_targets_conversations_and_approvals_are_inaccessible()
    {
        await using var owner = await CreateHarnessAsync("创建记录然后删除");
        await using var other = await CreateHarnessAsync("修改或删除记录，创建故事线");
        var created = await owner.Tools.CreateMyRecordAsync("Trace", "私密记录");
        var id = long.Parse(created.Targets.Single().Id);
        var approval = await owner.Tools.RequestDeleteMyRecordAsync(id);
        await Assert.ThrowsAsync<EventNotFoundException>(() => other.Tools.UpdateMyRecordAsync(id, new(Title: "越权")));
        await Assert.ThrowsAsync<EventNotFoundException>(() => other.Tools.RequestDeleteMyRecordAsync(id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => other.Mutations.DecideAsync(owner.ConversationId, approval.Id, "confirm", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => owner.Mutations.DecideAsync(other.ConversationId, approval.Id, "confirm", default));
        await Assert.ThrowsAsync<DomainValidationException>(() => other.Tools.CreateMyStorylineAsync("越权", [new(id)]));
    }

    [Fact]
    public async Task Deleting_storyline_preserves_its_records_and_plans()
    {
        await using var h = await CreateHarnessAsync("创建故事线然后删除故事线");
        var created = await h.Tools.CreateMyStorylineAsync("周末计划", [new(NewPlan: new("去公园"))]);
        var storyId = Guid.Parse(created.Targets[0].Id);
        var planId = long.Parse(created.Targets[1].Id);
        var request = await h.Tools.RequestDeleteMyStorylineAsync(storyId);
        var result = await h.Mutations.DecideAsync(h.ConversationId, request.Id, "confirm", default);
        Assert.Equal("Succeeded", result.State);
        await using var fresh = new TraceDbContext(fixture.Options);
        Assert.NotNull((await fresh.Storylines.SingleAsync(x => x.Id == storyId)).DeletedAt);
        Assert.Null((await fresh.Events.SingleAsync(x => x.Id == planId)).DeletedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_agent_mcp_stream_emits_durable_link_even_when_model_then_fails(bool fail)
    {
        await using var h = await CreateHarnessAsync("创建一个跑步计划");
        using var model = new MutationModel(fail, false);
        var service = CreateAssistant(h, model);
        var stream = new List<AssistantStreamEvent>();
        async Task Run()
        {
            await foreach (var item in service.SendAsync(h.ConversationId, "创建一个跑步计划", default)) stream.Add(item);
        }
        if (fail) await Assert.ThrowsAnyAsync<Exception>(Run); else await Run();
        var result = Assert.IsType<AiMutationResult>(Assert.Single(stream, item => item.Type == "mutation-result").Data);
        Assert.Contains("[Event #", result.Message.Content);
        await using var fresh = new TraceDbContext(fixture.Options);
        Assert.True(await fresh.AiMessages.AnyAsync(x => x.Id == result.Message.Id));
        Assert.True(await fresh.Events.AnyAsync(x => x.Id == long.Parse(result.Targets.Single().Id)));
        if (!fail)
        {
            Assert.Equal("done", stream[^1].Type);
            var evidence = Assert.IsType<EvidenceBundle>(Assert.Single(stream, item => item.Type == "evidence").Data);
            Assert.Equal(result.Targets[0].Title, Assert.Single(evidence.Records).Title);
        }
    }

    [Fact]
    public async Task Agent_can_request_deletion_but_finishes_without_executing_it()
    {
        await using var h = await CreateHarnessAsync("创建一个计划然后删除它");
        using var model = new MutationModel(false, true);
        var service = CreateAssistant(h, model);
        var stream = new List<AssistantStreamEvent>();
        await foreach (var item in service.SendAsync(h.ConversationId, "创建一个计划然后删除它", default)) stream.Add(item);
        var request = Assert.IsType<AiApprovalRequest>(Assert.Single(stream, x => x.Type == "approval-request").Data);
        Assert.Null((await h.Db.Events.SingleAsync(x => x.Id == long.Parse(request.TargetId))).DeletedAt);
        Assert.Equal("done", stream[^1].Type);
        Assert.Single(await h.Mutations.ListApprovalsAsync(h.ConversationId, default));
    }

    [Fact]
    public async Task Mcp_schema_rejects_unknown_parameters_before_writing_and_does_not_expose_delete_executor()
    {
        await using var h = await CreateHarnessAsync("创建记录");
        var package = new PersonalMutationsCapabilityPackage(h.Tools);
        var tools = package.CreateTools().OfType<AIFunction>().ToArray();
        Assert.Equal(19, tools.Length);
        Assert.Contains(tools, x => x.Name == "CreateMySubjectEntry");
        Assert.Contains(tools, x => x.Name == "RequestDeleteMySubjectContent");
        Assert.DoesNotContain(tools, x => x.Name is "DeleteMyRecord" or "DeleteMyStoryline");
        await using var mcp = await InternalMcpToolSession.CreateAsync(tools, writeTools: package.WriteTools.ToHashSet());
        var create = (AIFunction)mcp.Tools.Single(x => x.Name == "CreateMyRecord");
        Assert.False(create.JsonSchema.GetProperty("properties").TryGetProperty("userId", out _));
        var result = Assert.IsType<JsonElement>(await create.InvokeAsync(new() { ["kind"] = "Trace", ["title"] = "不能保存", ["userId"] = h.UserId }));
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.False(await h.Db.Events.AnyAsync(x => x.UserId == h.UserId));
    }

    private AssistantService CreateAssistant(Harness h, IChatClient model)
    {
        var db = AssistantSkillServiceTests.StrictProxy.Create<IDatabase>(_ => throw new InvalidOperationException("Mutation turns must bypass answer cache"));
        var redis = AssistantSkillServiceTests.StrictProxy.Create<IConnectionMultiplexer>(method => method.Name == "GetDatabase" ? db : throw new InvalidOperationException());
        var embedding = AssistantSkillServiceTests.StrictProxy.Create<IEmbeddingGenerator<string, Embedding<float>>>(_ => throw new InvalidOperationException("No embedding expected"));
        var gateway = AssistantSkillServiceTests.StrictProxy.Create<IAmapMcpGateway>(method => method.Name == "get_IsConfigured" ? false : throw new InvalidOperationException());
        var quota = AssistantSkillServiceTests.StrictProxy.Create<IAmapQuotaGuard>(_ => throw new InvalidOperationException());
        var personal = new PersonalRecordTools(new PersonalRecordQueries(h.Db), h.User, embedding);
        var maps = new AmapAiTools(gateway, quota, h.Clock);
        return new(new AiConversationRepository(h.Db), h.User, personal, maps,
            [new PersonalRecordsCapabilityPackage(personal), new PersonalMutationsCapabilityPackage(h.Tools)], model, redis,
            Options.Create(new AiModelOptions()), NullLoggerFactory.Instance, new ServiceCollection().BuildServiceProvider(), h.Clock,
            mutationTools: h.Tools);
    }

    private sealed class MutationModel(bool fail, bool requestDelete) : IChatClient
    {
        private int _calls;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No extra model call expected");
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _calls++;
            if (requestDelete && _calls > 3) throw new InvalidOperationException("An approval must end the model turn");
            FunctionCallContent? call = _calls switch
            {
                1 => new("skill", "ReadAssistantSkill", new Dictionary<string, object?> { ["key"] = "mutations" }),
                2 => new("create", "CreateMyRecord", new Dictionary<string, object?> { ["kind"] = "Plan", ["title"] = "跑步" }),
                _ => null,
            };
            if (_calls == 3 && fail) throw new TimeoutException("Simulated failure after commit");
            if (_calls == 3 && requestDelete)
            {
                var created = messages.SelectMany(x => x.Contents).OfType<FunctionResultContent>().Last();
                var json = JsonSerializer.SerializeToElement(created.Result);
                var id = long.Parse(json.GetProperty("structuredContent").GetProperty("targets")[0].GetProperty("id").GetString()!);
                call = new("delete-request", "RequestDeleteMyRecord", new Dictionary<string, object?> { ["eventId"] = id });
            }
            if (call is not null) yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [call], FinishReason = ChatFinishReason.ToolCalls };
            else yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent(requestDelete ? "请在输入框上方确认删除。" : "安排好了。")], FinishReason = ChatFinishReason.Stop };
            await Task.CompletedTask;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class StorylineMutationModel : IChatClient
    {
        private int _calls;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No extra model call expected");
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            FunctionCallContent? call = ++_calls switch
            {
                1 => new("skill", "ReadAssistantSkill", new Dictionary<string, object?> { ["key"] = "mutations" }),
                2 => new("create-story", "CreateMyStoryline", new Dictionary<string, object?>
                {
                    ["title"] = "西溪湿地一日游",
                    ["categoryKey"] = "trip",
                    ["nodes"] = Enumerable.Range(1, 7).Select(i => new { newPlan = new { title = $"环节{i}" } }).ToArray(),
                }),
                _ => null,
            };
            Assert.True(_calls <= 3);
            if (call is not null)
                yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [call], FinishReason = ChatFinishReason.ToolCalls };
            else
                yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent("已按7个环节创建故事线，请确认能打开链接。")], FinishReason = ChatFinishReason.Stop };
            await Task.CompletedTask;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private async Task<Harness> CreateHarnessAsync(string question)
    {
        var db = new TraceDbContext(fixture.Options);
        var userId = Random.Shared.NextInt64(2_000_000, 9_000_000);
        var clock = new TestClock();
        var conversation = new AiConversation { Id = Guid.NewGuid(), UserId = userId, Title = "写入测试", CreatedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow() };
        var message = new AiMessage { UserId = userId, Conversation = conversation, Role = AiMessageRole.User, Content = question, CreatedAt = clock.GetUtcNow() };
        db.AiMessages.Add(message);
        await db.SaveChangesAsync();
        return CreateServices(db, userId, conversation.Id, message.Id, clock, question);
    }

    private static Harness CreateServices(TraceDbContext db, long userId, Guid conversationId, long messageId, TestClock clock, string question)
    {
        var user = new CurrentUserContext(new TestHttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")),
            }
        });
        var outbox = new AnalysisOutbox(db);
        var storage = AssistantSkillServiceTests.StrictProxy.Create<IObjectStorage>(_ => throw new InvalidOperationException("No storage call expected"));
        var identity = AssistantSkillServiceTests.StrictProxy.Create<ISocialIdentityClient>(_ => throw new InvalidOperationException("No identity call expected"));
        var participation = new EventParticipationService(db, new FriendService(db, identity, outbox, clock));
        var eventService = new EventService(new EventRepository(db), clock, new MediaService(db, storage, outbox, clock), outbox, participation);
        var storylineService = new StorylineService(db, outbox, clock);
        var mutationService = new AiMutationService(new AiMutationRepository(db), new EventRepository(db), eventService, storylineService,
            new AiConversationRepository(db), user, clock);
        var tools = new PersonalMutationTools(mutationService, eventService, storylineService, user);
        tools.Configure(conversationId, messageId, AssistantCalendarContext.Create(clock.GetUtcNow()), question);
        return new(db, userId, conversationId, messageId, clock, eventService, storylineService, mutationService, tools, user);
    }

    private sealed record Harness(TraceDbContext Db, long UserId, Guid ConversationId, long MessageId, TestClock Clock, EventService Events,
        StorylineService Stories, AiMutationService Mutations, PersonalMutationTools Tools, CurrentUserContext User) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestMutationRepository(IAiMutationRepository inner, bool failReceipt = false, Action? afterLock = null) : IAiMutationRepository
    {
        public Task<T> ExecuteAsync<T>(string key, Func<CancellationToken, Task<T>> action, CancellationToken ct) => inner.ExecuteAsync(key, action, ct);
        public async Task LockTargetAsync(long userId, string type, string id, CancellationToken ct)
        {
            await inner.LockTargetAsync(userId, type, id, ct);
            afterLock?.Invoke();
        }
        public Task<bool> HasSourceMessageAsync(long userId, Guid conversation, long messageId, CancellationToken ct) => inner.HasSourceMessageAsync(userId, conversation, messageId, ct);
        public Task<AiMutationOperation?> FindByKeyAsync(long userId, string key, CancellationToken ct) => inner.FindByKeyAsync(userId, key, ct);
        public Task<AiMutationOperation?> FindAsync(long userId, Guid conversation, Guid id, CancellationToken ct) => inner.FindAsync(userId, conversation, id, ct);
        public Task<IReadOnlyList<AiMutationOperation>> ListPendingAsync(long userId, Guid conversation, DateTimeOffset now, CancellationToken ct) => inner.ListPendingAsync(userId, conversation, now, ct);
        public void Add(AiMutationOperation operation) => inner.Add(operation);
        public void Add(AiMessage message) => inner.Add(message);
        public Task SaveChangesAsync(CancellationToken ct) => failReceipt
            ? throw new InvalidOperationException("Simulated receipt storage failure after business SaveChanges") : inner.SaveChangesAsync(ct);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 30, 5, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
