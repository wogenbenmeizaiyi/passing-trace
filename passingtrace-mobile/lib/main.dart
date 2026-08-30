import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import 'auth_service.dart';
import 'views/events_list_view.dart';
import 'views/assistant_view.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const PassingTraceApp());
}

class PassingTraceApp extends StatelessWidget {
  const PassingTraceApp({super.key});

  static const paper = Color(0xfff4f0e5);
  static const ink = Color(0xff24231f);
  static const coral = Color(0xffd64b3c);
  static const sage = Color(0xff60715a);

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'PassingTrace',
    debugShowCheckedModeBanner: false,
    theme: ThemeData(
      colorScheme: ColorScheme.fromSeed(
        seedColor: coral,
        primary: coral,
        secondary: sage,
        surface: paper,
      ),
      scaffoldBackgroundColor: paper,
      useMaterial3: true,
      appBarTheme: const AppBarTheme(
        backgroundColor: paper,
        foregroundColor: ink,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
      ),
      drawerTheme: const DrawerThemeData(
        backgroundColor: paper,
        surfaceTintColor: Colors.transparent,
      ),
      cardTheme: const CardThemeData(
        color: Colors.white54,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
      ),
      dividerColor: ink.withValues(alpha: 0.14),
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
  final _username = TextEditingController();
  final _password = TextEditingController();
  final _confirmPassword = TextEditingController();
  bool _busy = false;
  bool _obscure = true;
  bool _creating = false;

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    _confirmPassword.dispose();
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
        bootstrapCode: 'passingtrace-local-setup',
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
  Widget build(BuildContext context) => Scaffold(
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
                  const Align(
                    alignment: Alignment.centerLeft,
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        _BrandMark(size: 36),
                        SizedBox(width: 12),
                        Text(
                          'PassingTrace',
                          style: TextStyle(
                            color: PassingTraceApp.ink,
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
                  const Text(
                    'YOUR LIFE, IN CONTEXT',
                    style: TextStyle(
                      color: PassingTraceApp.coral,
                      fontSize: 11,
                      fontWeight: FontWeight.w800,
                      letterSpacing: 2.1,
                    ),
                  ),
                  const SizedBox(height: 18),
                  Text(
                    _creating ? '开始记录你的时间。' : '欢迎回到你的时间线。',
                    style: const TextStyle(
                      color: PassingTraceApp.ink,
                      fontFamily: 'serif',
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
                    style: TextStyle(
                      color: PassingTraceApp.ink.withValues(alpha: 0.58),
                      height: 1.6,
                    ),
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
                      onFieldSubmitted: (_) => _submit(),
                      decoration: _fieldDecoration(
                        label: '确认密码',
                        icon: Icons.lock_reset_outlined,
                      ),
                      validator: (value) =>
                          value != _password.text ? '两次密码不一致' : null,
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
                        ? const SizedBox.square(
                            dimension: 20,
                            child: CircularProgressIndicator(
                              color: Colors.white,
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
                    style: TextStyle(
                      color: PassingTraceApp.ink.withValues(alpha: 0.46),
                      fontSize: 12,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );

  InputDecoration _fieldDecoration({
    required String label,
    required IconData icon,
    Widget? suffix,
  }) => InputDecoration(
    labelText: label,
    prefixIcon: Icon(icon),
    suffixIcon: suffix,
    filled: true,
    fillColor: Colors.white.withValues(alpha: 0.6),
    enabledBorder: OutlineInputBorder(
      borderSide: BorderSide(
        color: PassingTraceApp.ink.withValues(alpha: 0.18),
      ),
    ),
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

  Future<void> _scanFromDrawer() async {
    Navigator.of(context).pop();
    await Future<void>.delayed(const Duration(milliseconds: 180));
    if (mounted) await _scan();
  }

  Future<void> _openTimeline() async {
    final navigator = Navigator.of(context);
    if (navigator.canPop()) {
      navigator.pop();
    }
    await Future<void>.delayed(const Duration(milliseconds: 180));
    if (!mounted) return;
    final sessionExpired = await navigator.push<bool>(
      MaterialPageRoute(
        builder: (_) =>
            EventsListView(auth: widget.auth, session: widget.session),
      ),
    );
    await _restoreAfterSessionExpiry(sessionExpired);
  }

  Future<void> _openAssistant() async {
    final navigator = Navigator.of(context);
    if (navigator.canPop()) navigator.pop();
    await Future<void>.delayed(const Duration(milliseconds: 180));
    if (!mounted) return;
    final sessionExpired = await navigator.push<bool>(
      MaterialPageRoute(
        builder: (_) =>
            AssistantView(auth: widget.auth, session: widget.session),
      ),
    );
    await _restoreAfterSessionExpiry(sessionExpired);
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
  Widget build(BuildContext context) => Scaffold(
    drawer: _buildDrawer(),
    appBar: AppBar(
      titleSpacing: 0,
      title: const Row(
        children: [
          _BrandMark(size: 30),
          SizedBox(width: 10),
          Text(
            'PassingTrace',
            style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
          ),
        ],
      ),
      actions: [
        IconButton(
          tooltip: '时间线',
          onPressed: _busy ? null : _openTimeline,
          icon: const Icon(Icons.timeline_outlined),
        ),
        const SizedBox(width: 4),
      ],
      bottom: const PreferredSize(
        preferredSize: Size.fromHeight(1),
        child: Divider(height: 1),
      ),
    ),
    body: Stack(
      children: [
        const _PassingTraceHome(),
        if (_busy)
          const Positioned(
            left: 0,
            right: 0,
            top: 0,
            child: LinearProgressIndicator(minHeight: 3),
          ),
      ],
    ),
  );

  Widget _buildDrawer() => Drawer(
    width: 304,
    child: SafeArea(
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(24, 22, 18, 18),
            child: Row(
              children: [
                const _BrandMark(size: 38),
                const SizedBox(width: 12),
                const Expanded(
                  child: Text(
                    'PassingTrace',
                    style: TextStyle(fontSize: 20, fontWeight: FontWeight.w700),
                  ),
                ),
                IconButton(
                  tooltip: '关闭菜单',
                  onPressed: () => Navigator.of(context).pop(),
                  icon: const Icon(Icons.close),
                ),
              ],
            ),
          ),
          const Divider(height: 1),
          const SizedBox(height: 10),
          const _DrawerDestination(
            icon: Icons.home_outlined,
            selectedIcon: Icons.home,
            label: '首页',
            selected: true,
          ),
          _DrawerDestination(
            icon: Icons.timeline_outlined,
            selectedIcon: Icons.timeline,
            label: '时间线',
            onTap: _busy ? null : _openTimeline,
          ),
          _DrawerDestination(
            icon: Icons.auto_graph_outlined,
            selectedIcon: Icons.auto_graph,
            label: '生活洞察',
            onTap: _busy ? null : _openAssistant,
          ),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 10),
            child: Material(
              color: PassingTraceApp.coral.withValues(alpha: 0.1),
              borderRadius: BorderRadius.circular(16),
              child: InkWell(
                onTap: _busy ? null : _scanFromDrawer,
                borderRadius: BorderRadius.circular(16),
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Row(
                    children: [
                      Container(
                        width: 48,
                        height: 48,
                        decoration: BoxDecoration(
                          color: PassingTraceApp.coral,
                          borderRadius: BorderRadius.circular(14),
                        ),
                        child: const Icon(
                          Icons.qr_code_scanner,
                          color: Colors.white,
                          size: 27,
                        ),
                      ),
                      const SizedBox(width: 14),
                      const Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              '扫一扫',
                              style: TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                            SizedBox(height: 3),
                            Text('扫描电脑端登录二维码', style: TextStyle(fontSize: 12)),
                          ],
                        ),
                      ),
                      const Icon(Icons.chevron_right),
                    ],
                  ),
                ),
              ),
            ),
          ),
          const Spacer(),
          const Divider(height: 1),
          Padding(
            padding: const EdgeInsets.fromLTRB(22, 16, 22, 10),
            child: Row(
              children: [
                Icon(
                  widget.session.hasToken
                      ? Icons.verified_user_outlined
                      : Icons.phonelink_lock_outlined,
                  color: PassingTraceApp.sage,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        widget.session.hasToken ? '手机账号已登录' : '设备凭据已建立',
                        style: const TextStyle(fontWeight: FontWeight.w700),
                      ),
                      Text(
                        '设备 ${widget.session.deviceId}',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          fontSize: 12,
                          color: PassingTraceApp.ink.withValues(alpha: 0.58),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          if (!widget.session.hasToken)
            ListTile(
              leading: const Icon(Icons.login),
              title: const Text('登录账号'),
              onTap: _busy
                  ? null
                  : () {
                      Navigator.of(context).pop();
                      _login();
                    },
            ),
          ListTile(
            leading: const Icon(Icons.logout),
            title: const Text('退出此设备'),
            onTap: _busy
                ? null
                : () {
                    Navigator.of(context).pop();
                    _clear();
                  },
          ),
          const SizedBox(height: 12),
        ],
      ),
    ),
  );
}

class _BrandMark extends StatelessWidget {
  const _BrandMark({required this.size});

  final double size;

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    alignment: Alignment.center,
    decoration: const BoxDecoration(
      color: PassingTraceApp.coral,
      shape: BoxShape.circle,
    ),
    child: Text(
      'P',
      style: TextStyle(
        color: Colors.white,
        fontFamily: 'serif',
        fontSize: size * 0.55,
        fontStyle: FontStyle.italic,
        fontWeight: FontWeight.w700,
      ),
    ),
  );
}

class _DrawerDestination extends StatelessWidget {
  const _DrawerDestination({
    required this.icon,
    required this.selectedIcon,
    required this.label,
    this.selected = false,
    this.onTap,
  });

  final IconData icon;
  final IconData selectedIcon;
  final String label;
  final bool selected;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final handler = onTap;
    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 24),
      leading: Icon(selected ? selectedIcon : icon),
      title: Text(label),
      selected: selected,
      selectedColor: PassingTraceApp.coral,
      onTap: handler ?? () => Navigator.of(context).pop(),
    );
  }
}

class _PassingTraceHome extends StatelessWidget {
  const _PassingTraceHome();

  @override
  Widget build(BuildContext context) {
    void openTimeline() {
      final home = context.findAncestorStateOfType<_AccountHomeState>();
      home?._openTimeline();
    }

    return SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(24, 38, 24, 48),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'YOUR LIFE, IN CONTEXT',
            style: TextStyle(
              color: PassingTraceApp.coral,
              fontSize: 12,
              fontWeight: FontWeight.w800,
              letterSpacing: 2.3,
            ),
          ),
          const SizedBox(height: 22),
          const Text(
            '把生活留下来，',
            style: TextStyle(
              color: PassingTraceApp.ink,
              fontFamily: 'serif',
              fontSize: 38,
              height: 1.15,
              fontWeight: FontWeight.w500,
            ),
          ),
          const Text(
            '看见时间的形状。',
            style: TextStyle(
              color: PassingTraceApp.coral,
              fontFamily: 'serif',
              fontSize: 38,
              height: 1.15,
              fontWeight: FontWeight.w500,
            ),
          ),
          const SizedBox(height: 20),
          Text(
            '记录经历，也写下计划。PassingTrace 会将零散的文字整理成只属于你的时间线与生活洞察。',
            style: TextStyle(
              color: PassingTraceApp.ink.withValues(alpha: 0.62),
              height: 1.8,
              fontSize: 15,
            ),
          ),
          const SizedBox(height: 34),
          const _PrivateSpaceCard(),
          const SizedBox(height: 36),
          Material(
            color: Colors.transparent,
            child: InkWell(
              onTap: openTimeline,
              borderRadius: BorderRadius.circular(20),
              child: Container(
                padding: const EdgeInsets.all(22),
                decoration: BoxDecoration(
                  color: Colors.white.withValues(alpha: 0.55),
                  borderRadius: BorderRadius.circular(20),
                  border: Border.all(
                    color: PassingTraceApp.ink.withValues(alpha: 0.12),
                  ),
                ),
                child: Row(
                  children: [
                    Container(
                      width: 48,
                      height: 48,
                      alignment: Alignment.center,
                      decoration: const BoxDecoration(
                        color: PassingTraceApp.coral,
                        shape: BoxShape.circle,
                      ),
                      child: const Icon(
                        Icons.timeline,
                        color: Colors.white,
                        size: 24,
                      ),
                    ),
                    const SizedBox(width: 16),
                    const Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '我的时间线',
                            style: TextStyle(
                              fontFamily: 'serif',
                              fontSize: 20,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          SizedBox(height: 4),
                          Text(
                            '查看、记录、编辑你留下的痕迹和计划',
                            style: TextStyle(
                              fontSize: 12,
                              color: Colors.black54,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const Icon(Icons.arrow_forward, size: 18),
                  ],
                ),
              ),
            ),
          ),
          const SizedBox(height: 28),
          Container(
            padding: const EdgeInsets.all(24),
            color: PassingTraceApp.ink,
            child: const Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.auto_awesome, color: PassingTraceApp.coral),
                SizedBox(height: 16),
                Text(
                  '“你在这个月去过 3 个新地点，步行记录比上月同期多了 28%。”',
                  style: TextStyle(
                    color: Colors.white,
                    fontFamily: 'serif',
                    fontSize: 21,
                    height: 1.6,
                  ),
                ),
                SizedBox(height: 14),
                Text(
                  '基于你的私人数据生成',
                  style: TextStyle(color: Colors.white54, fontSize: 12),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _PrivateSpaceCard extends StatelessWidget {
  const _PrivateSpaceCard();

  @override
  Widget build(BuildContext context) => Container(
    width: double.infinity,
    padding: const EdgeInsets.all(26),
    color: PassingTraceApp.sage,
    child: const Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('私人空间', style: TextStyle(color: Colors.white70, fontSize: 12)),
        SizedBox(height: 34),
        Text(
          '01',
          style: TextStyle(
            color: Colors.white54,
            fontFamily: 'serif',
            fontSize: 28,
            fontStyle: FontStyle.italic,
          ),
        ),
        SizedBox(height: 18),
        Text(
          '一个账号，连接所有\nPassingTrace 客户端。',
          style: TextStyle(
            color: Colors.white,
            fontFamily: 'serif',
            fontSize: 24,
            height: 1.45,
          ),
        ),
        SizedBox(height: 14),
        Text(
          '通过左上角菜单使用“扫一扫”，即可批准网页和桌面端登录。',
          style: TextStyle(color: Colors.white70, height: 1.6),
        ),
      ],
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
