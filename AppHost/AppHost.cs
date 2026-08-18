var builder = DistributedApplication.CreateBuilder(args);

// 密码由 Aspire 参数系统提供，不写入源码或 appsettings。
var postgresPassword = builder.AddParameter(
    "postgres-password",
    secret: true);


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
    .WithEnvironment("Identity__Authority", identity.GetEndpoint("https"))
    .WaitFor(identity)
    .WaitFor(traceDatabase);

// Vue 仍是独立进程；AppHost 只负责统一启动、端口和 Identity 地址注入。
builder.AddViteApp("passingtrace-web", "../passingtrace-web")
    .WithPnpm(installArgs: ["--config.confirmModulesPurge=false"])
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithEnvironment("VITE_IDENTITY_AUTHORITY", identity.GetEndpoint("https"))
    .WithReference(identity)
    .WaitFor(identity)
    .WithExternalHttpEndpoints();

// 第二个独立 Origin/Client ID 用于从界面验证浏览器 SSO，而不是共享前端 Token。
builder.AddViteApp("passingtrace-sso-demo", "../passingtrace-sso-demo")
    .WithPnpm(installArgs: ["--config.confirmModulesPurge=false"])
    .WithEndpoint("http", endpoint => endpoint.Port = 5174)
    .WithEnvironment("VITE_IDENTITY_AUTHORITY", identity.GetEndpoint("https"))
    .WithEnvironment("VITE_MAIN_WEB_URL", "http://localhost:5173")
    .WithReference(identity)
    .WaitFor(identity)
    .WithExternalHttpEndpoints();

builder.Build().Run();
