import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../user_facing_error.dart';
import 'social_api.dart';
import 'social_widgets.dart';

class FriendsView extends StatefulWidget {
  const FriendsView({
    super.key,
    required this.api,
    required this.onChat,
    this.selectedId,
    this.embedded = false,
    this.initialPanel = '',
    this.feed,
  });
  final SocialApi api;
  final ValueChanged<String> onChat;
  final String? selectedId;
  final bool embedded;
  final String initialPanel;
  final SocialFeed? feed;
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
  Set<String> _collapsed = {};
  String? _preferenceKey;
  String? _notice;
  int _revision = -1;
  void _changed() {
    if (_revision == widget.feed?.revision) return;
    _revision = widget.feed?.revision ?? -1;
    _load();
  }

  @override
  void initState() {
    super.initState();
    widget.feed?.addListener(_changed);
    _load().then((_) {
      final matches = _friends.where((x) => x['id'] == widget.selectedId);
      if (mounted && matches.isNotEmpty) _details(matches.first);
    });
  }

  @override
  void dispose() {
    widget.feed?.removeListener(_changed);
    _query.dispose();
    _code.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      if (_preferenceKey == null) {
        final me = await widget.api.request(
          'GET',
          '/api/v1/people/me',
          identity: true,
        );
        _preferenceKey =
            'friend-groups:${widget.api.session.identityBaseUrl}:${me['profile']['id']}';
        try {
          final stored = await const FlutterSecureStorage().read(
            key: _preferenceKey!,
          );
          _collapsed = (jsonDecode(stored ?? '[]') as List)
              .whereType<String>()
              .toSet();
        } catch (_) {
          /* Preferences must not prevent loading friends. */
        }
      }
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

  Future<void> _act(
    Future<dynamic> Function() action, [
    String? success,
  ]) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
      _notice = null;
    });
    try {
      await action();
      await _load();
      if (mounted && success != null) setState(() => _notice = success);
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
    SocialRow me;
    try {
      me = Map<String, dynamic>.from(
        await widget.api.request('GET', '/api/v1/people/me', identity: true)
            as Map,
      );
    } catch (error) {
      remark.dispose();
      label.dispose();
      if (mounted) setState(() => _error = userFacingErrorMessage(error));
      return;
    }
    if (!mounted) {
      remark.dispose();
      label.dispose();
      return;
    }
    Widget detail(BuildContext sheet) => SafeArea(
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
            Center(
              child: SocialAvatar(
                api: widget.api,
                person: Map<String, dynamic>.from(initial['person'] as Map),
              ),
            ),
            const SizedBox(height: 12),
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
                    body: {'decision': 'clear', 'version': initial['version']},
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
    );
    await Navigator.push<void>(
      context,
      MaterialPageRoute(
        builder: (sheet) => Scaffold(
          appBar: AppBar(title: const Text('好友资料')),
          body: Builder(builder: detail),
        ),
      ),
    );
    remark.dispose();
    label.dispose();
  }

  Future<void> _panel(String panel) async {
    await Navigator.push<void>(
      context,
      MaterialPageRoute(
        builder: (_) => FriendsView(
          api: widget.api,
          onChat: widget.onChat,
          initialPanel: panel,
          feed: widget.feed,
        ),
      ),
    );
    if (mounted) await _load();
  }

  Future<void> _toggle(String label) async {
    setState(() {
      if (!_collapsed.remove(label)) _collapsed.add(label);
    });
    if (_preferenceKey != null) {
      try {
        await const FlutterSecureStorage().write(
          key: _preferenceKey!,
          value: jsonEncode(_collapsed.toList()),
        );
      } catch (_) {
        /* Keep this session's choice when storage is unavailable. */
      }
    }
  }

  List<Widget> _actions() => [
    if (widget.initialPanel.isEmpty) ...[
      IconButton(
        tooltip: '添加好友',
        icon: const Icon(Icons.person_add_alt),
        onPressed: () => _panel('add'),
      ),
      PopupMenuButton<String>(
        tooltip: '更多好友操作',
        onSelected: (value) => value == 'refresh' ? _load() : _panel('blocks'),
        itemBuilder: (_) => const [
          PopupMenuItem(value: 'refresh', child: Text('刷新好友')),
          PopupMenuItem(value: 'blocks', child: Text('已拉黑的用户')),
        ],
      ),
    ],
  ];

  @override
  Widget build(BuildContext context) {
    final panel = widget.initialPanel;
    final title = switch (panel) {
      'add' => '添加好友',
      'requests' => '新的好友',
      'blocks' => '已拉黑的用户',
      _ => '好友',
    };
    final query = _query.text.trim().toLowerCase();
    final groups = <String, List<SocialRow>>{};
    for (final friend in _friends) {
      if (![
        socialName(friend),
        friend['person']['nickname'],
        friend['label'],
        friend['relationship'] ?? '',
      ].any((v) => v.toString().toLowerCase().contains(query))) {
        continue;
      }
      final label = (friend['label'] as String? ?? '').trim();
      groups.putIfAbsent(label.isEmpty ? '朋友' : label, () => []).add(friend);
    }
    final rows = <(String, SocialRow?)>[];
    for (final label in groups.keys.toList()..sort()) {
      rows.add((label, null));
      if (query.isNotEmpty || !_collapsed.contains(label)) {
        final friends = groups[label]!
          ..sort((a, b) => socialName(a).compareTo(socialName(b)));
        rows.addAll(friends.map((f) => (label, f)));
      }
    }

    Widget header = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (widget.embedded)
          Row(
            children: [
              Expanded(
                child: Text(
                  '我的好友',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              ..._actions(),
            ],
          ),
        if (_error != null)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: Text(
              _error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ),
        if (_notice != null)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: Text(_notice!),
          ),
        if (panel.isEmpty) ...[
          TextField(
            controller: _query,
            onChanged: (_) => setState(() {}),
            decoration: InputDecoration(
              labelText: '查找好友',
              hintText: '昵称、备注或关系',
              prefixIcon: const Icon(Icons.search),
              suffixIcon: query.isEmpty
                  ? null
                  : IconButton(
                      tooltip: '清空搜索',
                      onPressed: () => setState(_query.clear),
                      icon: const Icon(Icons.close),
                    ),
            ),
          ),
          const SizedBox(height: 12),
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.person_add_alt),
            title: const Text('新的好友'),
            trailing: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (_requests
                    .where((r) => r['direction'] == 'received')
                    .isNotEmpty)
                  Badge(
                    label: Text(
                      '${_requests.where((r) => r['direction'] == 'received').length}',
                    ),
                  ),
                const SizedBox(width: 8),
                const Icon(Icons.chevron_right),
              ],
            ),
            onTap: () => _panel('requests'),
          ),
          if (_friends.isEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 32),
              child: Column(
                children: [
                  const Text('还没有好友，添加好友一起记录生活。'),
                  const SizedBox(height: 16),
                  FilledButton(
                    onPressed: () => _panel('add'),
                    child: const Text('添加好友'),
                  ),
                ],
              ),
            ),
          if (_friends.isNotEmpty && groups.isEmpty)
            Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                children: [
                  const Text('没有找到匹配的好友'),
                  TextButton(
                    onPressed: () => setState(_query.clear),
                    child: const Text('清空搜索'),
                  ),
                ],
              ),
            ),
        ],
      ],
    );
    final body = RefreshIndicator(
      onRefresh: _load,
      child: panel.isEmpty
          ? ListView.builder(
              key: const PageStorageKey('friend-list'),
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.all(16),
              itemCount: rows.length + 1,
              itemBuilder: (context, index) {
                if (index == 0) return header;
                final (label, friend) = rows[index - 1];
                if (friend == null) {
                  return Semantics(
                    expanded: query.isNotEmpty || !_collapsed.contains(label),
                    child: ListTile(
                      key: ValueKey('group:$label'),
                      contentPadding: EdgeInsets.zero,
                      title: Text(label),
                      subtitle: Text('${groups[label]!.length} 位好友'),
                      trailing: Icon(
                        query.isNotEmpty || !_collapsed.contains(label)
                            ? Icons.expand_less
                            : Icons.expand_more,
                      ),
                      onTap: () => _toggle(label),
                    ),
                  );
                }
                return ListTile(
                  key: ValueKey(friend['id']),
                  contentPadding: const EdgeInsets.symmetric(horizontal: 8),
                  leading: SocialAvatar(
                    api: widget.api,
                    person: Map<String, dynamic>.from(friend['person'] as Map),
                  ),
                  title: Text(socialName(friend)),
                  subtitle: friend['relationship'] == null
                      ? null
                      : Text('双方确认：${friend['relationship']}'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => _details(friend),
                );
              },
            )
          : ListView(
              padding: const EdgeInsets.all(20),
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                header,
                if (panel == 'add') ...[
                  TextField(
                    controller: _code,
                    onChanged: (_) => setState(() {}),
                    maxLength: 100,
                    decoration: const InputDecoration(
                      labelText: '好友码',
                      hintText: '输入或粘贴好友码',
                    ),
                  ),
                  const SizedBox(height: 16),
                  FilledButton(
                    onPressed: _busy || _code.text.trim().isEmpty
                        ? null
                        : () => _act(
                            () => widget.api.request(
                              'POST',
                              '/api/v1/friend-requests',
                              body: {'code': _code.text.trim()},
                            ),
                            '好友申请已发送，等待对方确认。',
                          ),
                    child: Text(_busy ? '正在发送…' : '发送申请'),
                  ),
                  const SizedBox(height: 12),
                  OutlinedButton.icon(
                    onPressed: _busy
                        ? null
                        : () async {
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
                  const SizedBox(height: 24),
                  ListTile(
                    leading: const Icon(Icons.qr_code),
                    title: const Text('我的好友码'),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => Navigator.push<void>(
                      context,
                      MaterialPageRoute(
                        builder: (_) => FriendCodeView(api: widget.api),
                      ),
                    ),
                  ),
                ],
                if (panel == 'requests') ...[
                  if (_requests.isEmpty)
                    const Padding(
                      padding: EdgeInsets.all(24),
                      child: Text('暂时没有待处理的好友申请。'),
                    ),
                  for (final request in _requests)
                    ListTile(
                      leading: SocialAvatar(
                        api: widget.api,
                        person: Map<String, dynamic>.from(
                          request['person'] as Map,
                        ),
                      ),
                      title: Text(request['person']['nickname'] as String),
                      subtitle: Wrap(
                        spacing: 8,
                        children: [
                          for (final decision
                              in request['direction'] == 'received'
                                  ? ['accept', 'reject']
                                  : ['withdraw'])
                            TextButton(
                              onPressed: _busy
                                  ? null
                                  : () => _act(
                                      () => widget.api.request(
                                        'POST',
                                        '/api/v1/friend-requests/${request['id']}/decision',
                                        body: {'decision': decision},
                                      ),
                                    ),
                              child: Text(switch (decision) {
                                'accept' => '接受',
                                'reject' => '拒绝',
                                _ => '撤回申请',
                              }),
                            ),
                        ],
                      ),
                    ),
                ],
                if (panel == 'blocks') ...[
                  if (_blocks.isEmpty)
                    const Padding(
                      padding: EdgeInsets.all(24),
                      child: Text('没有拉黑的用户。'),
                    ),
                  for (final id in _blocks)
                    ListTile(
                      title: Text(_blockedNames[id] ?? '账号资料暂不可用'),
                      trailing: TextButton(
                        onPressed: _busy
                            ? null
                            : () => _act(
                                () => widget.api.request(
                                  'DELETE',
                                  '/api/v1/friends/blocks/$id',
                                ),
                              ),
                        child: const Text('解除拉黑'),
                      ),
                    ),
                ],
              ],
            ),
    );
    if (widget.embedded) return body;
    return Scaffold(
      appBar: AppBar(title: Text(title), actions: _actions()),
      body: body,
    );
  }
}
