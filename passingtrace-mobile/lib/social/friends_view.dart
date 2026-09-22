import 'package:flutter/material.dart';

import '../user_facing_error.dart';
import 'social_api.dart';
import 'social_widgets.dart';

class FriendsView extends StatefulWidget {
  const FriendsView({
    super.key,
    required this.api,
    required this.onChat,
    this.selectedId,
  });
  final SocialApi api;
  final ValueChanged<String> onChat;
  final String? selectedId;
  @override
  State<FriendsView> createState() => _FriendsViewState();
}

class _FriendsViewState extends State<FriendsView> {
  final _query = TextEditingController(), _code = TextEditingController();
  List<SocialRow> _friends = [], _requests = [];
  List<String> _blocks = [];
  Map<String, String> _blockedNames = {};
  String? _error;
  bool _busy = false;
  @override
  void initState() {
    super.initState();
    _load().then((_) {
      final matches = _friends.where((x) => x['id'] == widget.selectedId);
      if (mounted && matches.isNotEmpty) _details(matches.first);
    });
  }

  @override
  void dispose() {
    _query.dispose();
    _code.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final results = await Future.wait([
        widget.api.request('GET', '/api/v1/friends'),
        widget.api.request('GET', '/api/v1/friend-requests'),
        widget.api.request('GET', '/api/v1/friends/blocks'),
      ]);
      final blocked = (results[2] as List).cast<String>();
      final names = <String, String>{};
      for (var at = 0; at < blocked.length; at += 100) {
        final people = socialRows(
          await widget.api.request(
            'POST',
            '/api/v1/people/profiles',
            identity: true,
            body: blocked.sublist(at, (at + 100).clamp(0, blocked.length)),
          ),
        );
        for (final person in people) {
          names[person['id'] as String] = person['nickname'] as String;
        }
      }
      if (mounted) {
        setState(() {
          _friends = socialRows(results[0]);
          _requests = socialRows(results[1]);
          _blocks = blocked;
          _blockedNames = names;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    }
  }

  Future<void> _act(Future<dynamic> Function() action) async {
    if (_busy) return;
    setState(() => _busy = true);
    try {
      await action();
      await _load();
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _details(SocialRow initial) async {
    final remark = TextEditingController(
          text: initial['remark'] as String? ?? '',
        ),
        label = TextEditingController(
          text: initial['label'] as String? ?? '朋友',
        );
    final me = await widget.api.request(
      'GET',
      '/api/v1/people/me',
      identity: true,
    );
    if (!mounted) {
      remark.dispose();
      label.dispose();
      return;
    }
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (sheet) => SafeArea(
        child: SingleChildScrollView(
          padding: EdgeInsets.fromLTRB(
            24,
            8,
            24,
            24 + MediaQuery.viewInsetsOf(sheet).bottom,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                initial['person']['nickname'] as String,
                style: Theme.of(sheet).textTheme.headlineSmall,
              ),
              Text(initial['person']['bio'] as String? ?? ''),
              const SizedBox(height: 16),
              TextField(
                controller: remark,
                maxLength: 80,
                decoration: const InputDecoration(
                  labelText: '私人备注',
                  helperText: '仅你和你的 AI 可见',
                ),
              ),
              TextField(
                controller: label,
                maxLength: 30,
                decoration: const InputDecoration(labelText: '私人关系标签'),
              ),
              Wrap(
                spacing: 8,
                children: [
                  for (final tag in ['朋友', '亲密朋友', '恋人', '家人', '同事', '同学'])
                    ActionChip(
                      label: Text(tag),
                      onPressed: () => label.text = tag,
                    ),
                ],
              ),
              FilledButton(
                onPressed: () {
                  Navigator.pop(sheet);
                  _act(
                    () => widget.api.request(
                      'PUT',
                      '/api/v1/friends/${initial['id']}/preference',
                      body: {'remark': remark.text, 'label': label.text},
                    ),
                  );
                },
                child: const Text('保存私人设置'),
              ),
              const Divider(),
              Text('双方确认的关系：${initial['relationship'] ?? '未设置'}'),
              const Text('只有你们两人可见，设置需要对方同意。'),
              if (initial['proposedRelationship'] != null) ...[
                Text('待确认：${initial['proposedRelationship']}'),
                if (initial['relationshipRequestedBy'] != me['profile']['id'])
                  Row(
                    children: [
                      for (final decision in ['accept', 'reject'])
                        TextButton(
                          onPressed: () {
                            Navigator.pop(sheet);
                            _act(
                              () => widget.api.request(
                                'POST',
                                '/api/v1/friends/${initial['id']}/relationship/decision',
                                body: {
                                  'decision': decision,
                                  'version': initial['version'],
                                },
                              ),
                            );
                          },
                          child: Text(decision == 'accept' ? '同意关系' : '拒绝'),
                        ),
                    ],
                  ),
              ],
              Wrap(
                spacing: 8,
                children: [
                  for (final kind in ['亲密朋友', '恋人', '家人'])
                    OutlinedButton(
                      onPressed: () {
                        Navigator.pop(sheet);
                        _act(
                          () => widget.api.request(
                            'POST',
                            '/api/v1/friends/${initial['id']}/relationship',
                            body: {'kind': kind, 'version': initial['version']},
                          ),
                        );
                      },
                      child: Text('申请$kind'),
                    ),
                ],
              ),
              TextButton(
                onPressed: () {
                  Navigator.pop(sheet);
                  _act(
                    () => widget.api.request(
                      'POST',
                      '/api/v1/friends/${initial['id']}/relationship/decision',
                      body: {
                        'decision': 'clear',
                        'version': initial['version'],
                      },
                    ),
                  );
                },
                child: const Text('解除 / 撤回双方关系'),
              ),
              const Divider(),
              FilledButton(
                onPressed: () {
                  Navigator.pop(sheet);
                  widget.onChat(initial['id'] as String);
                },
                child: const Text('发消息'),
              ),
              for (final block in [false, true])
                TextButton(
                  onPressed: () async {
                    if (!await confirmSocial(
                      sheet,
                      '${block ? '拉黑' : '删除'}好友将停止新消息、共同记录关联及双方分享。重新添加不会恢复旧授权。',
                    )) {
                      return;
                    }
                    if (sheet.mounted) Navigator.pop(sheet);
                    await _act(
                      () => widget.api.request(
                        'DELETE',
                        '/api/v1/friends/${initial['id']}?block=$block',
                      ),
                    );
                  },
                  child: Text(block ? '拉黑好友' : '删除好友'),
                ),
            ],
          ),
        ),
      ),
    );
    remark.dispose();
    label.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('好友'),
      actions: [
        IconButton(
          tooltip: '我的好友码',
          icon: const Icon(Icons.qr_code),
          onPressed: () => Navigator.push(
            context,
            MaterialPageRoute<void>(
              builder: (_) => FriendCodeView(api: widget.api),
            ),
          ),
        ),
        IconButton(
          tooltip: '刷新',
          icon: const Icon(Icons.refresh),
          onPressed: _load,
        ),
      ],
    ),
    body: RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          if (_error != null)
            Text(
              _error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          TextField(
            controller: _code,
            decoration: const InputDecoration(labelText: '好友码'),
          ),
          Wrap(
            spacing: 8,
            children: [
              FilledButton(
                onPressed: _busy
                    ? null
                    : () => _act(
                        () => widget.api.request(
                          'POST',
                          '/api/v1/friend-requests',
                          body: {'code': _code.text.trim()},
                        ),
                      ),
                child: const Text('申请添加'),
              ),
              OutlinedButton.icon(
                onPressed: () async {
                  final code = await Navigator.push<String>(
                    context,
                    MaterialPageRoute(
                      builder: (_) => const FriendScannerView(),
                    ),
                  );
                  if (code != null && mounted) {
                    setState(() => _code.text = code);
                  }
                },
                icon: const Icon(Icons.qr_code_scanner),
                label: const Text('扫码添加'),
              ),
            ],
          ),
          const SizedBox(height: 16),
          if (_requests.isNotEmpty) const Text('好友申请'),
          for (final r in _requests)
            Card(
              child: ListTile(
                leading: SocialAvatar(
                  api: widget.api,
                  person: Map<String, dynamic>.from(r['person'] as Map),
                ),
                title: Text(r['person']['nickname'] as String),
                subtitle: Wrap(
                  children: [
                    if (r['direction'] == 'received') ...[
                      for (final d in ['accept', 'reject'])
                        TextButton(
                          onPressed: _busy
                              ? null
                              : () => _act(
                                  () => widget.api.request(
                                    'POST',
                                    '/api/v1/friend-requests/${r['id']}/decision',
                                    body: {'decision': d},
                                  ),
                                ),
                          child: Text(d == 'accept' ? '接受' : '拒绝'),
                        ),
                    ] else
                      TextButton(
                        onPressed: _busy
                            ? null
                            : () => _act(
                                () => widget.api.request(
                                  'POST',
                                  '/api/v1/friend-requests/${r['id']}/decision',
                                  body: {'decision': 'withdraw'},
                                ),
                              ),
                        child: const Text('撤回申请'),
                      ),
                  ],
                ),
              ),
            ),
          TextField(
            controller: _query,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(labelText: '搜索好友昵称、备注或关系'),
          ),
          const SizedBox(height: 8),
          for (final f in _friends.where(
            (f) =>
                socialName(f).contains(_query.text) ||
                (f['person']['nickname'] as String).contains(_query.text) ||
                (f['label'] as String).contains(_query.text),
          ))
            ListTile(
              leading: SocialAvatar(
                api: widget.api,
                person: Map<String, dynamic>.from(f['person'] as Map),
              ),
              title: Text(socialName(f)),
              subtitle: Text('私人标签：${f['label']}'),
              trailing: const Icon(Icons.chevron_right),
              onTap: () => _details(f),
            ),
          if (_friends.isEmpty)
            const Padding(
              padding: EdgeInsets.all(24),
              child: Text('添加好友后，就可以一起记录生活了。'),
            ),
          if (_blocks.isNotEmpty)
            ExpansionTile(
              title: const Text('已拉黑'),
              children: [
                for (final id in _blocks)
                  ListTile(
                    title: Text(_blockedNames[id] ?? '账号资料暂不可用'),
                    trailing: TextButton(
                      onPressed: () => _act(
                        () => widget.api.request(
                          'DELETE',
                          '/api/v1/friends/blocks/$id',
                        ),
                      ),
                      child: const Text('取消拉黑'),
                    ),
                  ),
              ],
            ),
        ],
      ),
    ),
  );
}
