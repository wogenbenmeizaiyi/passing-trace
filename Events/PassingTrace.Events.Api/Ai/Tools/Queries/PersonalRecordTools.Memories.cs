using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai.Evidence;

namespace PassingTrace.Events.Api.Ai.Tools.Queries;

public sealed partial class PersonalRecordTools
{
    [Description("搜索当前用户有证据的长期记忆。拒绝状态不会返回；不接受 userId。")]
    public async Task<IReadOnlyList<MemoryEvidence>> SearchMyMemoriesAsync(
        [MaxLength(8000)] string query,
        [Range(1, 10)] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId;
        limit = Math.Clamp(limit, 1, 10);
        IReadOnlyList<UserMemory> result;
        try
        {
            var generated = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
            result = await queries.SearchMemoriesAsync(userId, query, limit, generated[0].Vector.ToArray(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = await queries.SearchMemoriesAsync(userId, query, limit, null, cancellationToken);
        }
        var ordered = result.Select(x => new MemoryEvidence(x.Id, x.Type.ToString(), x.Content, x.Status.ToString(),
            x.Confidence, x.Evidence.Select(e => e.EventId).Distinct().ToArray())).ToArray();
        _memoryEvidence.AddRange(ordered);
        foreach (var eventId in ordered.SelectMany(x => x.EventIds)) _retrievedEventIds.Add(eventId);
        return ordered;
    }
}
