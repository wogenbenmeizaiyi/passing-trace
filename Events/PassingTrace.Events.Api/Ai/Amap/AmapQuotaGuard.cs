using System.Globalization;
using Microsoft.Extensions.Options;
using PassingTrace.Events.Api.Places;
using StackExchange.Redis;

namespace PassingTrace.Events.Api.Ai.Amap;

public enum AmapQuotaKind
{
    Search,
    Lbs,
}

public interface IAmapQuotaGuard
{
    Task<bool> TryConsumeAsync(AmapQuotaKind kind, CancellationToken cancellationToken);
}

public sealed class RedisAmapQuotaGuard(
    IConnectionMultiplexer redis,
    IOptions<AmapOptions> options,
    TimeProvider clock) : IAmapQuotaGuard
{
    public async Task<bool> TryConsumeAsync(AmapQuotaKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        var month = now.ToString("yyyyMM", CultureInfo.InvariantCulture);
        var suffix = kind == AmapQuotaKind.Search ? "search" : "lbs";
        var key = $"passingtrace:amap:quota:{month}:{suffix}";
        var database = redis.GetDatabase();
        var count = await database.StringIncrementAsync(key);
        if (count == 1)
            await database.KeyExpireAsync(key, TimeSpan.FromDays(45));
        var configuredLimit = kind == AmapQuotaKind.Search
            ? options.Value.SearchMonthlyLimit
            : options.Value.LbsMonthlyLimit;
        var limit = Math.Max(1, configuredLimit);
        return count <= limit;
    }
}
