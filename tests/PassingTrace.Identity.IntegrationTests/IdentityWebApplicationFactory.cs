using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PassingTrace.Identity.Infrastructure;

namespace PassingTrace.Identity.IntegrationTests;

public sealed class IdentityWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public IdentityWebApplicationFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:identity",
            "Host=unused;Database=unused;Username=unused;Password=unused");
        builder.UseSetting("MobileRegistration:BootstrapCode", "testing-bootstrap");
        // Legacy deployment settings must not impose a registration quota.
        builder.UseSetting("MobileRegistration:MaxUsers", "1");

        // Keep integration tests independent of the Windows Event Log, which
        // is unavailable in restricted test runners.
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<PassingTrace.Identity.AuthorizationServer.Profile.IAvatarStorage>();
            services.AddSingleton<PassingTrace.Identity.AuthorizationServer.Profile.IAvatarStorage, TestAvatarStorage>();
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();

            services.AddDbContext<IdentityDbContext>(options =>
            {
                options.UseSqlite(_connection);
                options.UseOpenIddict<long>();
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
