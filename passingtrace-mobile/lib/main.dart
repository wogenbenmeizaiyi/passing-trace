import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import 'auth_service.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const PassingTraceApp());
}

class PassingTraceApp extends StatelessWidget {
  const PassingTraceApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'PassingTrace',
    debugShowCheckedModeBanner: false,
    theme: ThemeData(
      colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xff315d4b)),
      useMaterial3: true,
      inputDecorationTheme: const InputDecorationTheme(
        border: OutlineInputBorder(),
      ),
    ),
    home: const AccountGate(),
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
    if (_session == null) {
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
  final _identity = TextEditingController(text: AuthService.defaultIdentityUrl);
  final _username = TextEditingController();
  final _password = TextEditingController();
  final _confirmPassword = TextEditingController();
  final _bootstrap = TextEditingController(text: 'passingtrace-local-setup');
  final _device = TextEditingController(text: 'My Android');
  bool _busy = false;
  bool _obscure = true;

  @override
  void dispose() {
    _identity.dispose();
    _username.dispose();
    _password.dispose();
    _confirmPassword.dispose();
    _bootstrap.dispose();
    _device.dispose();
    super.dispose();
  }

  Future<void> _register() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() => _busy = true);
    try {
      final session = await widget.auth.register(
        identityBaseUrl: _identity.text,
        username: _username.text.trim(),
        password: _password.text,
        bootstrapCode: _bootstrap.text,
        deviceName: _device.text.trim(),
      );
      widget.onRegistered(session);
    } catch (error) {
      _showError(error);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _showError(Object error) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(error.toString())));
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 520),
            child: Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(Icons.fingerprint, size: 62),
                  const SizedBox(height: 16),
                  Text(
                    '初始化 PassingTrace 账号',
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.headlineSmall,
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    '注册只在这台 Android 客户端开放。完成后，此设备可以批准网页扫码登录。',
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 28),
                  TextFormField(
                    controller: _identity,
                    decoration: const InputDecoration(
                      labelText: 'Identity 地址',
                      helperText: '模拟器默认使用 10.0.2.2 访问开发机',
                    ),
                    keyboardType: TextInputType.url,
                    validator: _required,
                  ),
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _username,
                    decoration: const InputDecoration(labelText: '唯一用户名'),
                    autocorrect: false,
                    validator: (value) =>
                        RegExp(r'^[A-Za-z0-9_-]{3,32}$').hasMatch(value ?? '')
                        ? null
                        : '请输入 3～32 位字母、数字、_ 或 -',
                  ),
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _password,
                    obscureText: _obscure,
                    decoration: InputDecoration(
                      labelText: '密码',
                      suffixIcon: IconButton(
                        onPressed: () => setState(() => _obscure = !_obscure),
                        icon: Icon(
                          _obscure ? Icons.visibility : Icons.visibility_off,
                        ),
                      ),
                    ),
                    validator: (value) =>
                        (value?.length ?? 0) < 12 ? '密码至少需要 12 个字符' : null,
                  ),
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _confirmPassword,
                    obscureText: true,
                    decoration: const InputDecoration(labelText: '确认密码'),
                    validator: (value) =>
                        value != _password.text ? '两次密码不一致' : null,
                  ),
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _bootstrap,
                    obscureText: true,
                    decoration: const InputDecoration(
                      labelText: '首次安装引导码',
                      helperText: '开发环境默认值来自 appsettings.Development.json',
                    ),
                    validator: _required,
                  ),
                  const SizedBox(height: 14),
                  TextFormField(
                    controller: _device,
                    decoration: const InputDecoration(labelText: '设备名称'),
                    validator: _required,
                  ),
                  const SizedBox(height: 22),
                  FilledButton.icon(
                    onPressed: _busy ? null : _register,
                    icon: _busy
                        ? const SizedBox.square(
                            dimension: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.person_add_alt_1),
                    label: const Text('注册并登录'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );

  static String? _required(String? value) =>
      value == null || value.trim().isEmpty ? '此项必填' : null;
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

  Future<void> _login() async {
    await _run(() async {
      final session = await widget.auth.login(widget.session);
      widget.onSessionChanged(session);
      _message('登录成功，Token 已安全保存。');
    });
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
    } catch (error) {
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
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('PassingTrace 账号'),
      actions: [
        IconButton(
          onPressed: _busy ? null : _clear,
          icon: const Icon(Icons.delete_outline),
        ),
      ],
    ),
    body: Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(20),
                  child: Column(
                    children: [
                      Icon(
                        widget.session.hasToken
                            ? Icons.verified_user
                            : Icons.phonelink_lock,
                        size: 58,
                      ),
                      const SizedBox(height: 12),
                      Text(
                        widget.session.hasToken ? '手机账号已登录' : '设备凭据已建立',
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 8),
                      Text(widget.session.identityBaseUrl),
                      const SizedBox(height: 4),
                      Text('设备 ${widget.session.deviceId}'),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 20),
              FilledButton.icon(
                onPressed: _busy ? null : _login,
                icon: const Icon(Icons.login),
                label: Text(widget.session.hasToken ? '重新登录' : '登录账号'),
              ),
              const SizedBox(height: 12),
              FilledButton.tonalIcon(
                onPressed: _busy ? null : _scan,
                icon: const Icon(Icons.qr_code_scanner),
                label: const Text('扫描并批准网页登录'),
              ),
              if (_busy) ...[
                const SizedBox(height: 20),
                const LinearProgressIndicator(),
              ],
              const SizedBox(height: 22),
              const Text(
                '网页端不接收用户名或密码。它只显示一次性二维码，手机批准后，Identity 再给网页签发属于网页自己的授权码和 Token。',
                textAlign: TextAlign.center,
              ),
            ],
          ),
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
    appBar: AppBar(
      title: const Text('扫描登录二维码'),
      actions: [
        IconButton(
          onPressed: _controller.toggleTorch,
          icon: const Icon(Icons.flashlight_on),
        ),
      ],
    ),
    body: Stack(
      fit: StackFit.expand,
      children: [
        MobileScanner(controller: _controller, onDetect: _detected),
        Center(
          child: Container(
            width: 260,
            height: 260,
            decoration: BoxDecoration(
              border: Border.all(color: Colors.white, width: 3),
              borderRadius: BorderRadius.circular(20),
            ),
          ),
        ),
        const Positioned(
          left: 24,
          right: 24,
          bottom: 44,
          child: Text(
            '只接受当前 PassingTrace Identity 生成的两分钟一次性二维码',
            textAlign: TextAlign.center,
            style: TextStyle(color: Colors.white, fontSize: 16),
          ),
        ),
      ],
    ),
  );
}
