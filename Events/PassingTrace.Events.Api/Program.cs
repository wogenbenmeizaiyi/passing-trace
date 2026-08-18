using PassingTrace.Events.Api.DependencyInjection;
using PassingTrace.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddTraceInfrastructure(builder.Configuration)
    .AddTraceAuthentication(builder.Configuration)
    .AddTraceApplication();

var app = builder.Build();
await app.ConfigureTracePipelineAsync();
app.Run();

/// <summary>暴露入口类型，供 WebApplicationFactory 启动内存测试宿主。</summary>
public partial class Program;
