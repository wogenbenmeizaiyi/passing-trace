using Medallion.Threading.Redis;
using StackExchange.Redis;

namespace PassingTrace.Events.Api.Events
{
    public class DistributedLockService(IConnectionMultiplexer redis) : IDistributedLockService
    {
        public async Task<T> ExecuteAsync<T>(string resource, Func<CancellationToken, Task<T>> action, TimeSpan? waitTimeout = null, CancellationToken cancellationToken = default)
        {
            var distributedLock = new RedisDistributedLock("event:123", redis.GetDatabase());
            await using var handle = await distributedLock.AcquireAsync();
            try
            {
                // 临界区
            }
            finally
            {
                // await using 会自动释放
            }
            throw new NotImplementedException();
        }
    }
}
