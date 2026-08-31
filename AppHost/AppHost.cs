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
var amapWebServiceKey = builder.AddParameter("amap-web-service-key", secret: true);

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
    .WithReference(identityDatabase)
    .WaitFor(identityDatabase);

// 业务 API 使用独立数据库，并通过 Identity 的发现文档离线验证 Access Token。
var traceDatabase = postgres.AddDatabase("trace");

var api = builder.AddProject<Projects.PassingTrace_Events_Api>("passingtrace-events-api")
    .WithReference(traceDatabase)
    .WithReference(redis)
    .WithEnvironment("Identity__Authority", identity.GetEndpoint("http"))
    .WithEnvironment("Identity__RequireHttpsMetadata", "false")
    .WithEnvironment("ObjectStorage__Endpoint", minio.GetEndpoint("api"))
    .WithEnvironment("ObjectStorage__PublicEndpoint", objectStoragePublicEndpoint)
    .WithEnvironment("ObjectStorage__AccessKey", minioAccessKey)
    .WithEnvironment("ObjectStorage__SecretKey", minioSecretKey)
    .WithEnvironment("Qwen__ApiKey", qwenApiKey)
    .WithEnvironment("Amap__WebServiceKey", amapWebServiceKey)
    .WaitFor(identity)
    .WaitFor(traceDatabase)
    .WaitFor(redis)
    .WaitFor(minio);

var aiWorker = builder.AddProject<Projects.PassingTrace_Ai_Worker>("passingtrace-ai-worker")
    .WithReference(traceDatabase)
    .WithEnvironment("ObjectStorage__Endpoint", minio.GetEndpoint("api"))
    .WithEnvironment("ObjectStorage__PublicEndpoint", objectStoragePublicEndpoint)
    .WithEnvironment("ObjectStorage__AccessKey", minioAccessKey)
    .WithEnvironment("ObjectStorage__SecretKey", minioSecretKey)
    .WithEnvironment("Qwen__ApiKey", qwenApiKey)
    .WaitFor(traceDatabase)
    .WaitFor(minio)
    .WaitFor(api);

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
        .WithEnvironment("VITE_IDENTITY_AUTHORITY", identity.GetEndpoint("http"))
        .WithEnvironment("VITE_EVENTS_API_BASE_URL", api.GetEndpoint("http"))
        .WithReference(identity)
        .WithReference(api)
        .WaitFor(identity)
        .WaitFor(api)
        .WithExternalHttpEndpoints();

    builder.AddViteApp("passingtrace-sso-demo", "../passingtrace-sso-demo")
        .WithPnpm(installArgs: ["--config.confirmModulesPurge=false"])
        .WithEndpoint("http", endpoint => endpoint.Port = 5174)
        .WithEnvironment("VITE_IDENTITY_AUTHORITY", identity.GetEndpoint("http"))
        .WithEnvironment("VITE_MAIN_WEB_URL", "http://localhost:5173")
        .WithReference(identity)
        .WaitFor(identity)
        .WithExternalHttpEndpoints();
}


builder.Build().Run();
