import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';
import '../views/settings_view.dart';
import 'account_avatar.dart';
import 'avatar_crop_view.dart';
import 'profile_api.dart';
import 'profile_controller.dart';

class AccountCenterView extends StatefulWidget {
  const AccountCenterView({
    super.key,
    required this.controller,
    required this.onSignOut,
  });
  final ProfileController controller;
  final Future<void> Function() onSignOut;
  @override
  State<AccountCenterView> createState() => _AccountCenterViewState();
}

class _AccountCenterViewState extends State<AccountCenterView>
    with WidgetsBindingObserver {
  String? _error;
  bool _loading = true;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _load();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _load();
  }

  Future<void> _load() async {
    if (mounted) {
      setState(() {
        _loading = true;
        _error = null;
      });
    }
    try {
      await widget.controller.refresh();
    } catch (_) {
      if (mounted) setState(() => _error = '个人资料暂时无法加载，请重试。');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _edit() => Navigator.push<void>(
    context,
    MaterialPageRoute(
      builder: (_) => EditProfileView(controller: widget.controller),
    ),
  );
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: TraceAppBar(
      title: '用户中心',
      leading: TraceIconButton(
        glyph: TraceGlyph.chevronLeft,
        tooltip: '返回',
        onPressed: () => Navigator.pop(context),
      ),
    ),
    body: AnimatedBuilder(
      animation: widget.controller,
      builder: (context, _) {
        final account = widget.controller;
        final profile = account.profile;
        final colors = context.traceColors;
        return RefreshIndicator(
          onRefresh: _load,
          child: ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(24, 24, 24, 40),
            children: [
              if (_loading) const LinearProgressIndicator(minHeight: 2),
              if (_error != null) ...[
                Text(_error!, style: TextStyle(color: colors.danger)),
                TextButton(onPressed: _load, child: const Text('重新加载')),
              ],
              Row(
                children: [
                  Semantics(
                    button: true,
                    label: '编辑个人资料',
                    child: InkWell(
                      borderRadius: BorderRadius.circular(60),
                      onTap: profile == null ? null : _edit,
                      child: AccountAvatar(bytes: account.avatar, size: 96),
                    ),
                  ),
                  const SizedBox(width: 20),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          account.nickname,
                          style: Theme.of(context).textTheme.headlineSmall
                              ?.copyWith(fontWeight: FontWeight.w700),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          '仅自己可见的生活档案',
                          style: TextStyle(color: colors.inkSecondary),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 24),
              Text(
                profile?.bio.isNotEmpty == true
                    ? profile!.bio
                    : '在这里，慢慢收集属于你的生活。',
                style: TextStyle(color: colors.inkSecondary, height: 1.6),
              ),
              const SizedBox(height: 24),
              FilledButton.tonal(
                onPressed: profile == null ? null : _edit,
                child: const Text('编辑个人资料'),
              ),
              const SizedBox(height: 32),
              _AccountSection(
                children: [
                  if (profile != null) ...[
                    _InfoRow(label: '登录用户名', value: profile.username),
                    _InfoRow(label: '加入星期八', value: _date(profile.createdAt)),
                  ],
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    leading: const TraceIcon(TraceGlyph.settings),
                    title: const Text('设置'),
                    subtitle: const Text('主题与外观、退出此设备'),
                    trailing: const TraceIcon(TraceGlyph.chevronRight),
                    onTap: () => Navigator.push<void>(
                      context,
                      MaterialPageRoute(
                        builder: (_) =>
                            SettingsView(onSignOut: widget.onSignOut),
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 24),
              Text(
                '头像和简介不会作为记录保存，也不会自动加入 AI 记忆。',
                style: TextStyle(
                  color: colors.inkSecondary,
                  fontSize: 13,
                  height: 1.6,
                ),
              ),
            ],
          ),
        );
      },
    ),
  );
}

class EditProfileView extends StatefulWidget {
  const EditProfileView({super.key, required this.controller});
  final ProfileController controller;
  @override
  State<EditProfileView> createState() => _EditProfileViewState();
}

class _EditProfileViewState extends State<EditProfileView> {
  final _form = GlobalKey<FormState>();
  late final AccountProfile _initial;
  late final TextEditingController _nickname, _bio;
  Uint8List? _avatar;
  bool _remove = false, _saving = false, _allowPop = false, _conflict = false;
  String? _error;
  bool get _dirty =>
      _nickname.text != _initial.nickname ||
      _bio.text != _initial.bio ||
      _avatar != null ||
      _remove;
  @override
  void initState() {
    super.initState();
    _initial = widget.controller.profile!;
    _nickname = TextEditingController(text: _initial.nickname)
      ..addListener(_changed);
    _bio = TextEditingController(text: _initial.bio)..addListener(_changed);
  }

  void _changed() => setState(() {});
  @override
  void dispose() {
    _nickname.dispose();
    _bio.dispose();
    super.dispose();
  }

  Future<bool> _confirmDiscard(String title) async =>
      await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: Text(title),
          content: const Text('尚未保存的头像和资料修改将被放弃。'),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('继续编辑'),
            ),
            TextButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('放弃修改'),
            ),
          ],
        ),
      ) ??
      false;
  void _close() {
    setState(() => _allowPop = true);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) Navigator.pop(context);
    });
  }

  Future<void> _back() async {
    if (_saving) return;
    if (_dirty && !await _confirmDiscard('放弃修改？')) return;
    if (mounted) _close();
  }

  Future<void> _pick() async {
    try {
      final result = await FilePicker.pickFiles(
        type: FileType.custom,
        allowedExtensions: ['jpg', 'jpeg', 'png', 'webp'],
      );
      if (result.isEmpty || !mounted) return;
      final file = result.first;
      if (await file.length() > 5 * 1024 * 1024) {
        if (!mounted) return;
        setState(() => _error = '头像不能超过 5MB，请选择较小的图片。');
        return;
      }
      final bytes = await file.readAsBytes();
      if (!mounted) return;
      final cropped = await Navigator.push<Uint8List>(
        context,
        MaterialPageRoute(builder: (_) => AvatarCropView(bytes: bytes)),
      );
      if (!mounted || cropped == null) return;
      setState(() {
        _avatar = cropped;
        _remove = false;
        _error = null;
      });
    } catch (_) {
      if (mounted) setState(() => _error = '图片无法读取，请重新选择。');
    }
  }

  Future<void> _save() async {
    if (_saving || !_dirty || !_form.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await widget.controller.save(
        nickname: _nickname.text,
        bio: _bio.text,
        version: _initial.version,
        image: _avatar,
        removeAvatar: _remove,
      );
      if (!mounted) return;
      setState(() => _saving = false);
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('个人资料已保存')));
      _close();
    } catch (error) {
      if (mounted) {
        setState(() {
          _saving = false;
          _conflict = error is ProfileException && error.status == 409;
          _error = error is ProfileException
              ? error.message
              : '暂时无法保存，请稍后重试。已填写的内容会保留。';
        });
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(_error!)));
      }
    }
  }

  Future<void> _reload() async {
    if (!await _confirmDiscard('重新加载资料？') || !mounted) return;
    setState(() => _saving = true);
    try {
      await widget.controller.refresh();
      if (mounted) {
        setState(() => _saving = false);
        _close();
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _saving = false;
          _error = '重新加载失败，请稍后重试。';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: _allowPop || (!_dirty && !_saving),
    onPopInvokedWithResult: (didPop, _) {
      if (!didPop) _back();
    },
    child: Scaffold(
      appBar: TraceAppBar(
        title: '编辑个人资料',
        leading: TraceIconButton(
          glyph: TraceGlyph.chevronLeft,
          tooltip: '返回',
          onPressed: _back,
        ),
      ),
      body: SafeArea(
        child: Form(
          key: _form,
          child: ListView(
            padding: const EdgeInsets.fromLTRB(24, 20, 24, 32),
            children: [
              Center(
                child: AccountAvatar(
                  bytes: _remove ? null : _avatar ?? widget.controller.avatar,
                  size: 104,
                ),
              ),
              TextButton(
                onPressed: _saving ? null : _pick,
                child: const Text('更换头像'),
              ),
              if (_initial.hasAvatar || _avatar != null)
                TextButton(
                  onPressed: _saving || _remove
                      ? null
                      : () => setState(() {
                          _avatar = null;
                          _remove = true;
                        }),
                  child: const Text('恢复默认头像'),
                ),
              Text(
                'JPG、PNG、WebP · 不超过 5MB\n裁剪预览后，保存修改才会生效',
                textAlign: TextAlign.center,
                style: TextStyle(
                  color: context.traceColors.inkSecondary,
                  fontSize: 13,
                ),
              ),
              const SizedBox(height: 28),
              if (_error != null)
                Padding(
                  padding: const EdgeInsets.only(bottom: 20),
                  child: Semantics(
                    liveRegion: true,
                    child: Text(
                      _error!,
                      style: TextStyle(color: context.traceColors.danger),
                    ),
                  ),
                ),
              if (_conflict)
                TextButton(
                  onPressed: _saving ? null : _reload,
                  child: const Text('重新加载资料'),
                ),
              TextFormField(
                controller: _nickname,
                enabled: !_saving,
                decoration: const InputDecoration(
                  labelText: '昵称',
                  hintText: '填写你喜欢的称呼',
                  helperText: '1～24 个字，不影响登录用户名',
                  border: OutlineInputBorder(),
                ),
                autovalidateMode: AutovalidateMode.onUserInteraction,
                validator: (value) =>
                    value == null ||
                        value.trim().characters.isEmpty ||
                        value.trim().characters.length > 24 ||
                        RegExp(r'[\x00-\x1f\x7f]').hasMatch(value)
                    ? '昵称请填写 1～24 个字，不含换行'
                    : null,
              ),
              const SizedBox(height: 24),
              TextFormField(
                controller: _bio,
                enabled: !_saving,
                minLines: 3,
                maxLines: 6,
                decoration: InputDecoration(
                  labelText: '个人简介（选填）',
                  hintText: '简单介绍一下自己',
                  counterText: '${_bio.text.characters.length} / 100',
                  border: const OutlineInputBorder(),
                ),
                autovalidateMode: AutovalidateMode.onUserInteraction,
                validator: (value) =>
                    (value?.trim().characters.length ?? 0) > 100
                    ? '个人简介最多 100 个字'
                    : null,
              ),
              const SizedBox(height: 24),
              _AccountSection(
                children: [
                  _InfoRow(label: '登录用户名（不可修改）', value: _initial.username),
                  _InfoRow(label: '加入时间', value: _date(_initial.createdAt)),
                ],
              ),
              const SizedBox(height: 28),
              FilledButton(
                onPressed: _saving || !_dirty || _conflict ? null : _save,
                child: Text(_saving ? '正在保存…' : '保存修改'),
              ),
              TextButton(
                onPressed: _saving ? null : _back,
                child: const Text('取消'),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

String _date(DateTime value) {
  final local = value.toLocal();
  return '${local.year}年${local.month}月${local.day}日';
}

class _AccountSection extends StatelessWidget {
  const _AccountSection({required this.children});
  final List<Widget> children;
  @override
  Widget build(BuildContext context) => Material(
    color: context.traceColors.surface,
    shape: RoundedRectangleBorder(
      side: BorderSide(color: context.traceColors.line),
      borderRadius: BorderRadius.circular(20),
    ),
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: children,
      ),
    ),
  );
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.label, required this.value});
  final String label, value;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 12),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: TextStyle(
            color: context.traceColors.inkSecondary,
            fontSize: 13,
          ),
        ),
        const SizedBox(height: 6),
        SelectableText(value),
      ],
    ),
  );
}
