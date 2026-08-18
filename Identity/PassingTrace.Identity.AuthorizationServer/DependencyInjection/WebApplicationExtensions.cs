using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PassingTrace.Identity.Infrastructure;

namespace PassingTrace.Identity.AuthorizationServer.DependencyInjection;

/// <summary>组装 HTTP 请求管道；开发环境自动迁移，生产环境走异常页与 HTTPS。</summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// 组装 HTTP 请求管道：开发环境自动执行数据库迁移，生产环境启用异常页与 HTTPS；
    /// 按序挂载静态文件、CORS、限流、认证与授权中间件，并映射控制器和 Razor 页面。
    /// </summary>
    /// <param name="app">构建完成的 WebApplication。</param>
    public static async Task<WebApplication> ConfigureIdentityPipelineAsync(
        this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            // 仅开发环境自动迁移；生产迁移应作为部署步骤单独执行。
            await using var scope = app.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await dbContext.Database.MigrateAsync();
        }
        else
        {
            app.UseExceptionHandler("/error");
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseStaticFiles();
        app.UseStatusCodePages();
        app.UseRouting();
        // OpenIddict 是 AuthenticationRequestHandler，CORS 必须位于认证中间件之前。
        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapRazorPages();

        return app;
    }
}
