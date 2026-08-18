using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PassingTrace.Infrastructure.Persistence;

/// <summary>
/// 仅供 EF Core 设计时（dotnet ef）实例化 DbContext，不参与运行时依赖注入。
/// 连接串优先读取环境变量，缺省使用本地开发默认值。
/// </summary>
public sealed class TraceDbContextFactory : IDesignTimeDbContextFactory<TraceDbContext>
{
    public TraceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ConnectionStrings__trace")
            ?? "Host=localhost;Port=5432;Database=trace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<TraceDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TraceDbContext(options);
    }
}
