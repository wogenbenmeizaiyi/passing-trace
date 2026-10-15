using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;

namespace PassingTrace.Infrastructure.Persistence.Ai;

public sealed class EventSemanticRepository(TraceDbContext db) : IEventSemanticRepository
{
    public async Task<EventSemanticData?> ReadAsync(long userId, long eventId, CancellationToken cancellationToken)
    {
        var evt = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == eventId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (evt is null) return null;
        var run = await db.EventSemanticRuns.AsNoTracking().Where(x => x.EventId == eventId && x.UserId == userId && x.SourceRevision == evt.CurrentSourceRevision)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return new(evt, run);
    }

    public async Task<bool> RequestReparseAsync(long userId, long eventId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var evt = await db.Events.FirstOrDefaultAsync(x => x.Id == eventId && x.UserId == userId && x.DeletedAt == null, cancellationToken);
        if (evt is null) return false;
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Event = evt,
            SourceRevision = evt.CurrentSourceRevision,
            MessageType = "event.analyze",
            PayloadJson = "{\"force\":true}",
            Priority = 200,
            Status = OutboxStatus.Pending,
            AvailableAt = now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
