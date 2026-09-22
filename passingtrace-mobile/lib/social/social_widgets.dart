import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../user_facing_error.dart';
import 'social_api.dart';

class FriendCodeView extends StatefulWidget {
  const FriendCodeView({super.key, required this.api});
  final SocialApi api;
  @override
  State<FriendCodeView> createState() => _FriendCodeViewState();
}

class _FriendCodeViewState extends State<FriendCodeView> {
  SocialRow? _me;
  String? _error;
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final data = await widget.api.request(
        'GET',
        '/api/v1/people/me',
        identity: true,
      );
      if (mounted) setState(() => _me = Map<String, dynamic>.from(data as Map));
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('我的好友码')),
    body: SingleChildScrollView(
      padding: const EdgeInsets.all(24),
      child: Column(
        children: [
          if (_error != null) Text(_error!),
          if (_me == null && _error == null) const CircularProgressIndicator(),
          if (_me != null) ...[
            Text(
              _me!['profile']['nickname'] as String,
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            const SizedBox(height: 24),
            Image.memory(
              base64Decode((_me!['qrDataUrl'] as String).split(',').last),
              width: 240,
              height: 240,
              semanticLabel: '用于添加好友的二维码',
            ),
            const SizedBox(height: 16),
            SelectableText(_me!['profile']['friendCode'] as String),
            TextButton(
              onPressed: () => Clipboard.setData(
                ClipboardData(text: _me!['profile']['friendCode'] as String),
              ),
              child: const Text('复制好友码'),
            ),
            const Text('让朋友在“消息 · 好友”中扫码或输入好友码。'),
          ],
        ],
      ),
    ),
  );
}

class SocialAvatar extends StatefulWidget {
  const SocialAvatar({super.key, required this.api, required this.person});
  final SocialApi api;
  final SocialRow person;
  @override
  State<SocialAvatar> createState() => _SocialAvatarState();
}

class _SocialAvatarState extends State<SocialAvatar> {
  Uint8List? _image;
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (widget.person['hasAvatar'] != true) return;
    try {
      final image = await widget.api.bytes(
        '/api/v1/people/${widget.person['id']}/avatar',
        identity: true,
      );
      if (mounted) setState(() => _image = image);
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) => CircleAvatar(
    backgroundImage: _image == null ? null : MemoryImage(_image!),
    child: _image == null ? const Icon(Icons.person_outline) : null,
  );
}

Future<List<String>?> pickParticipants(
  BuildContext context,
  SocialApi api,
  List<String> selected,
) => showModalBottomSheet<List<String>>(
  context: context,
  isScrollControlled: true,
  showDragHandle: true,
  builder: (_) => _ParticipantsSheet(api: api, selected: selected),
);

class _ParticipantsSheet extends StatefulWidget {
  const _ParticipantsSheet({required this.api, required this.selected});
  final SocialApi api;
  final List<String> selected;
  @override
  State<_ParticipantsSheet> createState() => _ParticipantsSheetState();
}

class _ParticipantsSheetState extends State<_ParticipantsSheet> {
  List<SocialRow> _friends = [];
  late final Set<String> _selected = widget.selected.toSet();
  String _query = '';
  String? _error;
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final rows = socialRows(
        await widget.api.request('GET', '/api/v1/friends'),
      );
      if (mounted) setState(() => _friends = rows);
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    }
  }

  @override
  Widget build(BuildContext context) => SafeArea(
    child: SizedBox(
      height: MediaQuery.sizeOf(context).height * .72,
      child: Column(
        children: [
          ListTile(
            title: const Text('一起的人'),
            subtitle: const Text('好友收到提醒，并可查看内容与后续更新。'),
            trailing: TextButton(
              onPressed: () => Navigator.pop(context, _selected.toList()),
              child: const Text('完成'),
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(16),
            child: TextField(
              decoration: const InputDecoration(labelText: '搜索昵称或备注'),
              onChanged: (v) => setState(() => _query = v),
            ),
          ),
          if (_error != null) Text(_error!),
          Expanded(
            child: ListView(
              children: [
                for (final f in _friends.where(
                  (f) =>
                      socialName(f).contains(_query) ||
                      (f['person']['nickname'] as String).contains(_query),
                ))
                  CheckboxListTile(
                    value: _selected.contains(f['person']['id']),
                    title: Text(socialName(f)),
                    onChanged: (value) => setState(() {
                      if (value == true) {
                        _selected.add(f['person']['id'] as String);
                      } else {
                        _selected.remove(f['person']['id']);
                      }
                    }),
                  ),
                if (_friends.isEmpty && _error == null)
                  const ListTile(title: Text('还没有好友，可先在消息页添加。')),
              ],
            ),
          ),
        ],
      ),
    ),
  );
}

class FriendScannerView extends StatefulWidget {
  const FriendScannerView({super.key});
  @override
  State<FriendScannerView> createState() => _FriendScannerViewState();
}

class _FriendScannerViewState extends State<FriendScannerView> {
  bool _done = false;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('扫描好友码')),
    body: MobileScanner(
      onDetect: (capture) {
        if (_done) return;
        for (final b in capture.barcodes) {
          final value = b.rawValue;
          if (value != null &&
              RegExp(r'^passingtrace-friend:[0-9a-fA-F]{16}$')
                  .hasMatch(value)) {
            _done = true;
            Navigator.pop(context, value);
            break;
          }
        }
      },
    ),
  );
}

Future<bool> confirmSocial(BuildContext context, String text) async =>
    await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const Text('请确认'),
        content: Text(text),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const Text('确认'),
          ),
        ],
      ),
    ) ??
    false;
