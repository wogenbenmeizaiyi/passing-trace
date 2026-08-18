using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Events;

namespace PassingTrace.Infrastructure;

/// <summary>
/// 业务数据工作单元，管理 Event 与 SourceRevision 的持久化。
/// 与 Identity 使用不同的数据库，业务表之间不再拆库。
/// </summary>
public sealed class TraceDbContext(DbContextOptions<TraceDbContext> options)
    : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();

    public DbSet<SourceRevision> SourceRevisions => Set<SourceRevision>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(TraceDbContext).Assembly);
    }
}
