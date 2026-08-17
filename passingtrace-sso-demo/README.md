# PassingTrace SSO Demo

第二个独立的 Vue 3 / OIDC 公共客户端，用来从界面证明 PassingTrace Identity 的浏览器单点登录。

## 验证方法

1. 启动 Identity、主站和本验证站。
2. 打开 `http://localhost:5173`，在主站完成用户名密码登录。
3. 打开 `http://localhost:5174`。
4. 点击“发起 SSO 授权”；验证站会发送 `prompt=none`，不会自行显示密码页。
5. 如果直接显示相同用户名和 `sub`，SSO 验证成功；如果没有共享 Cookie，会返回 `login_required`。

两个网站有不同的 Origin、Client ID、授权码和 `sessionStorage`：

| 项目 | 主站 | 验证站 |
|---|---|---|
| 地址 | `http://localhost:5173` | `http://localhost:5174` |
| Client ID | `passingtrace-web` | `passingtrace-sso-demo` |
| Token 存储 | 主站自己的 sessionStorage | 验证站自己的 sessionStorage |
| 共享内容 | Identity HttpOnly Cookie | Identity HttpOnly Cookie |

## 启动

推荐统一启动：

```powershell
dotnet run --project AppHost/AppHost.csproj
```

单独启动：

```powershell
corepack pnpm install
corepack pnpm dev
```

默认 Identity 地址为 `https://localhost:56228`，可通过 `.env.local` 覆盖。
