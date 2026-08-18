using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassingTrace.Events.Api.Common;
using PassingTrace.Events.Api.Security;
using PassingTrace.Core.Events;

namespace PassingTrace.Events.Api.Events;

/// <summary>Event 管理的 HTTP 入口，负责协议校验与命令组装。</summary>
[ApiController]
[Authorize]
[Route("api/v1/events")]
public sealed class EventsController(EventService service) : ControllerBase
{
    /// <summary>创建 Trace 或 Plan，立即返回 Event，不等待 AI 解析。</summary>
    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateEventRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = new CreateEventCommand(
            User.GetUserId(),
            request.Kind,
            request.Title,
            request.RawContent,
            request.HappenedAt,
            request.PlannedAt,
            request.Timezone ?? "UTC",
            idempotencyKey);

        var evt = await service.CreateAsync(command, cancellationToken);

        return CreatedAtAction(
            nameof(GetAsync),
            new { id = evt.Id },
            ToResponse(evt));
    }

    /// <summary>游标分页查询 Event 列表。</summary>
    [HttpGet]
    public async Task<ActionResult<EventListResponse>> ListAsync(
        [FromQuery] EventKind? kind,
        [FromQuery] EventStatus? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 50,
        [FromQuery] long? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var query = new EventListQuery(
            User.GetUserId(),
            kind,
            status,
            from,
            to,
            IncludeDeleted: false,
            limit,
            cursor);

        var events = await service.ListAsync(query, cancellationToken);
        var items = events.Select(ToResponse).ToList();

        long? nextCursor = events.Count == limit && events.Count > 0
            ? events[^1].Id
            : null;

        return Ok(new EventListResponse(items, nextCursor));
    }

    /// <summary>查询单条 Event 详情。</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<EventResponse>> GetAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var evt = await service.GetAsync(User.GetUserId(), id, cancellationToken);

        if (evt is null || evt.DeletedAt is not null)
        {
            return NotFound();
        }

        return Ok(ToResponse(evt));
    }

    /// <summary>修改 Source，要求 If-Match 版本条件。</summary>
    [HttpPatch("{id:long}")]
    public async Task<ActionResult<EventResponse>> UpdateSourceAsync(
        long id,
        [FromBody] UpdateEventRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateEventCommand(
            User.GetUserId(),
            id,
            ParseIfMatch(),
            request.Title,
            request.RawContent,
            request.HappenedAt,
            request.PlannedAt,
            request.Timezone ?? "UTC");

        var evt = await service.UpdateSourceAsync(command, cancellationToken);

        return Ok(ToResponse(evt));
    }

    /// <summary>软删除 Event，要求 If-Match 版本条件。</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteAsync(
        long id,
        CancellationToken cancellationToken)
    {
        await service.SoftDeleteAsync(
            User.GetUserId(),
            id,
            ParseIfMatch(),
            cancellationToken);

        return NoContent();
    }

    private uint ParseIfMatch()
    {
        var header = Request.Headers.IfMatch.ToString();

        if (string.IsNullOrWhiteSpace(header) ||
            !uint.TryParse(header.Trim('"'), out var version))
        {
            throw new PreconditionRequiredException();
        }

        return version;
    }

    private static EventResponse ToResponse(Event evt)
    {
        return new EventResponse(
            evt.Id,
            evt.EventKind,
            evt.Status,
            evt.Title,
            evt.RawContent,
            evt.HappenedAt,
            evt.PlannedAt,
            evt.CompletedAt,
            evt.Timezone,
            evt.Visibility,
            evt.CurrentSourceRevision,
            evt.RowVersion,
            evt.CreatedAt,
            evt.UpdatedAt);
    }
}
