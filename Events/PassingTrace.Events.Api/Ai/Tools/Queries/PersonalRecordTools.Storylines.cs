using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Ai.Evidence;

namespace PassingTrace.Events.Api.Ai.Tools.Queries;

public sealed partial class PersonalRecordTools
{
    [Description("搜索当前登录用户自己的故事线，适合旅行过程、项目阶段、活动纪实和生命周期问题；不接受 userId。")]
    public async Task<IReadOnlyList<StorylineEvidence>> SearchMyStorylinesAsync(
        [MaxLength(8000), Description("自然语言关键词或问题")] string query,
        [Description("故事线主分类 key，可空")] string? category = null,
        [RegularExpression("^(Ongoing|Completed)$"), Description("Ongoing 或 Completed，可空")] string? status = null,
        [Range(1, 10), Description("最多返回 1-10 条")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 10);
        var userId = currentUser.UserId;
        query = query?.Trim() ?? string.Empty;
        var filter = new StorylineSearchFilter(query, string.IsNullOrWhiteSpace(category) ? null : category.Trim().ToLowerInvariant(),
            Enum.TryParse<StorylineStatus>(status, true, out var parsedStatus) ? parsedStatus : null);
        var scores = new Dictionary<Guid, double>();
        AddRanking(scores, await queries.RankStorylinesAsync(userId, filter, AiSearchOrder.Recent, null, cancellationToken));
        if (query.Length > 0)
        {
            AddRanking(scores, await queries.RankStorylinesAsync(userId, filter, AiSearchOrder.Text, null, cancellationToken));
            try
            {
                var generated = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
                AddRanking(scores, await queries.RankStorylinesAsync(userId, filter, AiSearchOrder.Vector, generated[0].Vector.ToArray(), cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
        }
        var ids = scores.OrderByDescending(x => x.Value).Take(limit).Select(x => x.Key).ToArray();
        var matches = await queries.ReadStorylinesAsync(userId, ids, cancellationToken);
        var evidence = ids.Where(id => matches.Any(x => x.Storyline.Id == id)).Select(id =>
        {
            var match = matches.Single(x => x.Storyline.Id == id);
            var story = match.Storyline;
            var revision = match.Revision;
            var stages = revision.Stages.OrderBy(x => x.SemanticOrder).Select(stage => new StorylineStageEvidence(
                stage.Key, stage.Title, revision.Nodes.Where(x => x.StageKey == stage.Key).OrderBy(x => x.SemanticOrder)
                    .Select(x => PinnedSource(x).Title ?? "无标题记录").ToArray())).ToArray();
            return new StorylineEvidence(id, revision.Revision, story.Title, StorylineTaxonomy.Label(story.CategoryKey),
                story.Status.ToString(), Snippet(match.RetrievalText, query), story.RangeStart, story.RangeEnd, stages,
                revision.Nodes.Where(x => x.Event.DeletedAt == null).Select(x => x.EventId).Distinct().ToArray(), scores[id]);
        }).ToArray();
        foreach (var item in evidence) { _storylineEvidence.Add(item); _retrievedStorylineIds.Add(item.StorylineId); }
        return evidence;
    }

    [Description("读取本轮已经检索到的故事线阶段、拓扑关系和固定记录修订证据。")]
    public async Task<object?> GetMyStorylineEvidenceAsync(Guid storylineId, CancellationToken cancellationToken = default)
    {
        if (!_retrievedStorylineIds.Contains(storylineId)) return null;
        var userId = currentUser.UserId;
        var revision = await queries.FindStorylineRevisionAsync(userId, storylineId, cancellationToken);
        if (revision is null) return null;
        var story = revision.Storyline;
        return new
        {
            storylineId,
            revision = revision.Revision,
            story.Title,
            stages = revision.Stages.OrderBy(x => x.SemanticOrder).Select(x => new { x.Key, x.Title, x.SemanticOrder }),
            nodes = revision.Nodes.OrderBy(x => x.SemanticOrder).Where(x => x.Event.DeletedAt == null)
                .Select(x => new
                {
                    x.Key,
                    x.EventId,
                    x.SourceRevision,
                    title = PinnedSource(x).Title,
                    rawContent = PinnedSource(x).RawContent,
                    occurredAt = PinnedSource(x).HappenedAt ?? PinnedSource(x).PlannedAt,
                    kind = x.Event.EventKind.ToString(),
                    status = x.Event.Status.ToString(),
                    x.StageKey,
                }),
            edges = revision.Edges.Select(x => new { x.SourceNodeKey, x.TargetNodeKey, relation = x.RelationType.ToString(), x.Label }),
        };
    }

    private static SourceRevision PinnedSource(StorylineNode node) =>
        node.Event.SourceRevisions.Single(x => x.Revision == node.SourceRevision);

    private static void AddRanking(Dictionary<Guid, double> scores, IReadOnlyList<Guid> ranking)
    {
        for (var rank = 0; rank < ranking.Count; rank++)
            scores[ranking[rank]] = scores.GetValueOrDefault(ranking[rank]) + 1d / (61 + rank);
    }
}
