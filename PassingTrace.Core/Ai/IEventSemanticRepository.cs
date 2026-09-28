using PassingTrace.Core.Events;

namespace PassingTrace.Core.Ai;

public sealed record EventSemanticData(Event Event, EventSemanticRun? Run);

public interface IEventSemanticRepository
{
    Task<EventSemanticData?> ReadAsync(long userId, long eventId, CancellationToken cancellationToken);
    Task<bool> RequestReparseAsync(long userId, long eventId, DateTimeOffset now, CancellationToken cancellationToken);
}
