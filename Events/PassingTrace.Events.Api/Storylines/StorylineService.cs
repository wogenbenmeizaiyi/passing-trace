using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Storylines;

/// <summary>Web 完整保存与手机增量修改共用的故事线应用服务和 DAG 校验。</summary>
public sealed class StorylineService(TraceDbContext db, IAnalysisOutbox outbox, TimeProvider clock)
{
    public async Task<StorylineSaveResponse> CreateAsync(
        long userId,
        SaveStorylineRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await db.Storylines.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.CreationIdempotencyKey == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                var aggregate = await LoadAggregateAsync(userId, existing.Id, cancellationToken)
                    ?? throw new KeyNotFoundException("故事线不存在。");
                var revision = aggregate.Revisions.Single(x => x.Revision == aggregate.CurrentRevision);
                if (revision.ContentHash != Hash(request)) throw new IdempotencyConflictException(idempotencyKey);
                var planMapping = revision.Nodes
                    .Where(x => x.Event.EventKind == EventKind.Plan)
                    .ToDictionary(x => x.Key, x => x.EventId);
                return new StorylineSaveResponse(ToResponse(aggregate, revision), planMapping, null);
            }
        }

        var now = clock.GetUtcNow();
        var storyline = new Storyline
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = StorylineStatus.Ongoing,
            CurrentRevision = 0,
            CreationIdempotencyKey = NormalizeIdempotency(idempotencyKey),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Storylines.Add(storyline);
        return await SaveCoreAsync(storyline, request, NormalizeIdempotency(idempotencyKey), null, false, cancellationToken);
    }

    public async Task<StorylineSaveResponse> SaveAsync(
        long userId,
        Guid storylineId,
        uint expectedVersion,
        SaveStorylineRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var storyline = await LoadAggregateAsync(userId, storylineId, cancellationToken)
            ?? throw new KeyNotFoundException("故事线不存在。");
        EnsureVersion(storyline, expectedVersion);
        return await SaveCoreAsync(storyline, request, NormalizeIdempotency(idempotencyKey), storyline.CurrentRevision, false, cancellationToken);
    }

    public async Task<StorylineSaveResponse> ApplyChangeAsync(
        long userId,
        Guid storylineId,
        uint expectedVersion,
        StorylineChangeRequest change,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var storyline = await LoadAggregateAsync(userId, storylineId, cancellationToken)
            ?? throw new KeyNotFoundException("故事线不存在。");
        EnsureVersion(storyline, expectedVersion);
        var current = storyline.Revisions.Single(x => x.Revision == storyline.CurrentRevision);
        var request = ToSaveRequest(current);
        var stages = request.Stages!.ToList();
        var nodes = request.Nodes!.ToList();
        var edges = request.Edges!.ToList();
        var operation = change.Operation?.Trim().ToLowerInvariant();

        switch (operation)
        {
            case "add-existing-event":
            case "add-plan":
                {
                    var key = change.NodeKey is { } supplied && supplied != Guid.Empty ? supplied : Guid.NewGuid();
                    if (nodes.Any(x => x.Key == key)) throw new DomainValidationException("节点 Key 已存在。");
                    var newNode = operation == "add-plan"
                        ? new StorylineNodeInput(key, "new-plan", null, null,
                            change.NewPlan ?? throw new DomainValidationException("缺少轻量计划内容。"), change.StageKey,
                            change.SemanticOrder ?? NextOrder(nodes, change.StageKey), change.Emphasis ?? StorylineNodeEmphasis.Normal)
                        : new StorylineNodeInput(key, "existing-event",
                            change.EventId ?? throw new DomainValidationException("缺少记录 ID。"), change.SourceRevision, null,
                            change.StageKey, change.SemanticOrder ?? NextOrder(nodes, change.StageKey),
                            change.Emphasis ?? StorylineNodeEmphasis.Normal);
                    nodes.Add(newNode);
                    if (change.ParentNodeKey is { } parent)
                    {
                        if (nodes.All(x => x.Key != parent)) throw new DomainValidationException("前置节点不存在。");
                        edges.Add(new StorylineEdgeInput(Guid.NewGuid(), parent, key,
                            change.CreateBranch ? StorylineRelationType.Branch : StorylineRelationType.Sequence, null));
                    }
                    break;
                }
            case "sync-node":
                {
                    var index = FindNode(nodes, change.NodeKey);
                    nodes[index] = nodes[index] with { SourceRevision = null };
                    break;
                }
            case "move-node-to-stage":
                {
                    var index = FindNode(nodes, change.NodeKey);
                    nodes[index] = nodes[index] with
                    {
                        StageKey = change.StageKey,
                        SemanticOrder = change.SemanticOrder ?? NextOrder(nodes.Where((_, i) => i != index), change.StageKey),
                    };
                    break;
                }
            case "remove-node":
                {
                    var index = FindNode(nodes, change.NodeKey);
                    var key = nodes[index].Key;
                    if (edges.Any(x => x.SourceNodeKey == key))
                        throw new DomainValidationException("手机端只能直接移除叶子节点；复杂关系请在网页整理。");
                    nodes.RemoveAt(index);
                    edges.RemoveAll(x => x.SourceNodeKey == key || x.TargetNodeKey == key);
                    break;
                }
            case "remove-node-and-reconnect":
                {
                    var index = FindNode(nodes, change.NodeKey);
                    var key = nodes[index].Key;
                    var incoming = edges.Where(x => x.TargetNodeKey == key).ToArray();
                    var outgoing = edges.Where(x => x.SourceNodeKey == key).ToArray();
                    if (incoming.Length != 1 || outgoing.Length != 1)
                        throw new DomainValidationException("只有单入边、单出边节点可以在手机端移除并连接前后。");
                    edges.RemoveAll(x => x.SourceNodeKey == key || x.TargetNodeKey == key);
                    edges.Add(new StorylineEdgeInput(Guid.NewGuid(), incoming[0].SourceNodeKey, outgoing[0].TargetNodeKey,
                        StorylineRelationType.Sequence, null));
                    nodes.RemoveAt(index);
                    break;
                }
            case "update-metadata":
                request = request with
                {
                    Title = change.Title ?? request.Title,
                    Description = change.Description ?? request.Description,
                    CategoryKey = change.CategoryKey ?? request.CategoryKey,
                    Status = change.Status ?? request.Status,
                    Tags = change.Tags ?? request.Tags,
                };
                break;
            default:
                throw new DomainValidationException("不支持的手机故事线操作。");
        }

        request = request with { Stages = stages, Nodes = nodes, Edges = edges };
        return await SaveCoreAsync(storyline, request, NormalizeIdempotency(idempotencyKey), current.Revision, true, cancellationToken);
    }

    public async Task<IReadOnlyList<StorylineSummaryResponse>> ListAsync(
        long userId, StorylineStatus? status, string? categoryKey, DateTimeOffset? from, DateTimeOffset? to,
        int limit, Guid? cursor, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 100);
        var query = db.Storylines.AsNoTracking().Where(x => x.UserId == userId && x.DeletedAt == null);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(categoryKey)) query = query.Where(x => x.CategoryKey == categoryKey);
        if (from is not null) query = query.Where(x => x.RangeEnd == null || x.RangeEnd >= from.Value.ToUniversalTime());
        if (to is not null) query = query.Where(x => x.RangeStart == null || x.RangeStart <= to.Value.ToUniversalTime());
        if (cursor is not null) query = query.Where(x => x.Id.CompareTo(cursor.Value) < 0);
        var rows = await query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id).Take(limit).ToListAsync(cancellationToken);
        var ids = rows.Select(x => x.Id).ToArray();
        var revisions = await db.StorylineRevisions.AsNoTracking().Include(x => x.Nodes).Include(x => x.Tags)
            .Where(x => ids.Contains(x.StorylineId)).ToListAsync(cancellationToken);
        return rows.Select(x =>
        {
            var revision = revisions.Single(r => r.StorylineId == x.Id && r.Revision == x.CurrentRevision);
            return ToSummary(x, revision);
        }).ToArray();
    }

    public async Task<StorylineRevisionResponse> GetAsync(
        long userId, Guid storylineId, int? revisionNumber, CancellationToken cancellationToken)
    {
        var storyline = await LoadAggregateAsync(userId, storylineId, cancellationToken)
            ?? throw new KeyNotFoundException("故事线不存在。");
        var revision = storyline.Revisions.SingleOrDefault(x => x.Revision == (revisionNumber ?? storyline.CurrentRevision))
            ?? throw new KeyNotFoundException("故事线修订不存在。");
        return ToResponse(storyline, revision);
    }

    public async Task<IReadOnlyList<StorylineRevisionHistoryResponse>> RevisionsAsync(
        long userId, Guid storylineId, CancellationToken cancellationToken)
    {
        var storyline = await db.Storylines.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == storylineId && x.UserId == userId && x.DeletedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("故事线不存在。");
        return await db.StorylineRevisions.AsNoTracking().Where(x => x.StorylineId == storylineId)
            .OrderByDescending(x => x.Revision)
            .Select(x => new StorylineRevisionHistoryResponse(x.Revision, x.ContentHash, x.LayoutState,
                x.Nodes.Count, x.CreatedAt, x.Revision == storyline.CurrentRevision)).ToListAsync(cancellationToken);
    }

    public async Task<StorylineSaveResponse> RestoreAsync(
        long userId, Guid storylineId, int revision, uint expectedVersion, string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var storyline = await LoadAggregateAsync(userId, storylineId, cancellationToken)
            ?? throw new KeyNotFoundException("故事线不存在。");
        EnsureVersion(storyline, expectedVersion);
        var source = storyline.Revisions.SingleOrDefault(x => x.Revision == revision)
            ?? throw new KeyNotFoundException("故事线修订不存在。");
        return await SaveCoreAsync(storyline, ToSaveRequest(source), NormalizeIdempotency(idempotencyKey),
            storyline.CurrentRevision, true, cancellationToken);
    }

    public async Task DeleteAsync(long userId, Guid storylineId, uint expectedVersion, CancellationToken cancellationToken)
    {
        var storyline = await db.Storylines.FirstOrDefaultAsync(x => x.Id == storylineId && x.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("故事线不存在。");
        EnsureVersion(storyline, expectedVersion);
        if (storyline.DeletedAt is not null) return;
        var now = clock.GetUtcNow();
        storyline.DeletedAt = now; storyline.UpdatedAt = now;
        foreach (var index in await db.StorylineSearchIndexes.Where(x => x.StorylineId == storylineId && x.IsCurrent).ToListAsync(cancellationToken))
            index.IsCurrent = false;
        outbox.EnqueueStoryline(userId, storylineId, storyline.CurrentRevision, now, "storyline.removed");
        await outbox.IncrementWatermarkAsync(userId, now, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    private async Task<StorylineSaveResponse> SaveCoreAsync(
        Storyline storyline, SaveStorylineRequest request, string? idempotencyKey, int? undoRevision,
        bool preserveLayout, CancellationToken cancellationToken)
    {
        ValidateMetadata(request);
        var requestHash = Hash(request);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var prior = storyline.Revisions.FirstOrDefault(x => x.IdempotencyKey == idempotencyKey);
            if (prior is not null)
            {
                if (prior.ContentHash != requestHash) throw new IdempotencyConflictException(idempotencyKey);
                var planMapping = prior.Nodes
                    .Where(x => x.Event.EventKind == EventKind.Plan)
                    .ToDictionary(x => x.Key, x => x.EventId);
                return new StorylineSaveResponse(ToResponse(storyline, prior), planMapping, undoRevision);
            }
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var now = clock.GetUtcNow();
            var createdPlans = new Dictionary<Guid, Event>();
            var nodes = (request.Nodes ?? []).ToList();
            var existingIds = nodes.Where(x => x.NodeType == "existing-event" && x.EventId.HasValue).Select(x => x.EventId!.Value).Distinct().ToArray();
            var events = await db.Events
                .Include(x => x.SourceRevisions).ThenInclude(x => x.MediaAssets).ThenInclude(x => x.MediaAsset)
                .Include(x => x.LabelIndexes).Include(x => x.Locations)
                .Where(x => x.UserId == storyline.UserId && existingIds.Contains(x.Id)).AsSplitQuery().ToListAsync(cancellationToken);
            if (events.Count != existingIds.Length || events.Any(x => x.DeletedAt is not null))
                throw new DomainValidationException("故事线包含不存在、已删除或不属于当前用户的记录。");

            foreach (var input in nodes.Where(x => x.NodeType == "new-plan"))
            {
                var plan = CreateInlinePlan(storyline.UserId, storyline.Id, input, idempotencyKey, now);
                db.Events.Add(plan);
                createdPlans[input.Key] = plan;
                events.Add(plan);
            }

            var resolved = ResolveNodes(nodes, events, createdPlans);
            ValidateGraph(request.Stages ?? [], resolved, request.Edges ?? [], request.Status);
            var range = DeriveRange(resolved);
            var cover = ResolveCover(request.CoverMediaAssetId, resolved);
            var tags = BuildTags(request.Tags ?? [], resolved);
            var nextRevision = storyline.CurrentRevision + 1;
            var revision = new StorylineRevision
            {
                Storyline = storyline,
                Revision = nextRevision,
                IdempotencyKey = idempotencyKey,
                ContentHash = requestHash,
                Title = request.Title.Trim(),
                Description = NormalizeDescription(request.Description),
                CategoryKey = request.CategoryKey.Trim().ToLowerInvariant(),
                Status = request.Status,
                CoverMediaAssetId = cover,
                RangeStart = range.Start,
                RangeEnd = range.End,
                LayoutState = StorylineLayoutState.Arranged,
                CreatedAt = now,
            };
            revision.Stages.AddRange((request.Stages ?? []).OrderBy(x => x.SemanticOrder).Select(x => new StorylineStage
            { Key = x.Key, Title = x.Title.Trim(), SemanticOrder = x.SemanticOrder }));
            revision.Nodes.AddRange(resolved.Select(x => new StorylineNode
            {
                Key = x.Input.Key,
                Event = x.Event,
                SourceRevision = x.Source.Revision,
                StageKey = x.Input.StageKey,
                SemanticOrder = x.Input.SemanticOrder,
                Emphasis = x.Input.Emphasis,
            }));
            revision.Edges.AddRange((request.Edges ?? []).Select(x => new StorylineEdge
            {
                Key = x.Key == Guid.Empty ? Guid.NewGuid() : x.Key,
                SourceNodeKey = x.SourceNodeKey,
                TargetNodeKey = x.TargetNodeKey,
                RelationType = x.RelationType,
                Label = Limit(x.Label, 120),
            }));
            revision.Tags.AddRange(tags);

            var layoutInput = request.WebCanvasLayout;
            if (layoutInput is null && preserveLayout && storyline.CurrentRevision > 0)
                layoutInput = CopyLayout(storyline.Revisions.Single(x => x.Revision == storyline.CurrentRevision));
            if (layoutInput is not null)
            {
                revision.WebLayout = BuildLayout(layoutInput, resolved.Select(x => x.Input.Key).ToHashSet(),
                    revision.Stages.Select(x => x.Key).ToHashSet());
                if (resolved.Any(x => revision.WebLayout.Nodes.All(p => p.NodeKey != x.Input.Key)))
                    revision.LayoutState = StorylineLayoutState.NeedsArrangement;
            }
            else if (resolved.Count > 0)
            {
                revision.LayoutState = StorylineLayoutState.NeedsArrangement;
            }

            storyline.Title = revision.Title; storyline.Description = revision.Description; storyline.CategoryKey = revision.CategoryKey;
            storyline.Status = revision.Status; storyline.CurrentRevision = nextRevision; storyline.CoverMediaAssetId = cover;
            storyline.RangeStart = range.Start; storyline.RangeEnd = range.End; storyline.UpdatedAt = now;
            storyline.Revisions.Add(revision);
            foreach (var index in storyline.SearchIndexes.Where(x => x.IsCurrent)) index.IsCurrent = false;
            storyline.SearchIndexes.Add(new StorylineSearchIndex
            {
                Storyline = storyline,
                UserId = storyline.UserId,
                Revision = nextRevision,
                RetrievalText = BuildRetrieval(revision, resolved, tags),
                IsCurrent = true,
                UpdatedAt = now,
            });
            outbox.EnqueueStoryline(storyline.UserId, storyline.Id, nextRevision, now);
            await outbox.IncrementWatermarkAsync(storyline.UserId, now, cancellationToken);
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new StorylineSaveResponse(ToResponse(storyline, revision),
                createdPlans.ToDictionary(x => x.Key, x => x.Value.Id), undoRevision);
        });
    }

    private Event CreateInlinePlan(long userId, Guid storylineId, StorylineNodeInput input, string? operationKey, DateTimeOffset now)
    {
        var planInput = input.NewPlan ?? throw new DomainValidationException("临时计划缺少内容。");
        var title = planInput.Title?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > 512) throw new DomainValidationException("轻量计划标题长度必须为 1 到 512 个字符。");
        var planKey = $"storyline:{storylineId:N}:{operationKey ?? "save"}:{input.Key:N}";
        if (planKey.Length > 64) planKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(planKey))).ToLowerInvariant();
        var evt = Event.Create(userId, EventKind.Plan, title, Limit(planInput.RawContent, 2000), null,
            planInput.PlannedAt?.ToUniversalTime(), string.IsNullOrWhiteSpace(planInput.Timezone) ? "UTC" : planInput.Timezone,
            planKey, now);
        var source = SourceRevision.Create(0, 1, evt.Title, evt.RawContent, null, evt.PlannedAt, now);
        evt.SourceRevisions.Add(source);
        evt.SearchIndexes.Add(new EventSearchIndex
        {
            UserId = userId,
            SourceRevision = 1,
            Title = title,
            RawContent = evt.RawContent ?? string.Empty,
            RetrievalText = string.Join('\n', new[] { title, evt.RawContent }.Where(x => !string.IsNullOrWhiteSpace(x))),
            IsCurrent = true,
            UpdatedAt = now,
        });
        outbox.EnqueueEvent(evt, 1, now);
        return evt;
    }

    private static List<ResolvedNode> ResolveNodes(
        IReadOnlyList<StorylineNodeInput> inputs, IReadOnlyList<Event> events, IReadOnlyDictionary<Guid, Event> plans)
    {
        var result = new List<ResolvedNode>();
        foreach (var input in inputs)
        {
            Event evt;
            if (input.NodeType == "new-plan") evt = plans[input.Key];
            else if (input.NodeType == "existing-event" && input.EventId.HasValue)
                evt = events.SingleOrDefault(x => x.Id == input.EventId.Value)
                    ?? throw new DomainValidationException("节点引用的记录不存在。");
            else throw new DomainValidationException("nodeType 只能为 existing-event 或 new-plan。");
            var source = input.NodeType == "new-plan"
                ? evt.SourceRevisions.Single()
                : evt.SourceRevisions.SingleOrDefault(x => x.Revision == (input.SourceRevision ?? evt.CurrentSourceRevision))
                    ?? throw new DomainValidationException($"记录 {evt.Id} 的固定修订不存在。");
            result.Add(new ResolvedNode(input, evt, source));
        }
        return result;
    }

    private static void ValidateMetadata(SaveStorylineRequest request)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > 120) throw new DomainValidationException("故事线标题长度必须为 1 到 120 个字符。");
        if ((request.Description?.Trim().Length ?? 0) > 2000) throw new DomainValidationException("故事线说明最多 2000 个字符。");
        var category = request.CategoryKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!StorylineTaxonomy.IsCategory(category)) throw new DomainValidationException("未知的故事线主分类。");
        if ((request.Tags?.Count ?? 0) > 10) throw new DomainValidationException("故事线最多 10 个自定义标签。");
    }

    private static void ValidateGraph(
        IReadOnlyList<StorylineStageInput> stages, IReadOnlyList<ResolvedNode> nodes,
        IReadOnlyList<StorylineEdgeInput> edges, StorylineStatus status)
    {
        if (stages.Count > 50 || nodes.Count > 500 || edges.Count > 1000)
            throw new DomainValidationException("故事线超过 50 个阶段、500 个节点或 1000 条连线的上限。");
        if (stages.Any(x => x.Key == Guid.Empty) || stages.Select(x => x.Key).Distinct().Count() != stages.Count)
            throw new DomainValidationException("阶段 Key 不能为空或重复。");
        if (stages.Any(x => string.IsNullOrWhiteSpace(x.Title) || x.Title.Trim().Length > 120))
            throw new DomainValidationException("阶段标题长度必须为 1 到 120 个字符。");
        if (nodes.Any(x => x.Input.Key == Guid.Empty) || nodes.Select(x => x.Input.Key).Distinct().Count() != nodes.Count)
            throw new DomainValidationException("节点 Key 不能为空或重复。");
        if (nodes.Where(x => x.Event.Id > 0).GroupBy(x => x.Event.Id).Any(x => x.Count() > 1))
            throw new DomainValidationException("同一条记录在同一故事线修订中只能出现一次。");
        var stageKeys = stages.Select(x => x.Key).ToHashSet();
        if (nodes.Any(x => x.Input.StageKey.HasValue && !stageKeys.Contains(x.Input.StageKey.Value)))
            throw new DomainValidationException("节点引用了不存在的阶段。");
        var keys = nodes.Select(x => x.Input.Key).ToHashSet();
        if (edges.Any(x => x.Key == Guid.Empty || !keys.Contains(x.SourceNodeKey) || !keys.Contains(x.TargetNodeKey)))
            throw new DomainValidationException("连线 Key 或端点无效。");
        if (edges.Any(x => x.SourceNodeKey == x.TargetNodeKey)) throw new DomainValidationException("故事线不允许自环。");
        if (edges.GroupBy(x => new { x.SourceNodeKey, x.TargetNodeKey, x.RelationType }).Any(x => x.Count() > 1))
            throw new DomainValidationException("故事线不允许重复连线。");
        Topological(keys, edges);
        if (status == StorylineStatus.Completed && keys.Count > 0 && !IsWeaklyConnected(keys, edges))
            throw new DomainValidationException("已完成故事线的所有节点必须属于同一个连通图。");
    }

    private static IReadOnlyList<Guid> Topological(HashSet<Guid> keys, IReadOnlyList<StorylineEdgeInput> edges)
    {
        var incoming = keys.ToDictionary(x => x, _ => 0);
        var outgoing = keys.ToDictionary(x => x, _ => new List<Guid>());
        foreach (var edge in edges) { incoming[edge.TargetNodeKey]++; outgoing[edge.SourceNodeKey].Add(edge.TargetNodeKey); }
        var queue = new PriorityQueue<Guid, string>();
        foreach (var key in keys.Where(x => incoming[x] == 0)) queue.Enqueue(key, key.ToString("N"));
        var result = new List<Guid>();
        while (queue.TryDequeue(out var key, out _))
        {
            result.Add(key);
            foreach (var target in outgoing[key]) if (--incoming[target] == 0) queue.Enqueue(target, target.ToString("N"));
        }
        if (result.Count != keys.Count) throw new DomainValidationException("故事线不能形成循环。");
        return result;
    }

    private static bool IsWeaklyConnected(HashSet<Guid> keys, IReadOnlyList<StorylineEdgeInput> edges)
    {
        if (keys.Count < 2) return true;
        var adjacent = keys.ToDictionary(x => x, _ => new List<Guid>());
        foreach (var edge in edges) { adjacent[edge.SourceNodeKey].Add(edge.TargetNodeKey); adjacent[edge.TargetNodeKey].Add(edge.SourceNodeKey); }
        var seen = new HashSet<Guid>(); var queue = new Queue<Guid>(); queue.Enqueue(keys.First());
        while (queue.TryDequeue(out var key)) if (seen.Add(key)) foreach (var next in adjacent[key]) queue.Enqueue(next);
        return seen.Count == keys.Count;
    }

    private static List<StorylineRevisionTag> BuildTags(
        IReadOnlyList<string> manualTags, IReadOnlyList<ResolvedNode> nodes)
    {
        var result = new List<StorylineRevisionTag>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var order = 0;
        foreach (var raw in manualTags)
        {
            var display = EventTaxonomy.NormalizeCustomTag(raw);
            var normalized = EventTaxonomy.NormalizedValue(display);
            if (seen.Add(normalized)) result.Add(new StorylineRevisionTag
            { Origin = StorylineTagOrigin.Manual, DisplayName = display, NormalizedValue = normalized, SortOrder = order++ });
        }
        var derived = nodes
            .SelectMany(node => node.Event.LabelIndexes.Where(label =>
                label.SourceRevision == node.Source.Revision && label.Type == EventLabelType.BehaviorTag))
            .GroupBy(x => new { x.TaxonomyKey, x.DisplayName, x.NormalizedValue }).OrderByDescending(x => x.Count()).Take(20)
            .Select(x => x.Key).ToList();
        foreach (var tag in derived)
        {
            if (result.Count >= 10) break;
            if (seen.Add(tag.NormalizedValue)) result.Add(new StorylineRevisionTag
            {
                Origin = StorylineTagOrigin.Derived,
                TaxonomyKey = tag.TaxonomyKey,
                DisplayName = tag.DisplayName,
                NormalizedValue = tag.NormalizedValue,
                SortOrder = order++
            });
        }
        return result;
    }

    private static (DateTimeOffset? Start, DateTimeOffset? End) DeriveRange(IReadOnlyList<ResolvedNode> nodes)
    {
        var times = nodes.Select(x => x.Source.HappenedAt ?? x.Source.PlannedAt).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return times.Length == 0 ? (null, null) : (times.Min(), times.Max());
    }

    private static Guid? ResolveCover(Guid? requested, IReadOnlyList<ResolvedNode> nodes)
    {
        var images = nodes.SelectMany(x => x.Source.MediaAssets)
            .Where(x => x.MediaAsset.Kind == MediaKind.Image && x.MediaAsset.Status == MediaAssetStatus.Ready)
            .Select(x => x.MediaAssetId).ToHashSet();
        if (requested.HasValue && !images.Contains(requested.Value))
            throw new DomainValidationException("封面必须来自当前故事线节点的图片附件。");
        if (requested.HasValue) return requested;
        var first = images.FirstOrDefault();
        return first == Guid.Empty ? null : first;
    }

    private static StorylineWebLayout BuildLayout(StorylineWebLayoutInput input, HashSet<Guid> nodeKeys, HashSet<Guid> stageKeys)
    {
        var direction = input.Direction?.ToUpperInvariant() is "TB" ? "TB" : "LR";
        var layout = new StorylineWebLayout
        { Direction = direction, ViewportX = input.ViewportX, ViewportY = input.ViewportY, Zoom = Math.Clamp(input.Zoom, 0.1m, 4m) };
        foreach (var node in input.Nodes ?? []) if (nodeKeys.Contains(node.NodeKey)) layout.Nodes.Add(new StorylineWebNodeLayout
        { NodeKey = node.NodeKey, X = node.X, Y = node.Y, Width = node.Width, Height = node.Height });
        foreach (var stage in input.Stages ?? []) if (stageKeys.Contains(stage.StageKey)) layout.Stages.Add(new StorylineWebStageLayout
        { StageKey = stage.StageKey, X = stage.X, Y = stage.Y, Width = stage.Width, Height = stage.Height });
        return layout;
    }

    private static StorylineWebLayoutInput? CopyLayout(StorylineRevision revision) => revision.WebLayout is null ? null : new(
        revision.WebLayout.Direction, revision.WebLayout.ViewportX, revision.WebLayout.ViewportY, revision.WebLayout.Zoom,
        revision.WebLayout.Nodes.Select(x => new StorylineWebNodeLayoutInput(x.NodeKey, x.X, x.Y, x.Width, x.Height)).ToArray(),
        revision.WebLayout.Stages.Select(x => new StorylineWebStageLayoutInput(x.StageKey, x.X, x.Y, x.Width, x.Height)).ToArray());

    private static SaveStorylineRequest ToSaveRequest(StorylineRevision revision) => new(
        revision.Title, revision.Description, revision.CategoryKey, revision.Status, revision.CoverMediaAssetId,
        revision.Tags.Where(x => x.Origin == StorylineTagOrigin.Manual).OrderBy(x => x.SortOrder).Select(x => x.DisplayName).ToArray(),
        revision.Stages.OrderBy(x => x.SemanticOrder).Select(x => new StorylineStageInput(x.Key, x.Title, x.SemanticOrder)).ToArray(),
        revision.Nodes.Select(x => new StorylineNodeInput(x.Key, "existing-event", x.EventId, x.SourceRevision, null,
            x.StageKey, x.SemanticOrder, x.Emphasis)).ToArray(),
        revision.Edges.Select(x => new StorylineEdgeInput(x.Key, x.SourceNodeKey, x.TargetNodeKey, x.RelationType, x.Label)).ToArray(),
        CopyLayout(revision));

    private StorylineRevisionResponse ToResponse(Storyline storyline, StorylineRevision revision)
    {
        var edges = revision.Edges.Select(x => new StorylineEdgeInput(x.Key, x.SourceNodeKey, x.TargetNodeKey, x.RelationType, x.Label)).ToArray();
        var keys = revision.Nodes.Select(x => x.Key).ToHashSet();
        var topo = Topological(keys, edges); var positions = topo.Select((x, i) => (x, i)).ToDictionary(x => x.x, x => x.i);
        var depths = new Dictionary<Guid, int>();
        foreach (var key in topo) depths[key] = revision.Edges.Where(x => x.TargetNodeKey == key)
            .Select(x => depths.GetValueOrDefault(x.SourceNodeKey) + 1).DefaultIfEmpty(0).Max();
        var nodes = revision.Nodes.Select(x =>
        {
            var source = x.Event.SourceRevisions.Single(r => r.Revision == x.SourceRevision);
            var currentLabels = x.Event.LabelIndexes.Where(l => l.IsCurrent).OrderBy(l => l.Type).Select(l => l.DisplayName).Take(3).ToArray();
            var place = x.Event.Locations.FirstOrDefault(l => l.SourceRevision == x.SourceRevision)?.Name;
            var image = source.MediaAssets.FirstOrDefault(m => m.MediaAsset.Kind == MediaKind.Image)?.MediaAssetId;
            var state = x.Event.DeletedAt is not null ? "deleted" : x.Event.CurrentSourceRevision == x.SourceRevision ? "upToDate" : "updated";
            return new StorylineNodeResponse(x.Key, x.EventId, x.SourceRevision, x.Event.CurrentSourceRevision, state,
                x.Event.EventKind, x.Event.Status, source.Title ?? "无标题记录", source.RawContent,
                source.HappenedAt ?? source.PlannedAt, x.StageKey, x.SemanticOrder, x.Emphasis, place, currentLabels, image);
        }).ToArray();
        var outline = topo.Select(key => new StorylineOutlineNodeResponse(key,
            revision.Nodes.Single(x => x.Key == key).StageKey, positions[key], depths[key],
            revision.Edges.Count(x => x.TargetNodeKey == key), revision.Edges.Count(x => x.SourceNodeKey == key),
            revision.Edges.Count(x => x.SourceNodeKey == key) > 1,
            revision.Edges.Count(x => x.TargetNodeKey == key) > 1)).ToArray();
        return new StorylineRevisionResponse(storyline.Id, revision.Title, revision.Description, revision.CategoryKey,
            StorylineTaxonomy.Label(revision.CategoryKey), revision.Status, revision.Revision, storyline.RowVersion,
            revision.CoverMediaAssetId, revision.RangeStart, revision.RangeEnd, revision.LayoutState,
            revision.Tags.OrderBy(x => x.SortOrder).Select(x => x.DisplayName).ToArray(),
            revision.Stages.OrderBy(x => x.SemanticOrder).Select(x => new StorylineStageInput(x.Key, x.Title, x.SemanticOrder)).ToArray(),
            nodes, edges, outline, CopyLayout(revision), storyline.UpdatedAt);
    }

    private static StorylineSummaryResponse ToSummary(Storyline storyline, StorylineRevision revision) => new(
        storyline.Id, storyline.Title, storyline.Description, storyline.CategoryKey, StorylineTaxonomy.Label(storyline.CategoryKey),
        storyline.Status, storyline.CurrentRevision, storyline.RowVersion, storyline.CoverMediaAssetId,
        storyline.RangeStart, storyline.RangeEnd, revision.Nodes.Count,
        revision.Tags.OrderBy(x => x.SortOrder).Select(x => x.DisplayName).Take(4).ToArray(), revision.LayoutState, storyline.UpdatedAt);

    private async Task<Storyline?> LoadAggregateAsync(long userId, Guid id, CancellationToken cancellationToken) =>
        await db.Storylines
            .Include(x => x.SearchIndexes)
            .Include(x => x.Revisions).ThenInclude(x => x.Stages)
            .Include(x => x.Revisions).ThenInclude(x => x.Edges)
            .Include(x => x.Revisions).ThenInclude(x => x.Tags)
            .Include(x => x.Revisions).ThenInclude(x => x.WebLayout).ThenInclude(x => x!.Nodes)
            .Include(x => x.Revisions).ThenInclude(x => x.WebLayout).ThenInclude(x => x!.Stages)
            .Include(x => x.Revisions).ThenInclude(x => x.Nodes).ThenInclude(x => x.Event).ThenInclude(x => x.SourceRevisions)
                .ThenInclude(x => x.MediaAssets).ThenInclude(x => x.MediaAsset)
            .Include(x => x.Revisions).ThenInclude(x => x.Nodes).ThenInclude(x => x.Event).ThenInclude(x => x.LabelIndexes)
            .Include(x => x.Revisions).ThenInclude(x => x.Nodes).ThenInclude(x => x.Event).ThenInclude(x => x.Locations)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId && x.DeletedAt == null, cancellationToken);

    private static string BuildRetrieval(StorylineRevision revision, IReadOnlyList<ResolvedNode> nodes,
        IReadOnlyList<StorylineRevisionTag> tags) => string.Join('\n', new[]
        { revision.Title, revision.Description, StorylineTaxonomy.Label(revision.CategoryKey) }
        .Concat(revision.Stages.OrderBy(x => x.SemanticOrder).Select(x => x.Title))
        .Concat(tags.Select(x => x.DisplayName))
        .Concat(nodes.OrderBy(x => x.Input.SemanticOrder).Select(x => x.Source.Title))
        .Where(x => !string.IsNullOrWhiteSpace(x))!);

    private static int FindNode(List<StorylineNodeInput> nodes, Guid? key)
    {
        if (key is null) throw new DomainValidationException("缺少节点 Key。");
        var index = nodes.FindIndex(x => x.Key == key.Value);
        return index >= 0 ? index : throw new DomainValidationException("故事线节点不存在。");
    }

    private static int NextOrder(IEnumerable<StorylineNodeInput> nodes, Guid? stageKey) =>
        nodes.Where(x => x.StageKey == stageKey).Select(x => x.SemanticOrder).DefaultIfEmpty(-1).Max() + 1;
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))).ToLowerInvariant();
    private static string? NormalizeIdempotency(string? value) => string.IsNullOrWhiteSpace(value) ? null : Limit(value, 128);
    private static string? NormalizeDescription(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Limit(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(max, value.Trim().Length)];
    private static void EnsureVersion(Storyline storyline, uint version)
    {
        if (storyline.RowVersion != version) throw new ConcurrencyException("故事线已在其他设备更新，请刷新后重试。");
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyException("故事线已在其他设备更新，请刷新后重试。"); }
    }

    private sealed record ResolvedNode(StorylineNodeInput Input, Event Event, SourceRevision Source);
}
