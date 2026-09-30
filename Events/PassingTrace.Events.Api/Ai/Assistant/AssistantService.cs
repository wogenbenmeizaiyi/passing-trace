using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai.Amap;
using PassingTrace.Events.Api.Ai.Assistant.Context;
using PassingTrace.Events.Api.Ai.Assistant.Presentation;
using PassingTrace.Events.Api.Ai.Capabilities;
using PassingTrace.Events.Api.Ai.Models;
using PassingTrace.Events.Api.Ai.Skills;
using PassingTrace.Events.Api.Ai.Tools.Mutations;
using PassingTrace.Events.Api.Ai.Tools.Queries;
using PassingTrace.Events.Api.Common;
using PassingTrace.Events.Api.Social;
using StackExchange.Redis;

namespace PassingTrace.Events.Api.Ai.Assistant;

public sealed partial class AssistantService(
    IAiConversationRepository repository,
    CurrentUserContext currentUser,
    PersonalRecordTools tools,
    AmapAiTools amapTools,
    IEnumerable<IAiCapabilityPackage> capabilityPackages,
    IChatClient chatClient,
    IConnectionMultiplexer redis,
    IOptions<AiModelOptions> aiOptions,
    ILoggerFactory loggerFactory,
    IServiceProvider services,
    TimeProvider clock,
    SocialAiTools? socialTools = null,
    PersonalMutationTools? mutationTools = null)
{
    public bool HasMutationOperations => mutationTools?.HasOperations == true;

    public async IAsyncEnumerable<AssistantStreamEvent> SendAsync(
        Guid conversationId,
        string content,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string? timezone = null)
    {
        content = content?.Trim() ?? string.Empty;
        if (content.Length == 0 || content.Length > 8000)
        {
            throw new AssistantMessageValidationException();
        }
        var conversation = await FindOwnedAsync(conversationId, cancellationToken);
        var now = clock.GetUtcNow();
        var calendar = AssistantCalendarContext.Create(now, timezone);
        tools.ConfigureCalendarContext(calendar, content);
        var watermark = await repository.ReadWatermarkAsync(currentUser.UserId, cancellationToken);
        var conversationContext = await ConversationContextSnapshot.LoadAsync(
            repository, currentUser.UserId, conversationId, long.MaxValue, now, cancellationToken);
        socialTools?.Configure(calendar, content, conversationContext.SharedFollowUp);
        amapTools.SeedCandidates(conversationContext.RecentAmapPlaces);
        var cacheKey = AssistantAnswerCache.BuildKey(
            currentUser.UserId, content, conversationContext.CacheValue, watermark, aiOptions.Value, calendar);
        var cache = redis.GetDatabase();
        var bypassCache = PersonalMutationTools.MayWrite(content) || AssistantNavigationPolicy.LooksLikeLiveAmapQuestion(content) || await repository.HasSocialHistoryAsync(currentUser.UserId, cancellationToken);
        var cached = bypassCache ? RedisValue.Null : await cache.StringGetAsync(cacheKey);

        var userMessage = new AiMessage
        {
            ConversationId = conversationId,
            UserId = currentUser.UserId,
            Role = AiMessageRole.User,
            Content = content,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
        };
        repository.Add(userMessage);
        conversation.UpdatedAt = now;
        await repository.SaveChangesAsync(cancellationToken);
        mutationTools?.Configure(conversationId, userMessage.Id, calendar, content);

        var cachedAnswer = AssistantAnswerCache.Read(cached);
        if (cachedAnswer is not null)
        {
            var value = cachedAnswer;
            await SaveAssistantAsync(conversation, value.Answer, value.Evidence, watermark, cancellationToken);
            yield return new AssistantStreamEvent("delta", new { text = value.Answer, cached = true });
            foreach (var action in value.Evidence.Actions ?? [])
                yield return new AssistantStreamEvent("action", action);
            yield return new AssistantStreamEvent("evidence", value.Evidence);
            yield return new AssistantStreamEvent("done", new { cached = true, watermark });
            yield break;
        }

        // Do not query personal records/memories just because a message was sent.
        // The model reads a scenario skill over MCP; a per-turn guard grants only that skill's tools.
        var skills = new AssistantSkillSession();
        var availablePackages = capabilityPackages.Where(package => package.IsAvailable).ToArray();
        await using var internalToolSession = await InternalMcpToolSession.CreateAsync(
            availablePackages.Where(package => package.UsesInternalMcp)
                .SelectMany(package => package.CreateTools()).Cast<AIFunction>()
                .Append(skills.CreateReader()), cancellationToken,
            availablePackages.SelectMany(package => package.WriteTools).ToHashSet(StringComparer.Ordinal));
        var functions = internalToolSession.Tools.Concat(availablePackages
            .Where(package => !package.UsesInternalMcp).SelectMany(package => package.CreateTools()))
            .Cast<AIFunction>().Select(function => (AITool)(function.Name == "ReadAssistantSkill"
                ? function : skills.Protect(function))).ToArray();
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "PassingTraceAssistantAgent",
            Description = "按场景聊天、检索和维护本人记录、计划、故事线，申请删除授权，以及查询高德地图",
            ChatOptions = new ChatOptions
            {
                Instructions = AssistantSkillCatalog.Instructions,
                Tools = functions,
                Temperature = 0.2f,
            },
            AIContextProviders =
            [
                new AssistantCalendarContextProvider(calendar),
            ],
            AllowConcurrentInvocation = false,
        }, loggerFactory, services);
        var session = await agent.CreateSessionAsync(cancellationToken);
        var answer = new StringBuilder();
        var promptMessages = conversationContext.BuildPromptMessages(content);
        await using var updates = agent.RunStreamingAsync(promptMessages, session, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool advanced = false;
            Exception? failure = null;
            try { advanced = await updates.MoveNextAsync(); }
            catch (Exception exception) { failure = exception; }
            // A tool may have committed even if the subsequent model request failed.
            var operationEvents = mutationTools?.DrainEvents() ?? [];
            foreach (var item in operationEvents) yield return item;
            // An approval ends this model turn. The separate decision endpoint handles the human response.
            if (operationEvents.Any(item => item.Type == "approval-request")) break;
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            if (!advanced) break;
            var update = updates.Current;
            AssistantStatisticsToolException.ThrowIfPresent(update.Contents);
            AssistantToolInvocationException.ThrowIfPresent(update.Contents);
            AssistantCompletionGuard.ThrowIfUnsuccessful(update.FinishReason);
            if (string.IsNullOrEmpty(update.Text)) continue;
            answer.Append(update.Text);
            yield return new AssistantStreamEvent("delta", new { text = update.Text, cached = false });
        }

        var finalAnswer = answer.ToString();
        if (string.IsNullOrWhiteSpace(finalAnswer) && mutationTools?.HasOperations == true)
        {
            finalAnswer = "操作结果已显示在聊天中；待删除的内容请在输入框上方确定或取消。";
            yield return new AssistantStreamEvent("delta", new { text = finalAnswer, replacement = true });
        }
        var personalEvidence = mutationTools?.Merge(tools.Snapshot) ?? tools.Snapshot;
        var intentText = AssistantNavigationPolicy.LooksLikeContextualFollowUp(content)
            ? string.Join('\n', conversationContext.RecentMessages
                .Where(message => message.Role == AiMessageRole.User)
                .TakeLast(4)
                .Select(message => message.Content)
                .Append(content))
            : content;
        var isNavigationRequest = AssistantNavigationPolicy.LooksLikeNavigationActionRequest(intentText);
        var isPersonalNavigationRequest = isNavigationRequest && AssistantNavigationPolicy.LooksLikePersonalHistoryPlaceRequest(intentText);
        if (isPersonalNavigationRequest && skills.Allows("GetNavigationTarget") && personalEvidence.NavigationTarget is null)
        {
            var locationId = tools.ResolvePreferredNavigationLocationId(finalAnswer);
            var navigationTool = functions.OfType<AIFunction>().FirstOrDefault(function => function.Name == "GetNavigationTarget");
            if (locationId.HasValue && navigationTool is not null)
            {
                // Fallbacks use the same skill guard, MCP validation and per-turn budget as model calls.
                await navigationTool.InvokeAsync(new() { ["locationId"] = locationId.Value }, cancellationToken);
                personalEvidence = mutationTools?.Merge(tools.Snapshot) ?? tools.Snapshot;
            }
        }
        var amapSnapshot = amapTools.Snapshot;
        if (isNavigationRequest && skills.Allows("CreateAmapNavigation") && personalEvidence.NavigationTarget is null &&
            amapSnapshot.Actions.Count == 0)
        {
            var candidate = amapTools.PreferredNavigationCandidate;
            var navigationTool = functions.OfType<AIFunction>().FirstOrDefault(function => function.Name == "CreateAmapNavigation");
            if (candidate is not null && navigationTool is not null)
            {
                await navigationTool.InvokeAsync(new() { ["candidateId"] = candidate.CandidateId }, cancellationToken);
                if (amapTools.Snapshot.Actions.Count > 0)
                {
                    const string confirmation = "\n\n已根据唯一候选生成高德导航入口，无需再次确认。";
                    finalAnswer += confirmation;
                    yield return new AssistantStreamEvent("delta", new { text = confirmation, cached = false });
                    amapSnapshot = amapTools.Snapshot;
                }
            }
        }
        var actions = (personalEvidence.NavigationTarget is null
                ? amapSnapshot.Actions
                : new[] { personalEvidence.NavigationTarget }.Concat(
                    amapSnapshot.Actions.Where(action => action.Type != "amap-navigation")))
            .DistinctBy(action => $"{action.Type}:{action.Latitude}:{action.Longitude}:{action.PlaceName}")
            .ToArray();
        var evidence = (socialTools?.Merge(personalEvidence) ?? personalEvidence) with
        {
            AmapPlaces = amapSnapshot.Places,
            Actions = actions,
            AmapResults = amapSnapshot.Results,
        };
        var presentedAnswer = AssistantAnswerPresenter.Present(finalAnswer, content, evidence, actions);
        if (!string.Equals(presentedAnswer, finalAnswer, StringComparison.Ordinal))
        {
            finalAnswer = presentedAnswer;
            yield return new AssistantStreamEvent("delta", new { text = finalAnswer, replacement = true, cached = false });
        }
        if (skills.NeedsEvidenceFallback(evidence))
        {
            finalAnswer = "我无法从你当前可检索的记录或记忆中找到足够证据，因此不作猜测。";
            yield return new AssistantStreamEvent("delta", new { text = finalAnswer, replacement = true });
        }
        AssistantCompletionGuard.ThrowIfEmpty(finalAnswer);
        if (mutationTools?.HasOperations == true) watermark = await repository.ReadWatermarkAsync(currentUser.UserId, cancellationToken);
        await SaveAssistantAsync(conversation, finalAnswer, evidence, watermark, cancellationToken);
        if (!bypassCache && mutationTools?.HasOperations != true && skills.CanCacheAnswer && !amapSnapshot.HasEvidence)
        {
            await cache.StringSetAsync(cacheKey,
                AssistantAnswerCache.Serialize(finalAnswer, evidence),
                TimeSpan.FromHours(24));
        }
        foreach (var action in evidence.Actions ?? [])
            yield return new AssistantStreamEvent("action", action);
        yield return new AssistantStreamEvent("evidence", evidence);
        yield return new AssistantStreamEvent("done", new { cached = false, watermark });
    }

}
