# AGENTS.md

PassingTrace — 基于 OIDC 的个人身份与活动事件平台，包含 .NET 10 后端、Android Flutter 主客户端（手机 App）、Vue 3 辅助 Web 客户端（电脑扫码批准登录）。

## Setup commands

- 后端依赖还原（首次或更新包后）：`dotnet restore PassingTrace.slnx`
- Web 客户端安装：`corepack pnpm install`（在 `passingtrace-web/` 与 `passingtrace-sso-demo/` 中分别执行）
- 移动端依赖：`flutter pub get`（在 `passingtrace-mobile/` 中执行）
- 本地一键起全栈： `dotnet run --project AppHost/AppHost.csproj`（Aspire 13 编排，PostgreSQL + Identity + Events 一起拉起；推荐入口）
- 仅起前端：`corepack pnpm dev`（`passingtrace-web` 默认 `http://localhost:5173`；`passingtrace-sso-demo` 默认 `:5174`）
- 仅起移动端：`flutter run`（需先用 HTTP profile 启动 `PassingTrace.Identity.AuthorizationServer`，默认端口 `56229`；模拟器内访问宿主用 `http://10.0.2.2:56229`）

## Build

- 解决方案：`dotnet build PassingTrace.slnx`
- Web：`corepack pnpm build`（含 `vue-tsc` 类型检查 + Vite 构建）
- 移动端：`flutter build apk --debug`（输出在 `build/app/outputs/flutter-apk/app-debug.apk`）

## Test

- 后端（xUnit + `Microsoft.AspNetCore.Mvc.Testing`，SQLite 内存库）：`dotnet test`
- Web（Vitest + jsdom + @vue/test-utils）：`corepack pnpm test:unit`
- 移动端（flutter_test）：`flutter test`
- 新行为必须有覆盖；后端在 `tests/<同层>.IntegrationTests/`、前端在 `src/**/*.spec.ts`、移动端在 `test/` 下就近放

## Lint / format

- 后端：`dotnet format`（C#，仓库未配置 editorconfig，按 Roslyn 默认）
- Web：`corepack pnpm lint`（oxlint + ESLint + Prettier，3 者并行）`corepack pnpm format`
- 移动端：`flutter analyze`
- 提交前跑一次对应范围的 lint/format

## Project layout

- `AppHost/` — .NET Aspire 13 AppHost 编排入口（PostgreSQL + Identity + Events）
- `PassingTrace.Core/` — 领域模型与共享抽象
- `PassingTrace.Infrastructure/` — EF Core 10 + Npgsql 10，`TraceDbContext` 与 `Persistence/`
- `Events/PassingTrace.Events.Api/` — 事件接入 API（JWT Bearer）
- `Identity/` — Identity 领域 `Domain` / 应用 `Application` / 基础设施 `Infrastructure` / `AuthorizationServer`（OpenIddict 7.6 + QRCoder）
- `tests/PassingTrace.Identity.IntegrationTests/` — 集成测试（xUnit）
- `passingtrace-web/` — Vue 3 + Vite 8 + TS 6 第一方客户端（OIDC 公共客户端，sessionStorage Token）
- `passingtrace-sso-demo/` — 独立 SSO 验证站（`:5174`），用于可视化验证 Identity SSO
- `passingtrace-mobile/` — Android-only Flutter 应用（`mobile_scanner` + `flutter_appauth` + `flutter_secure_storage`）
- `theme-showcase.html` — 视觉/品牌总览（"纸·墨·朱砂"）

## Code style

- C#：`.NET 10`，全局 `ImplicitUsings=enable` + `Nullable=enable`（见 `Directory.Build.props`），全仓统一通过 `Directory.Build.props` 注入
- Web：TypeScript strict（`@vue/tsconfig` + `@tsconfig/node24`）；Prettier 默认 + oxlint + ESLint Vue/TS 规则集；提交前跑 `corepack pnpm format`
- Flutter：`flutter_lints: ^6.0.0`（`analysis_options.yaml`）
- 命名：解决方案、目录、csproj 走 PascalCase；前端 `src/` 走 kebab-case 文件名 + PascalCase 组件；移动端 Dart 走 `flutter_lints` 推荐的 lowerCamelCase
- 公共 API 边界：Identity / Events 走 `FrameworkReference Microsoft.AspNetCore.App`；`PassingTrace.Core` 不引用 ASP.NET 或 EF

## PR & commit conventions

- 从 `origin/main`（默认分支 `main`）拉特性分支，**禁止**直推 `main`
- Commit 信息使用 Conventional Commits（`feat:` / `fix:` / `docs:` / `refactor:` / `chore:` / `test:`）
- 提交前：`dotnet test` + 对应范围 `lint`/类型检查必须全绿
- 仓库暂未配置 `.github/workflows/`（无 CI），需在本地完成上述验证后再开 PR

## Security

- 禁止提交任何密钥、令牌、PFX/PEM；`.env` / `appsettings.*.local.json` / `secrets*.json` / `*.pfx` / `*.pem` 已在 `.gitignore`
- Identity 本地开发用 `dotnet user-secrets`（`AppHost/UserSecretsId=7d6fad56-d5eb-4c37-b9b7-51d4669701b7`）
- Web 覆盖 Identity 地址用 `.env.local`（从 `.env.example` 复制），不要改 `.env.example`
- 移动端密钥与 Token 走 `flutter_secure_storage`；真机调试需把 Identity 地址和后端 `QrLogin:PublicOrigin` 同步改成局域网地址，并保证防火墙放行
- 默认 Development 安装引导码 `passingtrace-local-setup`，仅用于本地初始化，**禁止**进入生产配置
