using PassingTrace.Core.Events;
using PassingTrace.Events.Api.DependencyInjection;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddRedisClient("redis");
builder.AddNpgsqlDbContext<TraceDbContext>("trace");
builder.Services
    .AddTraceAuthentication(builder.Configuration)
    .AddTraceApplication();

builder.Services.AddScoped<IEventRepository, EventRepository>();


var app = builder.Build();
await app.ConfigureTracePipelineAsync();
app.Run();

/// <summary>暴露入口类型，供 WebApplicationFactory 启动内存测试宿主。</summary>
public partial class Program;
