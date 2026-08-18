using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PassingTrace.Identity.AuthorizationServer.DependencyInjection;

/// <summary>OpenTelemetry 资源与 Trace 采集；存在 OTLP 端点时才启用导出。</summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// 注册 OpenTelemetry 资源与 Trace 采集（ASP.NET Core、HttpClient、Npgsql）；
    /// 仅在配置了 <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> 时启用 OTLP 导出。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="environment">用于设置 OpenTelemetry 服务名。</param>
    /// <param name="configuration">用于读取 OTLP 导出端点配置。</param>
    public static IServiceCollection AddIdentityObservability(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        var openTelemetry = services
            .AddOpenTelemetry()
            .ConfigureResource(resource =>
                resource.AddService(environment.ApplicationName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddHttpClientInstrumentation();
                tracing.AddNpgsql();
            });

        if (!string.IsNullOrWhiteSpace(
                configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            openTelemetry.UseOtlpExporter();
        }

        return services;
    }
}
