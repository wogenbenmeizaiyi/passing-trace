using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddKubernetesEnvironment("k8s");

// 密码由 Aspire 参数系统提供，不写入源码或 appsettings。
var postgresPassword = builder.AddParameter(
    "postgres-password",
    secret: true);
var minioAccessKey = builder.AddParameter("minio-access-key", secret: true);
var minioSecretKey = builder.AddParameter("minio-secret-key", secret: true);
var objectStoragePublicEndpoint = builder.AddParameter(
    "object-storage-public-endpoint",
    "http://localhost:9000");
var qwenApiKey = builder.AddParameter("qwen-api-key", secret: true);
var miniMaxApiKey = builder.AddParameter("minimax-api-key", secret: true);
var amapWebServiceKey = builder.AddParameter("amap-web-service-key", secret: true);
var amapMcpKey = builder.Configuration["AMAP_MCP_KEY"];

var redis = builder.AddRedis("redis")
    .WithLifetime(ContainerLifetime.Persistent);
// 持久化容器便于本地开发保留账号、客户端和授权数据。
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg18-trixie")
    .WithHostPort(5432)
    .WithPassword(postgresPassword)
    // PostgreSQL 18+ 要求卷挂在 /var/lib/postgresql，由镜像在其下创建按主版本
    // 命名的数据目录。旧版 Aspire 的 /var/lib/postgresql/data 挂载会被镜像拒绝。
    // 使用新卷名保留原开发卷，避免无提示覆盖或删除旧数据。
    .WithVolume("passingtrace-postgres18-data", "/var/lib/postgresql")
    .WithLifetime(ContainerLifetime.Persistent);

// 私有对象存储。API/Worker 通过 api endpoint 访问；预签名 URL 使用单独的 PublicEndpoint。
var minio = builder.AddContainer("minio", "minio/minio")
    .WithImageTag("RELEASE.2025-09-07T16-13-09Z")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", minioAccessKey)
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioSecretKey)
    .WithEnvironment("MINIO_API_CORS_ALLOW_ORIGIN", "*")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithVolume("passingtrace-minio-data", "/data")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithExternalHttpEndpoints();

var identityDatabase = postgres.AddDatabase("identity");

// 引用数据库会自动注入 ConnectionStrings__identity。
var identity = builder.AddProject<Projects.PassingTrace_Identity_AuthorizationServer>("passingtrace-identity")
    // 手机内部测试包使用固定 HTTP 端口，避免每次重启 Aspire 后旧登录令牌和 APK 指向失效。
    .WithEndpoint("http", endpoint => endpoint.Port = 56229)
    .WithReference(identityDatabase)
    .WaitFor(identityDatabase);

// 业务 API 使用独立数据库，并通过 Identity 的发现文档离线验证 Access Token。
var traceDatabase = postgres.AddDatabase("trace");

var api = builder.AddProject<Projects.PassingTrace_Events_Api>("passingtrace-events-api")
    .WithEndpoint("http", endpoint => endpoint.Port = 54934)
    .WithReference(traceDatabase)
    .WithReference(redis)
    .WithEnvironment("Identity__Authority", identity.GetEndpoint("http"))
    .WithEnvironment("Identity__RequireHttpsMetadata", "false")
    .WithEnvironment("ObjectStorage__Endpoint", minio.GetEndpoint("api"))
    .WithEnvironment("ObjectStorage__PublicEndpoint", objectStoragePublicEndpoint)
    .WithEnvironment("ObjectStorage__AccessKey", minioAccessKey)
    .WithEnvironment("ObjectStorage__SecretKey", minioSecretKey)
    .WithEnvironment("AiModels__Providers__Qwen__ApiKey", qwenApiKey)
    .WithEnvironment("AiModels__Providers__MiniMax__ApiKey", miniMaxApiKey)
    .WithEnvironment("Amap__WebServiceKey", amapWebServiceKey)
    .WaitFor(identity)
    .WaitFor(traceDatabase)
    .WaitFor(redis)
    .WaitFor(minio);

if (!string.IsNullOrWhiteSpace(amapMcpKey))
    api.WithEnvironment("AMAP_MCP_KEY", amapMcpKey);

var aiWorker = builder.AddProject<Projects.PassingTrace_Ai_Worker>("passingtrace-ai-worker")
    .WithReference(traceDatabase)
    .WithEnvironment("ObjectStorage__Endpoint", minio.GetEndpoint("api"))
    .WithEnvironment("ObjectStorage__PublicEndpoint", objectStoragePublicEndpoint)
    .WithEnvironment("ObjectStorage__AccessKey", minioAccessKey)
    .WithEnvironment("ObjectStorage__SecretKey", minioSecretKey)
    .WithEnvironment("AiModels__Providers__Qwen__ApiKey", qwenApiKey)
    .WithEnvironment("AiModels__Providers__MiniMax__ApiKey", miniMaxApiKey)
    .WaitFor(traceDatabase)
    .WaitFor(minio)
    .WaitFor(api);

if (builder.Environment.IsProduction())
{
    builder.AddContainer(
            "passingtrace-web",
            "passingtrace-web")
        .WithHttpEndpoint(port: 80, targetPort: 80);
}
else
{
    builder.AddViteApp("passingtrace-web", "../passingtrace-web")
        .WithPnpm(installArgs: ["--config.confirmModulesPurge=false"])
        .WithEndpoint("http", endpoint => endpoint.Port = 5173)
        .WithEnvironment("VITE_IDENTITY_AUTHORITY", identity.GetEndpoint("http"))
        .WithEnvironment("VITE_EVENTS_API_BASE_URL", api.GetEndpoint("http"))
        .WithReference(identity)
        .WithReference(api)
        .WaitFor(identity)
        .WaitFor(api)
        .WithExternalHttpEndpoints();

}


builder.Build().Run();
