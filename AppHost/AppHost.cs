using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddKubernetesEnvironment("k8s");

// 密码由 Aspire 参数系统提供，不写入源码或 appsettings。
var postgresPassword = builder.AddParameter(
    "postgres-password",
    secret: true);

var redis = builder.AddRedis("redis")
    .WithLifetime(ContainerLifetime.Persistent);
// 持久化容器便于本地开发保留账号、客户端和授权数据。
var postgres = builder.AddPostgres("postgres")
    .WithHostPort(5432)
    .WithPassword(postgresPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var identityDatabase = postgres.AddDatabase("identity");

// 引用数据库会自动注入 ConnectionStrings__identity。
var identity = builder.AddProject<Projects.PassingTrace_Identity_AuthorizationServer>("passingtrace-identity")
    .WithReference(identityDatabase)
    .WaitFor(identityDatabase);

// 业务 API 使用独立数据库，并通过 Identity 的发现文档离线验证 Access Token。
var traceDatabase = postgres.AddDatabase("trace");

var api = builder.AddProject<Projects.PassingTrace_Events_Api>("passingtrace-events-api")
    .WithReference(traceDatabase)
    .WithReference(redis)
    .WithEnvironment("Identity__Authority", identity.GetEndpoint("https"))
    .WaitFor(identity)
    .WaitFor(traceDatabase)
    .WaitFor(redis);

if (builder.Environment.IsProduction())
{
    builder.AddContainer(
            "passingtrace-web",
            "passingtrace-web")
        .WithHttpEndpoint(port: 80, targetPort: 80);

    builder.AddContainer(
            "passingtrace-sso-demo",
            "passingtrace-sso-demo")
        .WithHttpEndpoint(port: 80, targetPort: 80);
}
else
{
    builder.AddViteApp("passingtrace-web", "../passingtrace-web")
        .WithPnpm(installArgs: ["--config.confirmModulesPurge=false"])
        .WithEndpoint("http", endpoint => endpoint.Port = 5173)
        .WithEnvironment("VITE_IDENTITY_AUTHORITY", identity.GetEndpoint("https"))
        .WithReference(identity)
        .WaitFor(identity)
        .WithExternalHttpEndpoints();

    builder.AddViteApp("passingtrace-sso-demo", "../passingtrace-sso-demo")
        .WithPnpm(installArgs: ["--config.confirmModulesPurge=false"])
        .WithEndpoint("http", endpoint => endpoint.Port = 5174)
        .WithEnvironment("VITE_IDENTITY_AUTHORITY", identity.GetEndpoint("https"))
        .WithEnvironment("VITE_MAIN_WEB_URL", "http://localhost:5173")
        .WithReference(identity)
        .WaitFor(identity)
        .WithExternalHttpEndpoints();
}


builder.Build().Run();
