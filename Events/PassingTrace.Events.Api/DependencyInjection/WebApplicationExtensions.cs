using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PassingTrace.Infrastructure;

namespace PassingTrace.Events.Api.DependencyInjection;

/// <summary>组装业务 API 的 HTTP 请求管道。</summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// 开发环境自动迁移业务数据库，生产环境启用异常页与 HTTPS；
    /// 按序挂载异常处理、认证与授权，并映射控制器。
    /// </summary>
    public static async Task<WebApplication> ConfigureTracePipelineAsync(
        this WebApplication app)
    {
        if (app.Environment.IsDevelopment() ||
            app.Configuration.GetValue<bool>("Database:AutoMigrate"))
        {
            // 本地开发默认迁移；单机容器部署可显式开启，集群部署应改用独立迁移任务。
            await using var scope = app.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TraceDbContext>();
            await dbContext.Database.MigrateAsync();
        }
        else
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseExceptionHandler();
        // 浏览器开发客户端与 API 使用独立端口，CORS 必须先于认证处理预检请求。
        // 生产环境默认不配置跨域来源，继续采用同域反向代理。
        app.UseCors(ApplicationExtensions.WebClientCorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }
}
