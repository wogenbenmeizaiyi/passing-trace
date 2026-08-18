using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PassingTrace.Core.Events;

namespace PassingTrace.Events.Api.Common;

/// <summary>
/// 将领域异常映射为稳定的 HTTP ProblemDetails，未识别的异常交由默认处理。
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        (int status, string title) = exception switch
        {
            EventNotFoundException => (StatusCodes.Status404NotFound, "资源不存在"),
            ConcurrencyException => (StatusCodes.Status409Conflict, "版本冲突"),
            IdempotencyConflictException => (StatusCodes.Status409Conflict, "幂等冲突"),
            DomainValidationException => (StatusCodes.Status400BadRequest, "请求不合法"),
            PreconditionRequiredException => (StatusCodes.Status428PreconditionRequired, "缺少前置条件"),
            _ => (0, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        context.Response.StatusCode = status;

        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
            },
            cancellationToken);

        return true;
    }
}
