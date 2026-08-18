using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PassingTrace.Core.Events;
using PassingTrace.Infrastructure.Persistence;

namespace PassingTrace.Infrastructure.DependencyInjection;

/// <summary>集中注册业务数据库、仓储与 EF Core 持久化。</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册业务基础设施，使用独立的 <c>trace</c> 数据库连接串，不访问 Identity 数据库。
    /// </summary>
    public static IServiceCollection AddTraceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("trace")
            ?? throw new InvalidOperationException(
                "缺少名为 'trace' 的数据库连接字符串。");

        services.AddDbContext<TraceDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IEventRepository, EventRepository>();

        return services;
    }
}
