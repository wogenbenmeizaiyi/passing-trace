using Microsoft.Extensions.Options;
using PassingTrace.Events.Api.Security;

namespace PassingTrace.Events.Api.Development;

public static class DevelopmentDemoEndpoints
{
    public static WebApplication MapDevelopmentDemo(this WebApplication app)
    {
        // No route at all in Production, even if configuration is accidentally copied.
        if (!app.Environment.IsDevelopment()) return app;

        app.MapPost("/api/v1/development/demo-data", async (HttpContext context) =>
        {
            var options = context.RequestServices.GetRequiredService<IOptions<DevelopmentDemoOptions>>().Value;
            if (!options.Enabled) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(options.Username) ||
                !string.Equals(context.User.FindFirst("preferred_username")?.Value,
                    options.Username, StringComparison.OrdinalIgnoreCase))
                return Results.Forbid();

            var result = await context.RequestServices.GetRequiredService<DevelopmentDemoSeeder>()
                .SeedAsync(context.User.GetUserId(), context.RequestAborted);
            return Results.Ok(result);
        }).RequireAuthorization();

        return app;
    }
}
