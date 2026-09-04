# 星期八手机端

Android-only 的主客户端（手机 App），不发布到应用商店。它负责首次注册、移动 OIDC 登录、安全保存设备凭据、扫描批准 Web 登录，并承载记录、查看与 AI 洞察等核心体验。

## 模拟器运行

1. 用 HTTP profile 启动 `PassingTrace.Identity.AuthorizationServer`，默认端口 `56229`。
2. 启动 Android Emulator。
3. 在本目录运行：

```powershell
flutter run
```

4. 首次页面使用 Identity 地址 `http://10.0.2.2:56229`。
5. Development 默认安装引导码为 `passingtrace-local-setup`。

`10.0.2.2` 是模拟器访问宿主 Windows 的固定地址。真机测试时，请把 App 中的 Identity 地址和后端 `QrLogin:PublicOrigin` 都改为电脑的局域网地址，并确保防火墙和 ASP.NET Core 监听允许手机访问。

## Android 构建通道

移动端使用两个 Android flavor，并通过 `PASSINGTRACE_CHANNEL` 做条件编译：

- `internalRelease`：内测包，默认连接本机服务，允许保存自定义服务地址；
- `productionRelease`：公网包，强制连接 `auth.passingtrace.com` 和
  `passingtrace.com`，忽略本地服务地址。首次从旧版切换时会清理旧环境凭据。

不要手写 `flutter build apk` 发布公网包，统一使用以下脚本。

### 内测 Release APK

```powershell
.\tool\build-internal-apk.ps1 `
  -IdentityUrl http://localhost:56229 `
  -EventsApiUrl http://localhost:54934
```

### 公网 Production Release APK

```powershell
.\tool\build-production-apk.ps1 -BuildName 1.0.0 -BuildNumber 2
```

成品统一输出到 `build/releases/`，文件名包含环境、版本名和版本号。

正式包登录后会自动检查 `/api/v1/app-updates/android/latest`。新版 APK 保存在私有 S3 桶的 `releases/android/` 下，后端为当次下载生成短效预签名 URL。

仓库的 `Release Android` GitHub Actions 工作流负责测试、构建、上传 APK，并在 APK 上传成功后最后更新 `releases/android/latest.json`。

## Debug APK

```powershell
flutter build apk --debug
```

输出位于 `build/app/outputs/flutter-apk/app-debug.apk`，仅用于本地调试，不能作为公网发布包。

## 安全说明

- 设备密钥和 Token 使用 `flutter_secure_storage` 保存；
- OIDC 授权在系统浏览器完成，不使用内嵌 WebView；
- 授权码强制 PKCE S256；
- 二维码只包含两分钟一次性随机 code；
- Debug 允许模拟器 HTTP，长期使用应切换到手机可访问的 HTTPS Identity 地址；
- 清除 App 数据会删除设备密钥；服务器端设备记录不会自动删除。
