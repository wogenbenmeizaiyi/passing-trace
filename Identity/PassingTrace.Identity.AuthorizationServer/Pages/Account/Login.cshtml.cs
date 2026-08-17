using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PassingTrace.Identity.Domain.Entities;
using PassingTrace.Identity.Domain.Enums;
using Microsoft.AspNetCore.WebUtilities;
using PassingTrace.Identity.AuthorizationServer.Mobile;

namespace PassingTrace.Identity.AuthorizationServer.Pages.Account;

/// <summary>Identity 托管的用户名密码登录页；成功后只创建站点 Cookie。</summary>
public sealed class LoginModel(
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    MobileFlowService mobileFlow) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var ticket = GetLaunchTicket();
        return await mobileFlow.IsValidLoginLaunchAsync(ticket, HttpContext.RequestAborted)
            ? Page()
            : NotFound();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var launchTicket = GetLaunchTicket();
        if (!await mobileFlow.IsValidLoginLaunchAsync(launchTicket, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByNameAsync(Input.Username);
        if (user is null || user.Status != UserStatus.Active)
        {
            AddGenericLoginError();
            return Page();
        }

        // lockoutOnFailure 让错误密码累计到 Identity 的 AccessFailedCount。
        var result = await signInManager.PasswordSignInAsync(
            user,
            Input.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            AddGenericLoginError();
            return Page();
        }

        var now = DateTimeOffset.UtcNow;
        user.LastLoginAt = now;
        user.UpdatedAt = now;
        await userManager.UpdateAsync(user);

        if (!await mobileFlow.ConsumeLoginLaunchAsync(
            launchTicket!,
            HttpContext.RequestAborted))
        {
            await signInManager.SignOutAsync();
            return BadRequest("移动登录启动票据已失效。");
        }

        return LocalRedirectOrHome(ReturnUrl);
    }

    // 用户不存在、密码错误、账号停用和锁定返回同一提示，避免用户名枚举。
    private void AddGenericLoginError() =>
        ModelState.AddModelError(string.Empty, "用户名或密码错误，或账号暂时不可用。");

    private IActionResult LocalRedirectOrHome(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage("/Index");

    private string? GetLaunchTicket()
    {
        if (!Url.IsLocalUrl(ReturnUrl))
        {
            return null;
        }

        var uri = new Uri("https://local" + ReturnUrl, UriKind.Absolute);
        return QueryHelpers.ParseQuery(uri.Query)["launch_ticket"].ToString();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "请输入用户名。")]
        [Display(Name = "用户名")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "请输入密码。")]
        [DataType(DataType.Password)]
        [Display(Name = "密码")]
        public string Password { get; set; } = string.Empty;
    }
}
