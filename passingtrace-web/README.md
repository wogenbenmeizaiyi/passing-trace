# PassingTrace Web

PassingTrace 的 Vue 3 第一方 Web 客户端。它不接收用户名或密码，而是通过 PassingTrace Identity 使用 OpenID Connect Authorization Code + PKCE 完成登录。

## 本地运行

推荐从仓库根目录启动 Aspire，Identity、PostgreSQL 与本项目会一起运行：

```powershell
dotnet run --project AppHost/AppHost.csproj
```

也可以分别启动 Identity 和前端。前端默认地址为 `http://localhost:5173`，Identity 默认地址来自 `.env.example`：

```powershell
corepack pnpm install
corepack pnpm dev
```

需要覆盖 Identity 地址时，复制 `.env.example` 为 `.env.local` 并修改 `VITE_IDENTITY_AUTHORITY`。

## 登录边界

- `passingtrace-web` 是没有 Client Secret 的公共客户端。
- Vue 只处理授权码、PKCE verifier 和 Token，不直接处理密码。
- Token 存在 `sessionStorage`，浏览器标签页关闭后会清除；续期由 Refresh Token 完成。
- 登录回调固定为 `http://localhost:5173/auth/callback`，退出回调固定为 `http://localhost:5173/auth/logout-callback`；生产地址需要同时更新 Identity 客户端配置。
- 申请 scopes：`openid profile offline_access passingtrace.api`。

## 验证

```powershell
corepack pnpm type-check
corepack pnpm test:unit
corepack pnpm build
```
