using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Common;
using PassingTrace.Events.Api.Security;

namespace PassingTrace.Events.Api.Storylines;

[ApiController]
[Authorize]
[Route("api/v1/storylines")]
public sealed class StorylinesController(StorylineService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StorylineListResponse>> ListAsync(
        [FromQuery] StorylineStatus? status,
        [FromQuery] string? categoryKey,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 40,
        [FromQuery] Guid? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var items = await service.ListAsync(User.GetUserId(), status, categoryKey, from, to, limit, cursor, cancellationToken);
        return Ok(new StorylineListResponse(items, items.Count == Math.Clamp(limit, 1, 100) ? items[^1].Id : null));
    }

    [HttpPost]
    public async Task<ActionResult<StorylineSaveResponse>> CreateAsync(
        [FromBody] SaveStorylineRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        RequireIdempotency(idempotencyKey);
        var result = await service.CreateAsync(User.GetUserId(), request, idempotencyKey, cancellationToken);
        return CreatedAtAction(nameof(GetAsync), new { id = result.Storyline.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StorylineRevisionResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(User.GetUserId(), id, null, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StorylineSaveResponse>> SaveAsync(
        Guid id, [FromBody] SaveStorylineRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        RequireIdempotency(idempotencyKey);
        return Ok(await service.SaveAsync(User.GetUserId(), id, ParseIfMatch(), request, idempotencyKey, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(User.GetUserId(), id, ParseIfMatch(), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/revisions")]
    public async Task<ActionResult<IReadOnlyList<StorylineRevisionHistoryResponse>>> RevisionsAsync(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await service.RevisionsAsync(User.GetUserId(), id, cancellationToken));

    [HttpGet("{id:guid}/revisions/{revision:int}")]
    public async Task<ActionResult<StorylineRevisionResponse>> RevisionAsync(
        Guid id, int revision, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(User.GetUserId(), id, revision, cancellationToken));

    [HttpPost("{id:guid}/revisions/{revision:int}/restore")]
    public async Task<ActionResult<StorylineSaveResponse>> RestoreAsync(
        Guid id, int revision,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        RequireIdempotency(idempotencyKey);
        return Ok(await service.RestoreAsync(User.GetUserId(), id, revision, ParseIfMatch(), idempotencyKey, cancellationToken));
    }

    [HttpPost("{id:guid}/changes")]
    public async Task<ActionResult<StorylineSaveResponse>> ChangeAsync(
        Guid id, [FromBody] StorylineChangeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        RequireIdempotency(idempotencyKey);
        return Ok(await service.ApplyChangeAsync(User.GetUserId(), id, ParseIfMatch(), request, idempotencyKey, cancellationToken));
    }

    private uint ParseIfMatch()
    {
        var raw = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw) || !uint.TryParse(raw.Trim('"'), out var version))
            throw new PreconditionRequiredException();
        return version;
    }

    private static void RequireIdempotency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainValidationException("缺少 Idempotency-Key。");
    }
}

[ApiController]
[Authorize]
[Route("api/v1/storyline-taxonomy")]
public sealed class StorylineTaxonomyController : ControllerBase
{
    [HttpGet]
    public ActionResult<StorylineTaxonomyResponse> Get() => Ok(new StorylineTaxonomyResponse(
        StorylineTaxonomy.Version,
        StorylineTaxonomy.All.Select(x => new StorylineCategoryResponse(x.Key, x.Value)).ToArray(),
        [
            new(StorylineRelationType.Sequence, "sequence", "先后"),
            new(StorylineRelationType.Branch, "branch", "分支"),
            new(StorylineRelationType.Parallel, "parallel", "并行"),
            new(StorylineRelationType.Related, "related", "相关"),
        ]));
}
