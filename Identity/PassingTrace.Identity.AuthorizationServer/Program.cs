using PassingTrace.Identity.AuthorizationServer.DependencyInjection;
using PassingTrace.Identity.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddIdentityInfrastructure(builder.Configuration,builder.Environment.IsDevelopment())
    .AddOpenIddictServer(builder.Configuration, builder.Environment)
    .AddIdentityApplicationServices(builder.Configuration)
    .AddIdentityRateLimiting(builder.Environment.IsEnvironment("Testing"))
    .AddIdentityCors(builder.Configuration)
    .AddIdentityObservability(builder.Environment, builder.Configuration);

var app = builder.Build();
await app.ConfigureIdentityPipelineAsync();
app.Run();

/// <summary>暴露入口类型，供 WebApplicationFactory 启动内存测试宿主。</summary>
public partial class Program;
