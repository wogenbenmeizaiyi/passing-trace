# PassingTrace Mobile

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

## 本地 APK

```powershell
flutter build apk --debug
```

输出位于 `build/app/outputs/flutter-apk/app-debug.apk`，可以直接侧载到自己的手机。项目设置了 `publish_to: none`，不需要 Google Play 项目或发布签名。

## 安全说明

- 设备密钥和 Token 使用 `flutter_secure_storage` 保存；
- OIDC 授权在系统浏览器完成，不使用内嵌 WebView；
- 授权码强制 PKCE S256；
- 二维码只包含两分钟一次性随机 code；
- Debug 允许模拟器 HTTP，长期使用应切换到手机可访问的 HTTPS Identity 地址；
- 清除 App 数据会删除设备密钥；服务器端设备记录不会自动删除。
