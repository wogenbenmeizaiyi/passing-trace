using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using PassingTrace.Identity.AuthorizationServer.Profile;
using PassingTrace.Identity.Domain.Entities;
using PassingTrace.Identity.Domain.Enums;
using PassingTrace.Identity.Infrastructure;

namespace PassingTrace.Identity.AuthorizationServer.Controllers;

public sealed record ProfileResponse(string Username, string Nickname, string Bio, DateTimeOffset CreatedAt, Guid Version, bool HasAvatar);

public sealed class UpdateProfileRequest
{
    public string Nickname { get; set; } = "";
    public string? Bio { get; set; }
    public Guid? Version { get; set; }
    public bool RemoveAvatar { get; set; }
    public IFormFile? Avatar { get; set; }
}

[ApiController]
[Route("api/v1/account")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class ProfileController(IdentityDbContext db, IAvatarStorage storage, TimeProvider clock,
    ILogger<ProfileController> logger) : ControllerBase
{
    private Task<User?> CurrentUser(CancellationToken ct) =>
        long.TryParse(User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value, out var id) &&
        User.HasScope("profile")
            ? db.Users.SingleOrDefaultAsync(x => x.Id == id && x.Status == UserStatus.Active, ct)
            : Task.FromResult<User?>(null);

    private static ProfileResponse Map(User user) => new(user.UserName!, user.Nickname ?? user.UserName!,
        user.Bio ?? "", user.CreatedAt, user.ProfileVersion, user.AvatarKey is not null);

    [HttpGet("profile")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var user = await CurrentUser(ct);
        return user is null ? Forbid() : Ok(Map(user));
    }

    [HttpGet("avatar")]
    public async Task<IActionResult> Avatar(CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var user = await CurrentUser(ct);
        if (user is null) return Forbid();
        if (user.AvatarKey is null) return NotFound();
        try { return File(await storage.ReadAsync(user.AvatarKey, ct), "image/png"); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Avatar read failed ({FailureType}).", ex.GetType().Name);
            return Problem(statusCode: 503, detail: "头像暂时无法加载，请稍后重试。");
        }
    }

    [HttpPut("profile")]
    [EnableRateLimiting("profile-update")]
    [RequestSizeLimit(AvatarImage.MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = AvatarImage.MaxBytes + 64 * 1024)]
    public async Task<IActionResult> Update([FromForm] UpdateProfileRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var user = await CurrentUser(ct);
        if (user is null) return Forbid();
        if (request.Version is null) return Problem(statusCode: 428, detail: "请先加载个人资料，再保存修改。");
        if (request.Version != user.ProfileVersion) return ConflictProblem();
        var nickname = request.Nickname.Trim();
        var bio = request.Bio?.Trim() ?? "";
        if (nickname.Length > 128 || StringInfo.ParseCombiningCharacters(nickname).Length is < 1 or > 24 || nickname.Any(char.IsControl))
            return Problem(statusCode: 400, detail: "昵称请填写 1～24 个字，不含换行或控制字符。");
        if (bio.Length > 1024 || StringInfo.ParseCombiningCharacters(bio).Length > 100 || bio.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            return Problem(statusCode: 400, detail: "个人简介不能超过 100 个字或包含控制字符。");
        if (request.RemoveAvatar && request.Avatar is not null)
            return Problem(statusCode: 400, detail: "请只选择更换头像或恢复默认头像中的一项。");

        string? newKey = null;
        var commitStarted = false;
        var previousKey = user.AvatarKey;
        try
        {
            if (request.Avatar is { } file)
            {
                if (file.Length is 0 or > AvatarImage.MaxBytes)
                    return Problem(statusCode: 400, detail: "请选择不超过 5MB 的头像图片。");
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct);
                byte[] image;
                try { image = AvatarImage.Normalize(buffer.ToArray()); }
                catch (InvalidDataException ex) { return Problem(statusCode: 400, detail: ex.Message); }
                newKey = $"identity/avatars/{user.Id}/{Guid.NewGuid():N}.png";
                await storage.PutAsync(newKey, image, ct);
                user.AvatarKey = newKey;
            }
            else if (request.RemoveAvatar) user.AvatarKey = null;
            user.Nickname = nickname;
            user.Bio = bio;
            user.ProfileVersion = Guid.NewGuid();
            user.UpdatedAt = clock.GetUtcNow();
            commitStarted = true;
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await Cleanup(newKey);
            return ConflictProblem();
        }
        catch (OperationCanceledException)
        {
            // A lost connection during commit does not prove the write was rolled back.
            // Keep the object in that case so a successfully committed avatar stays valid.
            if (!commitStarted) await Cleanup(newKey);
            throw;
        }
        catch (Exception ex)
        {
            if (!commitStarted) await Cleanup(newKey);
            logger.LogWarning("Profile save failed ({FailureType}).", ex.GetType().Name);
            return Problem(statusCode: 503, detail: "暂时无法确认保存结果，请重新加载个人资料后再试。");
        }
        if (previousKey != user.AvatarKey) await Cleanup(previousKey);
        return Ok(Map(user));
    }

    private ObjectResult ConflictProblem() => Problem(statusCode: 409,
        detail: "个人资料已在另一处更新。请重新加载后再修改，本次修改尚未保存。");

    private async Task Cleanup(string? key)
    {
        if (key is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await storage.DeleteAsync(key, timeout.Token);
        }
        catch { logger.LogWarning("An unused avatar could not be removed; storage cleanup is required."); }
    }
}
