using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using PassingTrace.Identity.AuthorizationServer.Profile;
using PassingTrace.Identity.Domain.Entities;
using PassingTrace.Identity.Domain.Enums;
using PassingTrace.Identity.Infrastructure;
using QRCoder;

namespace PassingTrace.Identity.AuthorizationServer.Controllers;

public sealed record PersonProfile(string Id, string Nickname, string Bio, bool HasAvatar, string FriendCode);

/// <summary>Only deliberately public profile fields; never account names, email or storage keys.</summary>
[ApiController, Route("api/v1/people")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class PeopleController(IdentityDbContext db, IAvatarStorage avatars) : ControllerBase
{
    private bool Allowed => User.HasScope("profile");
    private static PersonProfile Map(User u) => new(u.Id.ToString(CultureInfo.InvariantCulture),
        u.Nickname ?? "星期八用户", u.Bio ?? "", u.AvatarKey != null, u.FriendCode);

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!Allowed || !long.TryParse(User.FindFirst("sub")?.Value, out var id)) return Forbid();
        var u = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Status == UserStatus.Active, ct);
        if (u is null) return NotFound();
        // Leave enough redundancy for clients displaying a small central avatar.
        using var data = QRCodeGenerator.GenerateQrCode($"passingtrace-friend:{u.FriendCode}", QRCodeGenerator.ECCLevel.H);
        using var qr = new PngByteQRCode(data);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(new { profile = Map(u), qrDataUrl = "data:image/png;base64," + Convert.ToBase64String(qr.GetGraphic(6)) });
    }

    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve([FromQuery] string code, CancellationToken ct)
    {
        if (!Allowed) return Forbid();
        code = code.Trim().Replace("passingtrace-friend:", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
        if (code.Length != 16 || !code.All(Uri.IsHexDigit)) return BadRequest(new { message = "请检查好友码后重试。" });
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.FriendCode == code && x.Status == UserStatus.Active, ct);
        Response.Headers.CacheControl = "private, no-store";
        return user is null ? NotFound() : Ok(Map(user));
    }

    [HttpPost("profiles")]
    public async Task<IActionResult> Profiles([FromBody] string[] ids, CancellationToken ct)
    {
        if (!Allowed) return Forbid();
        if (ids.Length > 100 || ids.Any(x => !long.TryParse(x, out var n) || n <= 0)) return BadRequest();
        var keys = ids.Select(long.Parse).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => keys.Contains(x.Id) && x.Status == UserStatus.Active).ToListAsync(ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(users.Select(Map));
    }

    [HttpGet("{id:long}/avatar")]
    public async Task<IActionResult> Avatar(long id, CancellationToken ct)
    {
        if (!Allowed) return Forbid();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Status == UserStatus.Active, ct);
        if (user?.AvatarKey is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(await avatars.ReadAsync(user.AvatarKey, ct), "image/png");
    }
}
