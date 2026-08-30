using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Security;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.Ai;

[ApiController]
[Authorize]
[Route("api/v1/events/{eventId:long}/semantic")]
public sealed class EventSemanticController(TraceDbContext db, IAnalysisOutbox outbox, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EventSemanticResponse>> GetAsync(long eventId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var evt = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == eventId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (evt is null) return NotFound();
        var run = await db.EventSemanticRuns.AsNoTracking()
            .Where(x => x.EventId == eventId && x.UserId == userId && x.SourceRevision == evt.CurrentSourceRevision)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return Ok(new EventSemanticResponse(eventId, evt.CurrentSourceRevision, "Pending", null, null,
                string.Empty, string.Empty, evt.UpdatedAt, null, null));
        }
        object? semantic = string.IsNullOrWhiteSpace(run.SemanticEnvelopeJson)
            ? null : JsonSerializer.Deserialize<object>(run.SemanticEnvelopeJson);
        return Ok(new EventSemanticResponse(eventId, run.SourceRevision, run.Status.ToString(), run.Summary,
            semantic, run.Model, run.PipelineVersion, run.CreatedAt, run.CompletedAt, run.ErrorMessage));
    }

    [HttpPost("reparse")]
    public async Task<IActionResult> ReparseAsync(long eventId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        var evt = await db.Events.FirstOrDefaultAsync(x => x.Id == eventId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (evt is null) return NotFound();
        var now = clock.GetUtcNow();
        outbox.EnqueueEvent(evt, evt.CurrentSourceRevision, now, priority: 200);
        var message = db.ChangeTracker.Entries<OutboxMessage>().Select(x => x.Entity).Last();
        message.PayloadJson = "{\"force\":true}";
        await db.SaveChangesAsync(cancellationToken);
        return Accepted();
    }
}
