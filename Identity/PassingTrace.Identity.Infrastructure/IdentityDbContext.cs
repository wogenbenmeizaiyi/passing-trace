using Microsoft.EntityFrameworkCore;
using PassingTrace.Identity.Domain.Entities;

namespace PassingTrace.Identity.Infrastructure
{
    public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();




        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        }

    }
}
