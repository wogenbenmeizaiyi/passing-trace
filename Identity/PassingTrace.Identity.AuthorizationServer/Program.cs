using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry;
using PassingTrace.Identity.Application.DependencyInjection;
using PassingTrace.Identity.Infrastructure;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("user")
    ?? throw new InvalidOperationException("缺少名为 'user' 的数据库连接字符串。");

builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

var openTelemetry = builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(builder.Environment.ApplicationName))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddNpgsql();
    });

if (!string.IsNullOrWhiteSpace(
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    openTelemetry.UseOtlpExporter();
}

builder.Services.AddControllers();
builder.Services.AddAutoInject("PassingTrace.Identity.Application");
builder.Services.AddOpenApi();
var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}
if(app.Environment.IsDevelopment())
{
    var scop = app.Services.CreateScope();
    var dbContext = scop.ServiceProvider.GetRequiredService<IdentityDbContext>();
    dbContext.Database.Migrate();
}


app.MapControllers();
app.MapOpenApi();
app.Run();
