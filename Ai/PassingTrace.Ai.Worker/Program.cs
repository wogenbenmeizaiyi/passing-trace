using Microsoft.EntityFrameworkCore;
using PassingTrace.Ai.Worker;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Media;
using PassingTrace.Infrastructure;
using Pgvector.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Worker 空闲时每两秒轮询一次 Outbox；避免 EF 将正常轮询 SQL 刷满 Aspire 日志。
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

builder.AddNpgsqlDbContext<TraceDbContext>(
    "trace",
    configureDbContextOptions: options =>
        options.UseNpgsql(npgsql => npgsql.UseVector()));

builder.Services.Configure<ObjectStorageOptions>(builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
builder.Services.Configure<QwenAiOptions>(builder.Configuration.GetSection(QwenAiOptions.SectionName));
builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();
builder.Services.AddSingleton<QwenClientFactory>();
builder.Services.AddSingleton(provider => provider.GetRequiredService<QwenClientFactory>().ChatClient);
builder.Services.AddSingleton(provider => provider.GetRequiredService<QwenClientFactory>().EmbeddingGenerator);
builder.Services.AddScoped<SemanticPipeline>();
builder.Services.AddSingleton<ImageDerivativeProcessor>();
builder.Services.AddHostedService<AnalysisWorker>();

await builder.Build().RunAsync();
