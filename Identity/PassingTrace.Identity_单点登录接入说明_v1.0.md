# PassingTrace Identity 单点登录接入说明 v1.0

## 1. 当前能力

Identity 是 PassingTrace 唯一的身份提供方，使用 ASP.NET Core Identity + OpenIddict 7.6 实现 OAuth 2.0 / OpenID Connect Authorization Code + PKCE。

- 注册只由 Android Flutter 原生页面发起；移动密码登录由受设备票据保护的 Identity 托管页完成。
- 用户名是忽略大小写的唯一账号，格式为 `^[A-Za-z0-9_-]{3,32}$`。
- 密码只保存安全哈希；连续失败 5 次锁定 15 分钟。
- Access Token 是使用非对称密钥签名的 JWT，有效期 15 分钟。
- Refresh Token 有效期 30 天，每次使用都会轮换，旧 Token 不可重放。
- 所有第一方业务 API 使用 audience `passingtrace-api`。
- 当前不支持邮箱、邮箱验证码、第三方登录、MFA 和第三方客户端。

## 2. 标准端点

| 端点 | 用途 |
|---|---|
| `/.well-known/openid-configuration` | OIDC Discovery，包含 issuer、授权、Token 和 JWKS 地址。 |
| `/connect/authorize` | 浏览器授权入口，只允许 Authorization Code + PKCE。 |
| `/connect/token` | 授权码换 Token、Refresh Token 续签。 |
| `/connect/logout` | 清除 Identity 浏览器登录 Cookie。 |
| `/account/qr-login/{id}` | Web/桌面端的手机扫码登录页。 |
| `/account/login` | 仅持有效移动启动票据时显示的 Identity 托管登录页。 |

登录页本身不返回 JWT。客户端必须先取得授权码，再从 `/connect/token` 换取 Access Token 和 Refresh Token。

## 3. Flutter 与桌面端流程

### Flutter 第一次注册与登录

1. Flutter 使用首次安装引导码调用移动注册接口，Identity 创建唯一用户与设备密钥。
2. 客户端生成随机 `code_verifier`，计算 `S256 code_challenge` 和随机 `state`。
3. Identity 返回一次性交接 URL，Flutter 使用系统浏览器打开 `/connect/authorize`，请求：
   - `response_type=code`
   - `scope=openid profile offline_access passingtrace.api`
   - `code_challenge_method=S256`
   - 对应的 `client_id` 和 `redirect_uri`
4. 注册交接成功后 Identity 建立 Cookie，通过自定义 URI Scheme 返回一次性授权码。
5. 客户端校验 `state`，提交授权码与原始 `code_verifier` 到 `/connect/token`。
6. 客户端将设备密钥和 Refresh Token 写入 Android 安全存储。

Web/桌面没有 Identity Cookie 时进入二维码页，由已登录 Flutter 扫码批准；它们不显示注册或密码登录表单。

### 再次启动

```text
Access Token 未过期 -> 直接调用 API
Access Token 过期且有 Refresh Token -> 后台续签
Refresh Token 失效 -> 重新打开系统浏览器授权
```

客户端可以读取 `expires_in` 决定刷新时间，但不能把本地解析 JWT 当成安全校验。JWT 的签名、issuer、audience 和过期时间由业务 API 验证。

### 单点登录如何发生

每个 App 都保存自己的 Refresh Token；Identity 登录 Cookie 只保存在系统浏览器。第二个 PassingTrace App 首次授权时没有本地 Token，但系统浏览器已有 Identity Cookie，因此可以直接获得授权码，无需再次输入用户名密码。

禁止使用内嵌 WebView，否则不同 App 无法可靠共享系统浏览器的 Identity Cookie。

## 4. 已注册客户端

| 客户端 | Redirect URI | Logout Redirect URI |
|---|---|---|
| `passingtrace-mobile` | `com.passingtrace.mobile:/oauth2redirect` | `com.passingtrace.mobile:/logout-callback` |
| `passingtrace-desktop` | `com.passingtrace.desktop:/oauth2redirect` | `com.passingtrace.desktop:/logout-callback` |
| `passingtrace-web` | `http://localhost:5173/auth/callback` | `http://localhost:5173/auth/logout-callback` |
这些客户端都是无 Client Secret 的 public client，并强制 PKCE。配置位于 AuthorizationServer 的 `appsettings.json`，启动时会幂等同步到 OpenIddict 表。

## 5. 业务 API 接入

业务 API 不引用 Identity 的 Domain、Application 或 Infrastructure 项目，只依赖 Discovery/JWKS：

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Identity:Authority"];
        options.Audience = "passingtrace-api";
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("passingtrace.api", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "passingtrace.api");
    });

app.UseAuthentication();
app.UseAuthorization();
```

使用 `[Authorize]` 只要求有效用户；需要业务 scope 的接口使用 `[Authorize(Policy = "passingtrace.api")]`。Token 缺失、签名错误、issuer/audience 错误或过期返回 401；Token 有效但 scope 不足返回 403。

## 6. 数据库与启动

- Aspire 中的新数据库资源名为 `identity`，旧 `user` 数据库不再使用。
- Initial Migration 包含 ASP.NET Core Identity 与 OpenIddict 所需表。
- Development 启动时自动执行 Migration；生产环境必须在部署阶段执行：

```powershell
dotnet ef database update `
  --project Identity/PassingTrace.Identity.Infrastructure `
  --startup-project Identity/PassingTrace.Identity.AuthorizationServer
```

本地启动：

```powershell
dotnet run --project AppHost/AppHost.csproj
```

## 7. 生产证书配置

开发环境使用 Development Encryption/Signing Certificate。生产环境必须提供两张持有私钥的持久化 PFX，并配置稳定 issuer：

```text
OpenIddict__Issuer=https://identity.example.com/
OpenIddict__Certificates__Signing__Path=/run/secrets/identity-signing.pfx
OpenIddict__Certificates__Signing__Password=<secret>
OpenIddict__Certificates__Encryption__Path=/run/secrets/identity-encryption.pfx
OpenIddict__Certificates__Encryption__Password=<secret>
```

证书密码和 PFX 不得提交到 Git。缺少生产证书时 AuthorizationServer 会拒绝启动。

## 8. 自动化验证

集成测试使用临时签名密钥与 SQLite 内存库，覆盖：

- 注册、大小写无关的唯一用户名和用户名格式。
- 五次失败登录锁定。
- Authorization Code + PKCE 和错误 verifier 拒绝。
- Discovery/JWKS 签名验证、错误 audience 和篡改 JWT 拒绝。
- Refresh Token 轮换与旧 Token 重放拒绝。
- 同一浏览器 Cookie 下的跨客户端单点登录。

运行：

```powershell
dotnet test tests/PassingTrace.Identity.IntegrationTests
```
