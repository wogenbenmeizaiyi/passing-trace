import 'package:flutter/material.dart';

import '../user_facing_error.dart';
import 'social_api.dart';
import 'social_widgets.dart';

/// Adding a friend is a separate task, not a form inside the contact list.
class AddFriendView extends StatefulWidget {
  const AddFriendView({super.key, required this.api, this.initialCode = ''});

  final SocialApi api;
  final String initialCode;

  @override
  State<AddFriendView> createState() => _AddFriendViewState();
}

class _AddFriendViewState extends State<AddFriendView> {
  final _form = GlobalKey<FormState>();
  final _code = TextEditingController();
  final _codeFocus = FocusNode();
  bool _sending = false;
  String? _sentCode;
  String? _error;

  bool get _sent => _sentCode == _code.text.trim();

  @override
  void initState() {
    super.initState();
    _code.text = widget.initialCode;
  }

  @override
  void dispose() {
    _code.dispose();
    _codeFocus.dispose();
    super.dispose();
  }

  void _changed(String value) {
    setState(() => _error = null);
  }

  Future<void> _send() async {
    if (_sending || _sent || !_form.currentState!.validate()) return;
    final code = _code.text.trim();
    _codeFocus.unfocus();
    setState(() {
      _sending = true;
      _error = null;
    });
    try {
      await widget.api.request(
        'POST',
        '/api/v1/friend-requests',
        body: {'code': code},
      );
      if (mounted) setState(() => _sentCode = code);
    } catch (error) {
      if (mounted) {
        setState(() {
          _error = userFacingErrorMessage(
            error,
            fallback: '好友申请暂时没有发送成功，请稍后重试。',
          );
        });
      }
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  Future<void> _scan() async {
    _codeFocus.unfocus();
    final code = await Navigator.push<String>(
      context,
      MaterialPageRoute(builder: (_) => const FriendScannerView()),
    );
    if (!mounted || code == null) return;
    _form.currentState!.reset();
    _code.value = TextEditingValue(
      text: code,
      selection: TextSelection.collapsed(offset: code.length),
    );
    _changed(code);
    // Scanning fills the form; sending still requires an explicit confirmation.
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    return Scaffold(
      appBar: AppBar(title: const Text('添加好友')),
      body: SafeArea(
        top: false,
        child: SingleChildScrollView(
          keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
          padding: const EdgeInsets.fromLTRB(20, 24, 20, 24),
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 480),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text('把朋友加进生活里', style: theme.textTheme.titleLarge),
                  const SizedBox(height: 8),
                  Text(
                    '一起记录经历，分享日常。',
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: colors.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 24),
                  Card(
                    child: Padding(
                      padding: const EdgeInsets.all(20),
                      child: Form(
                        key: _form,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            Text('通过好友码添加', style: theme.textTheme.titleSmall),
                            const SizedBox(height: 12),
                            TextFormField(
                              key: const ValueKey('friend-code-input'),
                              controller: _code,
                              focusNode: _codeFocus,
                              readOnly: _sending,
                              maxLength: 100,
                              autocorrect: false,
                              enableSuggestions: false,
                              textInputAction: TextInputAction.done,
                              onFieldSubmitted: (_) => _send(),
                              onChanged: _changed,
                              validator: (value) =>
                                  value == null || value.trim().isEmpty
                                  ? '请先输入好友码。'
                                  : null,
                              decoration: InputDecoration(
                                labelText: '好友码',
                                hintText: '输入或粘贴朋友的好友码',
                                floatingLabelBehavior:
                                    FloatingLabelBehavior.never,
                                counterText: '',
                                errorText: _error,
                                errorMaxLines: 4,
                                suffixIcon: _code.text.isEmpty
                                    ? null
                                    : IconButton(
                                        tooltip: '清空好友码',
                                        onPressed: _sending
                                            ? null
                                            : () {
                                                _form.currentState!.reset();
                                                _code.clear();
                                                _changed('');
                                                _codeFocus.requestFocus();
                                              },
                                        icon: const Icon(Icons.close, size: 20),
                                      ),
                              ),
                            ),
                            const SizedBox(height: 16),
                            FilledButton(
                              onPressed:
                                  _sending || _sent || _code.text.trim().isEmpty
                                  ? null
                                  : _send,
                              style: FilledButton.styleFrom(
                                minimumSize: const Size(48, 48),
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 16,
                                  vertical: 12,
                                ),
                              ),
                              child: Text(
                                _sending
                                    ? '正在发送…'
                                    : _sent
                                    ? '申请已发送'
                                    : '发送申请',
                              ),
                            ),
                            if (_sent) ...[
                              const SizedBox(height: 12),
                              Semantics(
                                liveRegion: true,
                                child: Text(
                                  '好友申请已发送，等待对方确认。',
                                  style: theme.textTheme.bodySmall?.copyWith(
                                    color: colors.onSurfaceVariant,
                                  ),
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 24),
                  Card(
                    clipBehavior: Clip.antiAlias,
                    child: Column(
                      children: [
                        _Entry(
                          icon: Icons.qr_code_scanner_rounded,
                          title: '扫码添加',
                          subtitle: '扫描朋友的二维码',
                          onTap: _sending ? null : _scan,
                        ),
                        const Divider(height: 1, indent: 68, endIndent: 20),
                        _Entry(
                          icon: Icons.qr_code_rounded,
                          title: '我的好友码',
                          subtitle: '出示二维码，让朋友添加我',
                          onTap: _sending
                              ? null
                              : () {
                                  _codeFocus.unfocus();
                                  Navigator.push<void>(
                                    context,
                                    MaterialPageRoute(
                                      builder: (_) =>
                                          FriendCodeView(api: widget.api),
                                    ),
                                  );
                                },
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 20),
                  Text(
                    '对方同意后，你们就可以聊天、分享记录了。',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: colors.onSurfaceVariant,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _Entry extends StatelessWidget {
  const _Entry({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return ListTile(
      enabled: onTap != null,
      contentPadding: const EdgeInsets.symmetric(horizontal: 20, vertical: 8),
      leading: Icon(icon, color: theme.colorScheme.primary),
      title: Text(title, style: theme.textTheme.titleSmall),
      subtitle: Text(
        subtitle,
        style: theme.textTheme.bodySmall?.copyWith(
          color: theme.colorScheme.onSurfaceVariant,
        ),
      ),
      trailing: const Icon(Icons.chevron_right, size: 20),
      onTap: onTap,
    );
  }
}
