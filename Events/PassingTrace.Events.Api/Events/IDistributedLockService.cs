namespace PassingTrace.Events.Api.Events
{
    public interface IDistributedLockService
    {
        Task<T> ExecuteAsync<T>(
            string resource,
            Func<CancellationToken, Task<T>> action,
            TimeSpan? waitTimeout = null,
            CancellationToken cancellationToken = default);
    }
}
