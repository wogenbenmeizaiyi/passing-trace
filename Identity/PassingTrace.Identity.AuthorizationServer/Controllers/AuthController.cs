using Microsoft.AspNetCore.Mvc;

using PassingTrace.Identity.Application.Interfaces;
using PassingTrace.Identity.Application.Models;
using PassingTrace.Identity.AuthorizationServer.Common;

namespace PassingTrace.Identity.AuthorizationServer.Controllers
{


    [ApiController]
    [Tags("用户认证")]
    [Route("api/auth")]
    public sealed class AuthController(ILoginService loginService) : ControllerBase
    {
        [HttpPost("login")]
        public async Task<ActionResult<ApiResponse<LoginResult>>> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken)
        {
            var result = await loginService.LoginAsync(
                request.Email,
                request.Password,
                cancellationToken);

            var traceId = HttpContext.TraceIdentifier;

            return result.Status switch
            {
                LoginStatus.Succeeded => Ok(ApiResponse<LoginResult>.Ok(result, traceId)),
                LoginStatus.Inactive => StatusCode(
                    StatusCodes.Status403Forbidden,
                    ApiResponse<LoginResult>.Fail(
                        ApiResponseCodes.Forbidden,
                        "账号已被禁用",
                        traceId)),
                _ => Unauthorized(ApiResponse<LoginResult>.Fail(
                    ApiResponseCodes.Unauthorized,
                    "账号或密码错误",
                    traceId))
            };
        }
    }

    public sealed record LoginRequest(string Email, string Password);
}
