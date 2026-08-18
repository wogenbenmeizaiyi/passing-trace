using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PassingTrace.Identity.AuthorizationServer.Security;
using PassingTrace.Identity.Domain.Entities;
using PassingTrace.Identity.Domain.Enums;
using PassingTrace.Identity.Infrastructure;

namespace PassingTrace.Identity.AuthorizationServer.QrLogin;

public sealed class QrLoginService(
    IdentityDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<QrLoginOptions> options,
    TimeProvider timeProvider)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "PassingTrace.Identity.QrLogin.AuthorizeRequest.v1");
    private readonly QrLoginOptions _options = options.Value;

    public async Task<CreatedQrLogin> CreateAsync(
        string clientId,
        string authorizeRequest,
        string sourceIp,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var code = SecretEncoding.Generate();
        var binding = SecretEncoding.Generate();
        var entity = new QrLoginTransaction
        {
            Id = Guid.CreateVersion7(),
            CodeHash = SecretEncoding.Hash(code),
            BrowserBindingHash = SecretEncoding.Hash(binding),
            ClientId = clientId,
            ProtectedAuthorizeRequest = _protector.Protect(authorizeRequest),
            SourceIp = sourceIp,
            UserAgent = userAgent.Length <= 512 ? userAgent : userAgent[..512],
            Status = QrLoginStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(_options.LifetimeSeconds),
            ConcurrencyToken = Guid.NewGuid()
        };
        dbContext.QrLoginTransactions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Dev-only：若配置开启，则用指定用户立刻批准该事务，浏览器轮询直接看到 Approved。
        if (_options.DevAutoApprove && !string.IsNullOrWhiteSpace(_options.DevApproveUsername))
        {
            var approver = await dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserName == _options.DevApproveUsername, cancellationToken);
            if (approver is not null)
            {
                entity.Status = QrLoginStatus.Approved;
                entity.ApprovedUserId = approver.Id;
                entity.ApprovedAt = timeProvider.GetUtcNow();
                entity.ConcurrencyToken = Guid.NewGuid();
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return new CreatedQrLogin(entity.Id, code, binding, entity.ExpiresAt);
    }

    public async Task<QrLoginTransaction?> GetByIdAndCodeAsync(
        Guid id,
        string code,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.QrLoginTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        return entity is not null && SecretEncoding.Verify(code, entity.CodeHash)
            ? entity
            : null;
    }

    public async Task<QrLoginTransaction?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var hash = SecretEncoding.Hash(code);
        var entity = await dbContext.QrLoginTransactions
            .SingleOrDefaultAsync(value => value.CodeHash == hash, cancellationToken);
        if (entity is not null)
        {
            await ExpireIfNeededAsync(entity, cancellationToken);
        }
        return entity;
    }

    public async Task<QrLoginStatus?> GetStatusAsync(
        Guid id,
        string? browserBinding,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.QrLoginTransactions
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (entity is null || string.IsNullOrWhiteSpace(browserBinding) ||
            !SecretEncoding.Verify(browserBinding, entity.BrowserBindingHash))
        {
            return null;
        }
        await ExpireIfNeededAsync(entity, cancellationToken);
        return entity.Status;
    }

    public async Task<QrLoginStatus> DecideAsync(
        string code,
        long userId,
        bool approve,
        CancellationToken cancellationToken)
    {
        var entity = await GetByCodeAsync(code, cancellationToken)
            ?? throw new QrLoginException("not_found", "扫码事务不存在。", 404);
        if (entity.Status != QrLoginStatus.Pending)
        {
            throw new QrLoginException("invalid_state", "扫码事务已处理。", 409);
        }

        var now = timeProvider.GetUtcNow();
        entity.Status = approve ? QrLoginStatus.Approved : QrLoginStatus.Rejected;
        entity.ApprovedUserId = approve ? userId : null;
        entity.ApprovedAt = approve ? now : null;
        entity.RejectedAt = approve ? null : now;
        entity.ConcurrencyToken = Guid.NewGuid();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new QrLoginException("concurrent_update", "扫码事务已被其他请求处理。", 409);
        }
        return entity.Status;
    }

    public async Task<ConsumedQrLogin> ConsumeAsync(
        Guid id,
        string? browserBinding,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.QrLoginTransactions
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
            ?? throw new QrLoginException("not_found", "扫码事务不存在。", 404);
        if (string.IsNullOrWhiteSpace(browserBinding) ||
            !SecretEncoding.Verify(browserBinding, entity.BrowserBindingHash))
        {
            throw new QrLoginException("invalid_browser", "浏览器绑定无效。", 403);
        }
        await ExpireIfNeededAsync(entity, cancellationToken);
        if (entity.Status != QrLoginStatus.Approved || entity.ApprovedUserId is null)
        {
            throw new QrLoginException("invalid_state", "扫码事务尚未批准。", 409);
        }

        entity.Status = QrLoginStatus.Consumed;
        entity.ConsumedAt = timeProvider.GetUtcNow();
        entity.ConcurrencyToken = Guid.NewGuid();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new QrLoginException("concurrent_update", "扫码事务已被消费。", 409);
        }

        return new ConsumedQrLogin(
            entity.ApprovedUserId.Value,
            _protector.Unprotect(entity.ProtectedAuthorizeRequest));
    }

    private async Task ExpireIfNeededAsync(
        QrLoginTransaction entity,
        CancellationToken cancellationToken)
    {
        if (entity.Status is QrLoginStatus.Pending or QrLoginStatus.Approved &&
            entity.ExpiresAt <= timeProvider.GetUtcNow())
        {
            entity.Status = QrLoginStatus.Expired;
            entity.ConcurrencyToken = Guid.NewGuid();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public static string CookieName(Guid id) => $"PassingTrace.QrBrowser.{id:N}";
}

public sealed record CreatedQrLogin(Guid Id, string Code, string BrowserBinding, DateTimeOffset ExpiresAt);
public sealed record ConsumedQrLogin(long UserId, string AuthorizeRequest);

public sealed class QrLoginException(string code, string message, int statusCode)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
