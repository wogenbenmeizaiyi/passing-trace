import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import 'auth_service.dart';
import 'theme/appearance_controller.dart';
import 'theme/passingtrace_mark.dart';
import 'theme/passingtrace_theme.dart';
import 'theme/quiet_trace_components.dart';
import 'theme/quiet_trace_icons.dart';
import 'build_environment.dart';
import 'update_service.dart';
import 'views/assistant_view.dart';
import 'views/events_list_view.dart';
import 'views/settings_view.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final appearance = AppearanceController();
  await appearance.load();
  runApp(PassingTraceApp(appearance: appearance));
}

class PassingTraceApp extends StatelessWidget {
  const PassingTraceApp({super.key, required this.appearance});

  final AppearanceController appearance;

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: appearance,
    builder: (context, _) => AppearanceScope(
      controller: appearance,
      child: MaterialApp(
        title: 'PassingTrace',
        debugShowCheckedModeBanner: false,
        theme: PassingTraceTheme.light(appearance.palette),
        darkTheme: PassingTraceTheme.dark(appearance.palette),
        themeMode: appearance.mode,
        home: const AccountGate(),
      ),
    ),
  );
}

class AccountGate extends StatefulWidget {
  const AccountGate({super.key});

  @override
  State<AccountGate> createState() => _AccountGateState();
}

class _AccountGateState extends State<AccountGate> {
  final _auth = AuthService();
  AuthSession? _session;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _restore();
  }

  Future<void> _restore() async {
    final session = await _auth.restore();
    if (!mounted) return;
    setState(() {
      _session = session;
      _loading = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }
    if (_session == null || !_session!.hasToken) {
      return RegistrationPage(
        auth: _auth,
        onRegistered: (session) => setState(() => _session = session),
      );
    }
    return AccountHome(
      auth: _auth,
      session: _session!,
      onSessionChanged: (session) => setState(() => _session = session),
      onReset: () => setState(() => _session = null),
    );
  }
}

class RegistrationPage extends StatefulWidget {
  const RegistrationPage({
    super.key,
    required this.auth,
    required this.onRegistered,
  });

  final AuthService auth;
  final ValueChanged<AuthSession> onRegistered;

  @override
  State<RegistrationPage> createState() => _RegistrationPageState();
}

class _RegistrationPageState extends State<RegistrationPage> {
  final _formKey = GlobalKey<FormState>();
  final _username = TextEditingController();
  final _password = TextEditingController();
  final _confirmPassword = TextEditingController();
  final _bootstrapCode = TextEditingController();
  bool _busy = false;
  bool _obscure = true;
  bool _creating = false;

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    _confirmPassword.dispose();
    _bootstrapCode.dispose();
    super.dispose();
  }

  Future<void> _register() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() => _busy = true);
    try {
      final session = await widget.auth.register(
        identityBaseUrl: AuthService.defaultIdentityUrl,
        username: _username.text.trim(),
        password: _password.text,
        bootstrapCode: _bootstrapCode.text.trim(),
        deviceName: 'Android 手机',
      );
      widget.onRegistered(session);
    } catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _login() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() => _busy = true);
    try {
      final session = await widget.auth.loginWithPassword(
        identityBaseUrl: AuthService.defaultIdentityUrl,
        username: _username.text.trim(),
        password: _password.text,
        deviceName: 'Android 手机',
      );
      widget.onRegistered(session);
    } catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _submit() => _creating ? _register() : _login();

  void _showError(Object error) {
    debugPrint('Mobile account action failed: $error');
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(error.toString())));
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 440),
              child: Form(
                key: _formKey,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Align(
                      alignment: Alignment.centerLeft,
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const PassingTraceMark(size: 36),
                          const SizedBox(width: 12),
                          Text(
                            'PassingTrace',
                            style: TextStyle(
                              color: colors.ink,
                              fontSize: 20,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 38),
                    SegmentedButton<bool>(
                      segments: const [
                        ButtonSegment(
                          value: false,
                          label: Text('登录'),
                          icon: Icon(Icons.login),
                        ),
                        ButtonSegment(
                          value: true,
                          label: Text('创建账号'),
                          icon: Icon(Icons.person_add_alt_1),
                        ),
                      ],
                      selected: {_creating},
                      onSelectionChanged: _busy
                          ? null
                          : (selection) {
                              setState(() => _creating = selection.first);
                              _formKey.currentState?.reset();
                            },
                      showSelectedIcon: false,
                    ),
                    const SizedBox(height: 38),
                    Text(
                      'YOUR LIFE, IN CONTEXT',
                      style: TextStyle(
                        color: colors.accent,
                        fontSize: 11,
                        fontWeight: FontWeight.w800,
                        letterSpacing: 2.1,
                      ),
                    ),
                    const SizedBox(height: 18),
                    Text(
                      _creating ? '开始记录你的时间。' : '欢迎回到你的时间线。',
                      style: TextStyle(
                        color: colors.ink,
                        fontSize: 34,
                        height: 1.25,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(height: 10),
                    Text(
                      _creating
                          ? '创建账号后，这台手机也可以安全批准网页端登录。'
                          : '登录后即可进入主页，并使用手机批准其他客户端登录。',
                      style: TextStyle(color: colors.inkSecondary, height: 1.6),
                    ),
                    const SizedBox(height: 34),
                    TextFormField(
                      controller: _username,
                      decoration: _fieldDecoration(
                        label: '用户名',
                        icon: Icons.person_outline,
                      ),
                      autocorrect: false,
                      autofillHints: [
                        _creating
                            ? AutofillHints.newUsername
                            : AutofillHints.username,
                      ],
                      textInputAction: TextInputAction.next,
                      validator: (value) =>
                          RegExp(r'^[A-Za-z0-9_-]{3,32}$').hasMatch(value ?? '')
                          ? null
                          : '请输入 3～32 位字母、数字、_ 或 -',
                    ),
                    const SizedBox(height: 14),
                    TextFormField(
                      controller: _password,
                      obscureText: _obscure,
                      autofillHints: [
                        _creating
                            ? AutofillHints.newPassword
                            : AutofillHints.password,
                      ],
                      textInputAction: _creating
                          ? TextInputAction.next
                          : TextInputAction.done,
                      onFieldSubmitted: _creating ? null : (_) => _submit(),
                      decoration: _fieldDecoration(
                        label: '密码',
                        icon: Icons.lock_outline,
                        suffix: IconButton(
                          onPressed: () => setState(() => _obscure = !_obscure),
                          icon: Icon(
                            _obscure ? Icons.visibility : Icons.visibility_off,
                          ),
                        ),
                      ),
                      validator: (value) {
                        if (value == null || value.isEmpty) return '请输入密码';
                        if (_creating && value.length < minimumPasswordLength) {
                          return '密码至少需要 $minimumPasswordLength 个字符';
                        }
                        return null;
                      },
                    ),
                    if (_creating) ...[
                      const SizedBox(height: 14),
                      TextFormField(
                        controller: _confirmPassword,
                        obscureText: true,
                        textInputAction: TextInputAction.next,
                        decoration: _fieldDecoration(
                          label: '确认密码',
                          icon: Icons.lock_reset_outlined,
                        ),
                        validator: (value) =>
                            value != _password.text ? '两次密码不一致' : null,
                      ),
                      const SizedBox(height: 14),
                      TextFormField(
                        controller: _bootstrapCode,
                        obscureText: true,
                        autocorrect: false,
                        enableSuggestions: false,
                        textInputAction: TextInputAction.done,
                        onFieldSubmitted: (_) => _submit(),
                        decoration: _fieldDecoration(
                          label: '初始注册码',
                          icon: Icons.vpn_key_outlined,
                        ),
                        validator: (value) =>
                            value == null || value.trim().isEmpty
                            ? '请输入部署者提供的初始注册码'
                            : null,
                      ),
                    ],
                    const SizedBox(height: 26),
                    FilledButton(
                      onPressed: _busy ? null : _submit,
                      style: FilledButton.styleFrom(
                        minimumSize: const Size.fromHeight(54),
                        shape: const RoundedRectangleBorder(),
                      ),
                      child: _busy
                          ? SizedBox.square(
                              dimension: 20,
                              child: CircularProgressIndicator(
                                color: colors.onPrimary,
                                strokeWidth: 2,
                              ),
                            )
                          : Row(
                              mainAxisAlignment: MainAxisAlignment.center,
                              children: [
                                Text(_creating ? '创建账号' : '登录'),
                                const SizedBox(width: 10),
                                const Icon(Icons.arrow_forward, size: 18),
                              ],
                            ),
                    ),
                    const SizedBox(height: 18),
                    Text(
                      _creating
                          ? '账号信息仅发送给你的 PassingTrace Identity 服务。'
                          : '新手机登录后会自动绑定为可信设备。',
                      textAlign: TextAlign.center,
                      style: TextStyle(color: colors.inkTertiary, fontSize: 12),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  InputDecoration _fieldDecoration({
    required String label,
    required IconData icon,
    Widget? suffix,
  }) => InputDecoration(
    labelText: label,
    prefixIcon: Icon(icon),
    suffixIcon: suffix,
  );
}

class AccountHome extends StatefulWidget {
  const AccountHome({
    super.key,
    required this.auth,
    required this.session,
    required this.onSessionChanged,
    required this.onReset,
  });

  final AuthService auth;
  final AuthSession session;
  final ValueChanged<AuthSession> onSessionChanged;
  final VoidCallback onReset;

  @override
  State<AccountHome> createState() => _AccountHomeState();
}

class _AccountHomeState extends State<AccountHome> {
  bool _busy = false;
  int _section = 1;

  @override
  void initState() {
    super.initState();
    if (BuildEnvironment.current.isProduction) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _checkForUpdate());
    }
  }

  Future<void> _checkForUpdate() async {
    try {
      final service = AppUpdateService();
      final update = await service.check();
      if (!mounted || update == null || !update.updateAvailable) return;
      final accepted = await showDialog<bool>(
        context: context,
        barrierDismissible: !update.required,
        builder: (dialogContext) => AlertDialog(
          title: Text('发现新版本 ${update.versionName}'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('安装包大小：${_formatBytes(update.size)}'),
              if (update.notes case final notes?
                  when notes.trim().isNotEmpty) ...[
                const SizedBox(height: 12),
                Text(notes),
              ],
            ],
          ),
          actions: [
            if (!update.required)
              TextButton(
                onPressed: () => Navigator.of(dialogContext).pop(false),
                child: const Text('稍后'),
              ),
            FilledButton(
              onPressed: () => Navigator.of(dialogContext).pop(true),
              child: const Text('下载更新'),
            ),
          ],
        ),
      );
      if (accepted == true) await service.download(update);
    } catch (error) {
      // 更新检查不影响用户进入主界面。
      debugPrint('App update check failed: $error');
    }
  }

  String _formatBytes(int bytes) {
    if (bytes >= 1024 * 1024) {
      return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
    }
    return '${(bytes / 1024).toStringAsFixed(1)} KB';
  }

  Future<void> _scanFromDrawer() async {
    Navigator.of(context).pop();
    await Future<void>.delayed(const Duration(milliseconds: 180));
    if (mounted) await _scan();
  }

  void _selectSection(int section) {
    Navigator.of(context).pop();
    if (_section == section) return;
    setState(() => _section = section);
  }

  Future<void> _openSettings() async {
    Navigator.of(context).pop();
    await Future<void>.delayed(const Duration(milliseconds: 160));
    if (!mounted) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute(builder: (_) => SettingsView(onSignOut: _clear)),
    );
  }

  Future<void> _restoreAfterSessionExpiry(bool? sessionExpired) async {
    if (sessionExpired != true) return;
    final restored = await widget.auth.restore();
    if (!mounted) return;
    if (restored == null) {
      widget.onReset();
      return;
    }
    widget.onSessionChanged(restored);
    _message('登录状态已过期，请重新登录。');
  }

  Future<void> _scan() async {
    final raw = await Navigator.of(context)
        .push<String>(MaterialPageRoute(builder: (_) => const QrScannerPage()));
    if (raw == null || !mounted) return;
    await _run(() async {
      final details = await widget.auth.getQrDetails(widget.session, raw);
      widget.onSessionChanged(details.session);
      if (!mounted) return;
      final approved = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('批准网页登录？'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('应用：${details.clientDisplayName}'),
              Text('客户端：${details.clientId}'),
              Text('来源：${details.sourceIp}'),
              Text('浏览器：${details.browser}'),
              Text('有效至：${details.expiresAt.toLocal()}'),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('拒绝'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('批准'),
            ),
          ],
        ),
      );
      if (approved == null) return;
      final session = await widget.auth.decideQr(details, approved);
      widget.onSessionChanged(session);
      _message(approved ? '已批准，网页将自动完成登录。' : '已拒绝网页登录。');
    });
  }

  Future<void> _clear() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('移除此手机凭据？'),
        content: const Text('这会清除本机 Token 和设备密钥，但不会删除服务器账号。'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('清除'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
    await widget.auth.clearLocalAccount();
    widget.onReset();
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
    } on DeviceCredentialsInvalidException {
      await widget.auth.clearLocalAccount();
      if (mounted) widget.onReset();
    } catch (error, stackTrace) {
      debugPrint('Account action failed: $error');
      debugPrintStack(stackTrace: stackTrace);
      _message(error.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _message(String text) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(text)));
  }

  @override
  Widget build(BuildContext context) => Stack(
    children: [
      if (_section == 0)
        AssistantView(
          auth: widget.auth,
          session: widget.session,
          drawer: _buildDrawer(),
          bottomNavigationBar: _buildPrimaryNavigation(),
          onSessionExpired: () => _restoreAfterSessionExpiry(true),
        )
      else if (_section == 1)
        EventsListView(
          auth: widget.auth,
          session: widget.session,
          drawer: _buildDrawer(),
          bottomNavigationBar: _buildPrimaryNavigation(),
          onSessionExpired: () => _restoreAfterSessionExpiry(true),
        )
      else
        MemoriesView(
          auth: widget.auth,
          session: widget.session,
          drawer: _buildDrawer(),
          onSessionExpired: () => _restoreAfterSessionExpiry(true),
        ),
      if (_busy)
        const Positioned(
          left: 0,
          right: 0,
          top: 0,
          child: LinearProgressIndicator(minHeight: 3),
        ),
    ],
  );

  Widget _buildPrimaryNavigation() => TraceBottomNavigation(
    selectedIndex: _section == 1 ? 0 : 1,
    onSelected: (index) {
      final section = index == 0 ? 1 : 0;
      if (_section != section) setState(() => _section = section);
    },
  );

  Widget _buildDrawer() => Drawer(
    width: 304,
    child: SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(14, 20, 14, 12),
        child: Column(
          children: [
            SizedBox(
              height: 72,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(10, 2, 2, 14),
                child: Row(
                  children: [
                    Expanded(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '我的 PassingTrace',
                            style: TextStyle(
                              color: context.traceColors.ink,
                              fontSize: 17,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          const SizedBox(height: 3),
                          Text(
                            '仅自己可见的生活档案',
                            style: TextStyle(
                              color: context.traceColors.inkTertiary,
                              fontSize: 11,
                            ),
                          ),
                        ],
                      ),
                    ),
                    SizedBox.square(
                      dimension: 48,
                      child: TraceIconButton(
                        glyph: TraceGlyph.scan,
                        tooltip: '扫一扫',
                        onPressed: _busy ? null : _scanFromDrawer,
                        color: context.traceColors.primaryStrong,
                        backgroundColor: context.traceColors.primarySoft,
                        borderColor: context.traceColors.lineStrong,
                      ),
                    ),
                  ],
                ),
              ),
            ),
            Divider(height: 1, color: context.traceColors.line),
            const SizedBox(height: 12),
            TraceDrawerItem(
              glyph: TraceGlyph.journal,
              label: '我的记录',
              selected: _section == 1,
              onTap: () => _selectSection(1),
            ),
            TraceDrawerItem(
              glyph: TraceGlyph.sparkle,
              label: '问问记录',
              selected: _section == 0,
              onTap: () => _selectSection(0),
            ),
            TraceDrawerItem(
              glyph: TraceGlyph.memory,
              label: '我的记忆',
              selected: _section == 2,
              onTap: () => _selectSection(2),
            ),
            const Spacer(),
            Divider(height: 1, color: context.traceColors.line),
            const SizedBox(height: 10),
            TraceDrawerItem(
              glyph: TraceGlyph.settings,
              label: '设置',
              onTap: _busy ? null : _openSettings,
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
              child: Text(
                '点击菜单外的区域即可收起',
                style: TextStyle(
                  color: context.traceColors.inkTertiary,
                  fontSize: 10,
                ),
              ),
            ),
          ],
        ),
      ),
    ),
  );
}

class QrScannerPage extends StatefulWidget {
  const QrScannerPage({super.key});

  @override
  State<QrScannerPage> createState() => _QrScannerPageState();
}

class _QrScannerPageState extends State<QrScannerPage> {
  final _controller = MobileScannerController(
    detectionSpeed: DetectionSpeed.noDuplicates,
    formats: const [BarcodeFormat.qrCode],
  );
  bool _returned = false;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _detected(BarcodeCapture capture) {
    if (_returned) return;
    final value = capture.barcodes.firstOrNull?.rawValue;
    if (value == null || value.isEmpty) return;
    _returned = true;
    _controller.stop();
    Navigator.of(context).pop(value);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    backgroundColor: Colors.black,
    extendBodyBehindAppBar: true,
    appBar: AppBar(
      backgroundColor: Colors.transparent,
      foregroundColor: Colors.white,
      title: const Text('扫一扫'),
      actions: [
        Padding(
          padding: const EdgeInsets.only(right: 8),
          child: IconButton.filledTonal(
            tooltip: '打开手电筒',
            onPressed: _controller.toggleTorch,
            style: IconButton.styleFrom(
              backgroundColor: Colors.black.withValues(alpha: 0.38),
              foregroundColor: Colors.white,
            ),
            icon: const Icon(Icons.flashlight_on_outlined),
          ),
        ),
      ],
    ),
    body: Stack(
      fit: StackFit.expand,
      children: [
        MobileScanner(controller: _controller, onDetect: _detected),
        Center(
          child: SizedBox(
            width: 260,
            height: 260,
            child: CustomPaint(painter: const _ScannerFramePainter()),
          ),
        ),
        Positioned(
          left: 32,
          right: 32,
          bottom: 54,
          child: DecoratedBox(
            decoration: BoxDecoration(
              color: Colors.black.withValues(alpha: 0.55),
              borderRadius: BorderRadius.circular(24),
            ),
            child: const Padding(
              padding: EdgeInsets.symmetric(horizontal: 18, vertical: 12),
              child: Text(
                '将电脑端登录二维码放入框内',
                textAlign: TextAlign.center,
                style: TextStyle(color: Colors.white, fontSize: 15),
              ),
            ),
          ),
        ),
      ],
    ),
  );
}

class _ScannerFramePainter extends CustomPainter {
  const _ScannerFramePainter();

  @override
  void paint(Canvas canvas, Size size) {
    const corner = 42.0;
    const radius = 18.0;
    final paint = Paint()
      ..color = Colors.white
      ..strokeWidth = 4
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round;
    final path = Path()
      ..moveTo(0, corner)
      ..lineTo(0, radius)
      ..quadraticBezierTo(0, 0, radius, 0)
      ..lineTo(corner, 0)
      ..moveTo(size.width - corner, 0)
      ..lineTo(size.width - radius, 0)
      ..quadraticBezierTo(size.width, 0, size.width, radius)
      ..lineTo(size.width, corner)
      ..moveTo(size.width, size.height - corner)
      ..lineTo(size.width, size.height - radius)
      ..quadraticBezierTo(
        size.width,
        size.height,
        size.width - radius,
        size.height,
      )
      ..lineTo(size.width - corner, size.height)
      ..moveTo(corner, size.height)
      ..lineTo(radius, size.height)
      ..quadraticBezierTo(0, size.height, 0, size.height - radius)
      ..lineTo(0, size.height - corner);
    canvas.drawPath(path, paint);
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}
