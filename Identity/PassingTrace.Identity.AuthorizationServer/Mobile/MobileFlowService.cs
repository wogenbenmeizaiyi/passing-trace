using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PassingTrace.Identity.Application.Accounts;
using PassingTrace.Identity.AuthorizationServer.Security;
using PassingTrace.Identity.AuthorizationServer.Setup;
using PassingTrace.Identity.Domain.Entities;
using PassingTrace.Identity.Domain.Enums;
using PassingTrace.Identity.Infrastructure;

namespace PassingTrace.Identity.AuthorizationServer.Mobile;

public sealed class MobileFlowService(
    IdentityDbContext dbContext,
    UserManager<User> userManager,
    ILookupNormalizer normalizer,
    FirstPartyClientRegistry clients,
    IOptions<MobileRegistrationOptions> options,
    TimeProvider timeProvider)
{
    private readonly MobileRegistrationOptions _options = options.Value;

    public async Task<RegistrationIntentResponse> CreateRegistrationIntentAsync(
        CreateRegistrationIntentRequest request,
        CancellationToken cancellationToken)
    {
        ValidateMobileAuthorizationRequest(
            request.ClientId,
            request.RedirectUri,
            request.CodeChallenge);

        if (!UsernamePolicy.IsValid(request.Username))
        {
            throw new MobileFlowException("invalid_username", "用户名格式不正确。");
        }

        if (await dbContext.Users.CountAsync(cancellationToken) >= _options.MaxUsers)
        {
            throw new MobileFlowException("registration_closed", "个人实例已完成初始化注册。", 403);
        }

        var now = timeProvider.GetUtcNow();
        var ticket = SecretEncoding.Generate();
        var normalizedUsername = normalizer.NormalizeName(request.Username) ?? request.Username;
        var entity = new MobileAuthorizationTicket
        {
            Id = Guid.CreateVersion7(),
            TicketHash = SecretEncoding.Hash(ticket),
            TicketType = MobileAuthorizationTicketType.RegistrationIntent,
            ClientId = request.ClientId,
            RedirectUri = request.RedirectUri,
            CodeChallenge = request.CodeChallenge,
            State = request.State,
            Nonce = request.Nonce,
            NormalizedUsernameHash = SecretEncoding.Hash(normalizedUsername),
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(_options.TicketLifetimeSeconds),
            ConcurrencyToken = Guid.NewGuid()
        };
        entity.RequestHash = ComputeIntentHash(entity);

        dbContext.MobileAuthorizationTickets.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegistrationIntentResponse(
            entity.Id,
            entity.RequestHash,
            _options.TicketLifetimeSeconds);
    }

    public async Task<RegistrationResponse> CompleteRegistrationAsync(
        CompleteRegistrationRequest request,
        Uri publicOrigin,
        CancellationToken cancellationToken)
    {
        VerifyBootstrapCode(request.BootstrapCode);
        var now = timeProvider.GetUtcNow();
        var intent = await dbContext.MobileAuthorizationTickets.SingleOrDefaultAsync(
            ticket => ticket.Id == request.IntentId &&
                ticket.TicketType == MobileAuthorizationTicketType.RegistrationIntent,
            cancellationToken)
            ?? throw new MobileFlowException("invalid_intent", "注册意图不存在。", 404);

        if (intent.ConsumedAt is not null || intent.ExpiresAt <= now)
        {
            throw new MobileFlowException("expired_intent", "注册意图已过期或已使用。", 410);
        }

        var normalized = normalizer.NormalizeName(request.Username) ?? request.Username;
        if (!SecretEncoding.Verify(normalized, intent.NormalizedUsernameHash!))
        {
            throw new MobileFlowException("intent_mismatch", "用户名与注册意图不匹配。", 400);
        }

        if (await dbContext.Users.CountAsync(cancellationToken) >= _options.MaxUsers)
        {
            throw new MobileFlowException("registration_closed", "个人实例已完成初始化注册。", 403);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = new User
        {
            UserName = request.Username,
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw new MobileFlowException(
                "registration_failed",
                string.Join(" ", result.Errors.Select(error => error.Description)),
                result.Errors.Any(error => error.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
                    ? 409
                    : 400);
        }

        var deviceSecret = SecretEncoding.Generate();
        var device = new MobileDevice
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            DisplayName = string.IsNullOrWhiteSpace(request.DeviceName)
                ? "My Android"
                : request.DeviceName.Trim()[..Math.Min(request.DeviceName.Trim().Length, 100)],
            SecretHash = SecretEncoding.Hash(deviceSecret),
            CreatedAt = now,
            ConcurrencyToken = Guid.NewGuid()
        };
        dbContext.MobileDevices.Add(device);

        intent.ConsumedAt = now;
        intent.ConcurrencyToken = Guid.NewGuid();
        var handoffSecret = SecretEncoding.Generate();
        var handoff = new MobileAuthorizationTicket
        {
            Id = Guid.CreateVersion7(),
            TicketHash = SecretEncoding.Hash(handoffSecret),
            TicketType = MobileAuthorizationTicketType.RegistrationHandoff,
            UserId = user.Id,
            ClientId = intent.ClientId,
            RedirectUri = intent.RedirectUri,
            CodeChallenge = intent.CodeChallenge,
            State = intent.State,
            Nonce = intent.Nonce,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(_options.TicketLifetimeSeconds),
            ConcurrencyToken = Guid.NewGuid()
        };
        dbContext.MobileAuthorizationTickets.Add(handoff);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RegistrationResponse(
            BuildAuthorizeUrl(publicOrigin, handoff, "handoff_code", handoffSecret),
            _options.TicketLifetimeSeconds,
            device.Id,
            deviceSecret);
    }

    public async Task<AuthorizationLaunchResponse> CreateAuthorizationLaunchAsync(
        CreateAuthorizationLaunchRequest request,
        Uri publicOrigin,
        CancellationToken cancellationToken)
    {
        ValidateMobileAuthorizationRequest(request.ClientId, request.RedirectUri, request.CodeChallenge);
        var now = timeProvider.GetUtcNow();
        var device = await dbContext.MobileDevices.SingleOrDefaultAsync(
            value => value.Id == request.DeviceId && value.RevokedAt == null,
            cancellationToken)
            ?? throw new MobileFlowException("invalid_device", "移动设备凭据无效。", 403);

        if (!SecretEncoding.Verify(request.DeviceSecret, device.SecretHash))
        {
            throw new MobileFlowException("invalid_device", "移动设备凭据无效。", 403);
        }

        device.LastUsedAt = now;
        device.ConcurrencyToken = Guid.NewGuid();
        var secret = SecretEncoding.Generate();
        var ticket = new MobileAuthorizationTicket
        {
            Id = Guid.CreateVersion7(),
            TicketHash = SecretEncoding.Hash(secret),
            TicketType = MobileAuthorizationTicketType.LoginLaunch,
            UserId = device.UserId,
            ClientId = request.ClientId,
            RedirectUri = request.RedirectUri,
            CodeChallenge = request.CodeChallenge,
            State = request.State,
            Nonce = request.Nonce,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(_options.TicketLifetimeSeconds),
            ConcurrencyToken = Guid.NewGuid()
        };
        dbContext.MobileAuthorizationTickets.Add(ticket);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthorizationLaunchResponse(
            BuildAuthorizeUrl(publicOrigin, ticket, "launch_ticket", secret),
            _options.TicketLifetimeSeconds);
    }

    public async Task<User?> ConsumeHandoffAsync(string secret, CancellationToken cancellationToken)
    {
        var ticket = await FindValidTicketAsync(
            secret,
            MobileAuthorizationTicketType.RegistrationHandoff,
            cancellationToken);
        if (ticket is null || ticket.UserId is null)
        {
            return null;
        }

        ticket.ConsumedAt = timeProvider.GetUtcNow();
        ticket.ConcurrencyToken = Guid.NewGuid();
        await dbContext.SaveChangesAsync(cancellationToken);
        return await userManager.FindByIdAsync(ticket.UserId.Value.ToString(CultureInfo.InvariantCulture));
    }

    public async Task<bool> IsValidLoginLaunchAsync(string? secret, CancellationToken cancellationToken) =>
        !string.IsNullOrWhiteSpace(secret) &&
        await FindValidTicketAsync(secret, MobileAuthorizationTicketType.LoginLaunch, cancellationToken) is not null;

    public async Task<bool> ConsumeLoginLaunchAsync(string secret, CancellationToken cancellationToken)
    {
        var ticket = await FindValidTicketAsync(secret, MobileAuthorizationTicketType.LoginLaunch, cancellationToken);
        if (ticket is null)
        {
            return false;
        }

        ticket.ConsumedAt = timeProvider.GetUtcNow();
        ticket.ConcurrencyToken = Guid.NewGuid();
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<MobileAuthorizationTicket?> FindValidTicketAsync(
        string secret,
        MobileAuthorizationTicketType type,
        CancellationToken cancellationToken)
    {
        var hash = SecretEncoding.Hash(secret);
        var now = timeProvider.GetUtcNow();
        var ticket = await dbContext.MobileAuthorizationTickets.SingleOrDefaultAsync(
            ticket => ticket.TicketHash == hash &&
                ticket.TicketType == type &&
                ticket.ConsumedAt == null,
            cancellationToken);

        // SQLite cannot translate every DateTimeOffset comparison. Fetch the
        // uniquely identified ticket first, then enforce expiry in memory.
        return ticket is not null && ticket.ExpiresAt > now ? ticket : null;
    }

    private void ValidateMobileAuthorizationRequest(string clientId, string redirectUri, string challenge)
    {
        if (!string.Equals(clientId, IdentityOpenIddictConstants.MobileClientId, StringComparison.Ordinal) ||
            !clients.IsMobile(clientId) ||
            !clients.IsRedirectUriAllowed(clientId, redirectUri) ||
            !SecretEncoding.IsS256Challenge(challenge))
        {
            throw new MobileFlowException("invalid_request", "移动授权参数无效。", 400);
        }
    }

    private void VerifyBootstrapCode(string value)
    {
        if (string.IsNullOrWhiteSpace(_options.BootstrapCode) ||
            !SecretEncoding.Verify(value, SecretEncoding.Hash(_options.BootstrapCode)))
        {
            throw new MobileFlowException("invalid_bootstrap_code", "初始化注册码无效。", 403);
        }
    }

    private static string ComputeIntentHash(MobileAuthorizationTicket intent)
    {
        var canonical = string.Join('|',
            intent.Id.ToString("N"),
            intent.ClientId,
            intent.RedirectUri,
            intent.CodeChallenge,
            intent.NormalizedUsernameHash);
        return SecretEncoding.Hash(canonical);
    }

    private static string BuildAuthorizeUrl(
        Uri origin,
        MobileAuthorizationTicket ticket,
        string ticketParameter,
        string ticketSecret)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = ticket.ClientId,
            ["redirect_uri"] = ticket.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = $"openid profile offline_access {IdentityOpenIddictConstants.ApiScope} {IdentityOpenIddictConstants.LoginApprovalScope}",
            ["code_challenge"] = ticket.CodeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = ticket.State,
            ["nonce"] = ticket.Nonce,
            [ticketParameter] = ticketSecret
        };
        return QueryHelpers.AddQueryString(new Uri(origin, "/connect/authorize").ToString(), parameters);
    }
}

public sealed class MobileFlowException(
    string code,
    string message,
    int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
