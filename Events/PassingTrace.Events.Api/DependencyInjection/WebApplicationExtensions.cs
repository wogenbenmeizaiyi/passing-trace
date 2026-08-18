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
        if (app.Environment.IsDevelopment())
        {
            // 仅开发环境自动迁移；生产迁移应作为部署步骤单独执行。
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
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }
}
