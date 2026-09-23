import 'dart:async';

import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../user_facing_error.dart';
import 'friends_view.dart';
import 'add_friend_view.dart';
import 'shared_content_view.dart';
import 'social_api.dart';
import 'social_widgets.dart';

class MessagesView extends StatefulWidget {
  const MessagesView({
    super.key,
    required this.auth,
    required this.session,
    required this.feed,
    this.api,
    this.drawer,
    this.bottomNavigationBar,
  });
  final AuthService auth;
  final AuthSession session;
  final SocialFeed feed;
  final SocialApi? api;
  final Widget? drawer, bottomNavigationBar;
  @override
  State<MessagesView> createState() => _MessagesViewState();
}

class _MessagesViewState extends State<MessagesView> {
  late final SocialApi _api =
      widget.api ?? SocialApi(widget.auth, widget.session);
  final Map<String, String> _drafts = {};
  List<SocialRow> _items = [];
  String? _cursor, _error;
  bool _busy = false;
  int _revision = -1;
  String _me = '';
  SocialRow? _myProfile, _notificationSummary;
  final _search = TextEditingController();
  bool _searching = false;
  @override
  void initState() {
    super.initState();
    widget.feed.addListener(_changed);
    _load();
  }

  @override
  void dispose() {
    _search.dispose();
    widget.feed.removeListener(_changed);
    _api.close();
    super.dispose();
  }

  void _changed() {
    if (mounted) setState(() {});
    if (_revision == widget.feed.revision) return;
    _revision = widget.feed.revision;
    _load();
  }

  Future<void> _load({bool more = false}) async {
    if (_busy) return;
    _busy = true;
    try {
      final page = await _api.request(
        'GET',
        '/api/v1/conversations?limit=20${more && _cursor != null ? '&cursor=${Uri.encodeQueryComponent(_cursor!)}' : ''}',
      );
      if (_me.isEmpty) {
        final me = await _api.request(
          'GET',
          '/api/v1/people/me',
          identity: true,
        );
        _myProfile = Map<String, dynamic>.from(me['profile'] as Map);
        _me = _myProfile!['id'] as String;
      }
      final summary = await _api.request(
        'GET',
        '/api/v1/notifications/summary',
      );
      if (mounted) {
        setState(() {
          _notificationSummary = Map<String, dynamic>.from(summary as Map);
          final rows = socialRows(page['items']);
          _items = more
              ? [
                  ..._items,
                  ...rows.where((r) => !_items.any((x) => x['id'] == r['id'])),
                ]
              : rows;
          _cursor = page['nextCursor'] as String?;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    } finally {
      _busy = false;
    }
  }

  Future<void> _chat(String id) async {
    await Navigator.push<void>(
      context,
      MaterialPageRoute(
        builder: (_) => DirectChatView(
          api: _api,
          conversationId: id,
          me: _me,
          myProfile: _myProfile,
          initialDraft: _drafts[id] ?? '',
          onDraftChanged: (draft) => _drafts[id] = draft,
          feed: widget.feed,
        ),
      ),
    );
    if (mounted) _load();
  }

  Future<void> _friendChat(String friendshipId) async {
    try {
      final c = await _api.request(
        'POST',
        '/api/v1/conversations',
        body: {'friendshipId': friendshipId},
      );
      if (mounted) await _chat(c['id'] as String);
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    }
  }

  Future<void> _notifications() async {
    await Navigator.push<void>(
      context,
      MaterialPageRoute(
        builder: (_) => NotificationsView(api: _api, onChat: _chat),
      ),
    );
    if (mounted) await _load();
  }

  Future<void> _contacts() async {
    await Navigator.push<void>(
      context,
      MaterialPageRoute(
        settings: const RouteSettings(name: '/contacts'),
        builder: (_) =>
            FriendsView(api: _api, onChat: _friendChat, feed: widget.feed),
      ),
    );
    if (mounted) await _load();
  }

  Future<void> _add(String action) async {
    String code = '';
    if (action == 'scan') {
      final scanned = await Navigator.push<String>(
        context,
        MaterialPageRoute(builder: (_) => const FriendScannerView()),
      );
      if (!mounted || scanned == null) return;
      code = scanned;
    }
    if (!mounted) return;
    await Navigator.push<void>(
      context,
      MaterialPageRoute(
        builder: (_) => action == 'code'
            ? FriendCodeView(api: _api)
            : AddFriendView(api: _api, initialCode: code),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final query = _search.text.trim().toLowerCase();
    final visible = _items
        .where(
          (item) =>
              query.isEmpty ||
              '${item['person']['nickname']} ${item['preview']}'
                  .toLowerCase()
                  .contains(query),
        )
        .toList();
    return Scaffold(
      drawer: widget.drawer,
      bottomNavigationBar: widget.bottomNavigationBar,
      appBar: AppBar(
        title: const Text('消息'),
        centerTitle: false,
        titleSpacing: 8,
        actions: [
          IconButton(
            tooltip: _searching ? '关闭搜索' : '搜索会话',
            icon: Icon(_searching ? Icons.search_off : Icons.search),
            onPressed: () => setState(() {
              _searching = !_searching;
              if (!_searching) _search.clear();
            }),
          ),
          IconButton(
            tooltip: '联系人',
            icon: const Icon(Icons.people_outline),
            onPressed: _contacts,
          ),
          PopupMenuButton<String>(
            tooltip: '添加',
            icon: const Icon(Icons.add),
            onSelected: _add,
            itemBuilder: (_) => const [
              PopupMenuItem(value: 'add', child: Text('添加好友')),
              PopupMenuItem(value: 'scan', child: Text('扫码添加')),
              PopupMenuItem(value: 'code', child: Text('我的好友码')),
            ],
          ),
        ],
      ),
      body: Column(
        children: [
          if (_searching)
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
              child: TextField(
                controller: _search,
                autofocus: true,
                onChanged: (_) => setState(() {}),
                decoration: InputDecoration(
                  hintText: '搜索已加载的会话',
                  prefixIcon: const Icon(Icons.search),
                  suffixIcon: IconButton(
                    tooltip: '清空搜索',
                    icon: const Icon(Icons.close),
                    onPressed: () => setState(_search.clear),
                  ),
                ),
              ),
            ),
          if (widget.feed.reconnecting)
            const Padding(
              padding: EdgeInsets.all(12),
              child: Text('新消息同步暂时中断，正在重试'),
            ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: _load,
              child: ListView.builder(
                key: const PageStorageKey('conversations'),
                physics: const AlwaysScrollableScrollPhysics(),
                itemCount: visible.length + 2,
                itemBuilder: (context, index) {
                  if (index == 0) {
                    final latest = _notificationSummary?['latest'] as Map?;
                    if (_searching || latest == null) {
                      return const SizedBox.shrink();
                    }
                    final unread =
                        (_notificationSummary?['unreadCount'] as num?) ?? 0;
                    return ListTile(
                      leading: const CircleAvatar(
                        child: Icon(Icons.people_alt_outlined),
                      ),
                      title: const Text('好友通知'),
                      subtitle: Text(
                        latest['text'] as String? ?? '',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                      trailing: unread > 0
                          ? Badge(label: Text('$unread'))
                          : const Icon(Icons.chevron_right),
                      onTap: _notifications,
                    );
                  }
                  if (index > visible.length) {
                    return Padding(
                      padding: const EdgeInsets.all(24),
                      child: Column(
                        children: [
                          if (_error != null) Text(_error!),
                          if (visible.isEmpty) ...[
                            Text(
                              _searching
                                  ? '已加载的会话中没有匹配结果。'
                                  : '还没有聊天，和好友聊聊共同的生活吧。',
                            ),
                            TextButton(
                              onPressed: _contacts,
                              child: const Text('查看好友'),
                            ),
                          ],
                          if (_cursor != null)
                            TextButton(
                              onPressed: () => _load(more: true),
                              child: const Text('加载更多'),
                            ),
                        ],
                      ),
                    );
                  }
                  final c = visible[index - 1];
                  return ListTile(
                    key: ValueKey(c['id']),
                    leading: SocialAvatar(
                      api: _api,
                      person: Map<String, dynamic>.from(c['person'] as Map),
                    ),
                    title: Text(
                      c['person']['nickname'] as String,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    subtitle: Text(
                      c['preview'] as String,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    trailing: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(
                          socialMessageTime(c['updatedAt'] as String? ?? ''),
                          style: Theme.of(context).textTheme.labelSmall,
                        ),
                        if ((c['unreadCount'] as num) > 0)
                          Badge(label: Text('${c['unreadCount']}')),
                      ],
                    ),
                    onTap: () => _chat(c['id'] as String),
                  );
                },
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class DirectChatView extends StatefulWidget {
  const DirectChatView({
    super.key,
    required this.api,
    required this.conversationId,
    required this.me,
    this.myProfile,
    this.feed,
    this.initialDraft = '',
    this.onDraftChanged,
  });
  final SocialApi api;
  final String conversationId, me;
  final SocialRow? myProfile;
  final SocialFeed? feed;
  final String initialDraft;
  final ValueChanged<String>? onDraftChanged;
  @override
  State<DirectChatView> createState() => _DirectChatViewState();
}

class _DirectChatViewState extends State<DirectChatView>
    with WidgetsBindingObserver {
  late final _draft = TextEditingController(text: widget.initialDraft);
  final _scroll = ScrollController();
  List<SocialRow> _messages = [];
  SocialRow? _summary;
  String? _older, _error;
  SocialRow? _pending;
  bool _busy = false, _loading = false, _syncing = false;
  int _read = 0, _revision = -1;
  bool _syncAgain = false;
  SocialFeed? _ownedFeed;
  SocialFeed? get _feed => widget.feed ?? _ownedFeed;
  String get _base => '/api/v1/conversations/${widget.conversationId}';
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (widget.feed == null) {
      _ownedFeed = SocialFeed(widget.api.auth, widget.api.session)..start();
    }
    _feed?.addListener(_changed);
    _draft.addListener(_saveDraft);
    _scroll.addListener(_scrolled);
    _load();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _feed?.removeListener(_changed);
    _ownedFeed?.dispose();
    _draft.dispose();
    _scroll.dispose();
    super.dispose();
  }

  void _saveDraft() => widget.onDraftChanged?.call(_draft.text);

  void _changed() {
    if (mounted) setState(() {});
    if (_revision == _feed?.revision) return;
    _revision = _feed?.revision ?? 0;
    _sync();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _ownedFeed?.start();
      _sync();
    } else {
      _ownedFeed?.stop();
    }
  }

  Future<void> _load({bool older = false}) async {
    if (_loading) return;
    setState(() => _loading = true);
    try {
      final page = await widget.api.request(
        'GET',
        '$_base/messages?limit=30${older && _older != null ? '&before=$_older' : ''}',
      );
      final c = await widget.api.request('GET', _base);
      if (!mounted) return;
      setState(() {
        _summary = Map<String, dynamic>.from(c as Map);
        _messages = older
            ? [...socialRows(page['items']), ..._messages]
            : socialRows(page['items']);
        _messages.removeWhere(
          (m) => (m['id'] as num) <= (c['clearedThroughId'] as num? ?? 0),
        );
        _older = page['nextCursor'] as String?;
        _error = null;
      });
      await _mark();
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _mark() async {
    if (!mounted ||
        ModalRoute.of(context)?.isCurrent != true ||
        (_scroll.hasClients && _scroll.offset > 100) ||
        _messages.isEmpty) {
      return;
    }
    final latest = (_messages.last['id'] as num).toInt();
    if (latest <= _read) return;
    _read = latest;
    try {
      await widget.api.request(
        'PUT',
        '$_base/read',
        body: {'throughId': latest},
      );
    } catch (_) {
      _read = 0;
    }
  }

  void _scrolled() {
    if (_scroll.hasClients && _scroll.offset <= 100) _mark();
  }

  Future<void> _sync() async {
    if (_syncing) {
      _syncAgain = true;
      return;
    }
    if (_loading ||
        WidgetsBinding.instance.lifecycleState == AppLifecycleState.paused) {
      return;
    }
    _syncing = true;
    try {
      do {
        _syncAgain = false;
        var after = _messages.isEmpty
            ? 0
            : (_messages.last['id'] as num).toInt();
        var more = true;
        while (more && mounted) {
          final page = await widget.api.request(
            'GET',
            '$_base/messages?limit=30&after=$after',
          );
          if (!mounted) return;
          final rows = socialRows(page['items']);
          setState(
            () => _messages.addAll(
              rows.where((r) => !_messages.any((m) => m['id'] == r['id'])),
            ),
          );
          final next = rows.isEmpty ? after : (rows.last['id'] as num).toInt();
          more = page['nextCursor'] != null && next > after;
          after = next;
        }
        final c = await widget.api.request('GET', _base);
        if (mounted) {
          setState(() {
            _summary = Map<String, dynamic>.from(c as Map);
            _messages.removeWhere(
              (m) => (m['id'] as num) <= (c['clearedThroughId'] as num? ?? 0),
            );
          });
          final ids = _messages
              .map((m) => m['shareId'])
              .whereType<String>()
              .toSet()
              .toList();
          for (var at = 0; at < ids.length; at += 100) {
            final rows = socialRows(
              await widget.api.request(
                'POST',
                '$_base/share-statuses',
                body: ids.sublist(at, (at + 100).clamp(0, ids.length)),
              ),
            );
            if (!mounted) return;
            final updates = {for (final row in rows) row['id']: row};
            setState(() {
              for (final message in _messages) {
                final update = updates[message['shareId']];
                if (update != null) {
                  message['shareTitle'] = update['title'];
                  message['shareAvailable'] = update['available'];
                }
              }
            });
          }
        }
        await _mark();
      } while (_syncAgain && mounted);
    } catch (_) {
    } finally {
      _syncing = false;
    }
  }

  Future<void> _send({bool retry = false}) async {
    if (_busy || (!retry && _draft.text.trim().isEmpty)) return;
    final payload = retry
        ? _pending
        : {
            'clientMessageId': messageKey(),
            'kind': 'text',
            'text': _draft.text.trim(),
          };
    if (payload == null) return;
    setState(() {
      _busy = true;
      _pending = payload;
      _error = null;
    });
    try {
      final message = Map<String, dynamic>.from(
        await widget.api.request('POST', '$_base/messages', body: payload)
            as Map,
      );
      if (mounted) {
        setState(() {
          if (!_messages.any((m) => m['id'] == message['id'])) {
            _messages.add(message);
          }
          if (_draft.text.trim() == payload['text']) _draft.clear();
          _pending = null;
        });
      }
      if (_scroll.hasClients) _scroll.jumpTo(0);
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _clear() async {
    if (!await confirmSocial(context, '只清除你这边的聊天显示，不会替对方删除消息，也不会停止分享。')) return;
    try {
      await widget.api.request('DELETE', '$_base/messages');
      if (mounted) {
        setState(() {
          _messages = [];
          _older = null;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    }
  }

  Future<void> _share(String kind) async {
    await Navigator.push<void>(
      context,
      MaterialPageRoute(
        builder: (_) => _ShareChooser(
          api: widget.api,
          kind: kind,
          friendshipId: _summary?['friendshipId'] as String?,
        ),
      ),
    );
    await _sync();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(_summary?['person']['nickname'] as String? ?? '聊天'),
      actions: [
        PopupMenuButton<String>(
          onSelected: (_) => _clear(),
          itemBuilder: (_) => [
            const PopupMenuItem(value: 'clear', child: Text('清除我的聊天记录')),
          ],
        ),
      ],
    ),
    body: Column(
      children: [
        if (_feed?.reconnecting == true)
          const Padding(
            padding: EdgeInsets.all(12),
            child: Text('新消息同步暂时中断，正在重试'),
          ),
        Expanded(
          child: ListView.builder(
            controller: _scroll,
            reverse: true,
            padding: const EdgeInsets.all(16),
            itemCount: _messages.length + 1,
            itemBuilder: (context, index) {
              if (index == _messages.length) {
                return Center(
                  child: _older != null
                      ? TextButton(
                          onPressed: _loading ? null : () => _load(older: true),
                          child: Text(_loading ? '加载中…' : '查看更早消息'),
                        )
                      : _loading
                      ? const CircularProgressIndicator()
                      : const SizedBox.shrink(),
                );
              }
              final m = _messages[_messages.length - index - 1];
              final own = m['senderId'] == widget.me;
              final previous = index + 1 < _messages.length
                  ? _messages[_messages.length - index - 2]
                  : null;
              return Column(
                children: [
                  if (showSocialMessageTime(m, previous))
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: Text(
                        socialMessageTime(m['createdAt'] as String, full: true),
                        style: Theme.of(context).textTheme.labelSmall,
                      ),
                    ),
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    textDirection: own ? TextDirection.rtl : TextDirection.ltr,
                    children: [
                      SocialAvatar(
                        api: widget.api,
                        person: own
                            ? (widget.myProfile ??
                                  {
                                    'id': widget.me,
                                    'nickname': '我',
                                    'hasAvatar': false,
                                  })
                            : Map<String, dynamic>.from(
                                _summary?['person'] as Map? ??
                                    {
                                      'id': m['senderId'],
                                      'nickname': '好友',
                                      'hasAvatar': false,
                                    },
                              ),
                      ),
                      const SizedBox(width: 8),
                      Flexible(
                        child: Align(
                          alignment: own
                              ? Alignment.centerRight
                              : Alignment.centerLeft,
                          child: Container(
                            constraints: BoxConstraints(
                              maxWidth: MediaQuery.sizeOf(context).width * .75,
                            ),
                            margin: const EdgeInsets.only(bottom: 16),
                            child: Column(
                              crossAxisAlignment: own
                                  ? CrossAxisAlignment.end
                                  : CrossAxisAlignment.start,
                              children: [
                                Card(
                                  color: own
                                      ? Theme.of(context)
                                            .colorScheme
                                            .primaryContainer
                                      : null,
                                  child: m['kind'] == 'text'
                                      ? Padding(
                                          padding: const EdgeInsets.all(14),
                                          child: SelectableText(
                                            m['text'] as String,
                                          ),
                                        )
                                      : ListTile(
                                          title: Text(
                                            m['shareAvailable'] == true
                                                ? (m['shareTitle'] as String? ??
                                                      '好友分享')
                                                : '内容已不可查看',
                                          ),
                                          subtitle: Text(
                                            m['kind'] == 'record'
                                                ? '分享的记录 · 发送时版本'
                                                : '分享的故事线 · 发送时版本',
                                          ),
                                          trailing: const Icon(
                                            Icons.chevron_right,
                                          ),
                                          onTap: m['shareAvailable'] == true
                                              ? () async {
                                                  await Navigator.push<void>(
                                                    context,
                                                    MaterialPageRoute(
                                                      builder: (_) =>
                                                          SharedContentView(
                                                            auth:
                                                                widget.api.auth,
                                                            session: widget
                                                                .api
                                                                .session,
                                                            shareId:
                                                                m['shareId']
                                                                    as String,
                                                          ),
                                                    ),
                                                  );
                                                  await _sync();
                                                }
                                              : null,
                                        ),
                                ),
                                if (own)
                                  Text(
                                    (m['id'] as num) <=
                                            (_summary?['peerReadThroughId']
                                                    as num? ??
                                                0)
                                        ? '已读'
                                        : '已发送',
                                    style: Theme.of(context)
                                        .textTheme
                                        .labelSmall,
                                  ),
                              ],
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                ],
              );
            },
          ),
        ),
        if (_error != null)
          Padding(
            padding: const EdgeInsets.all(12),
            child: Row(
              children: [
                Expanded(child: Text(_error!)),
                TextButton(
                  onPressed: _busy
                      ? null
                      : () => _pending != null ? _send(retry: true) : _load(),
                  child: const Text('重试'),
                ),
              ],
            ),
          ),
        SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: _summary?['canSend'] == false
                ? const Text('好友关系已结束，无法继续发送消息。')
                : Column(
                    children: [
                      Row(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          PopupMenuButton<String>(
                            tooltip: '分享内容',
                            icon: const Icon(Icons.add_circle_outline),
                            enabled: !_busy,
                            onSelected: _share,
                            itemBuilder: (_) => const [
                              PopupMenuItem(
                                value: 'record',
                                child: Text('分享记录'),
                              ),
                              PopupMenuItem(
                                value: 'storyline',
                                child: Text('分享故事线'),
                              ),
                            ],
                          ),
                          Expanded(
                            child: TextField(
                              controller: _draft,
                              minLines: 1,
                              maxLines: 5,
                              maxLength: 8000,
                              decoration: const InputDecoration(
                                hintText: '和好友聊聊…',
                                counterText: '',
                              ),
                            ),
                          ),
                          const SizedBox(width: 8),
                          IconButton.filled(
                            tooltip: '发送',
                            onPressed: _busy ? null : () => _send(),
                            icon: _busy
                                ? const SizedBox.square(
                                    dimension: 20,
                                    child: CircularProgressIndicator(),
                                  )
                                : const Icon(Icons.send),
                          ),
                        ],
                      ),
                    ],
                  ),
          ),
        ),
      ],
    ),
  );
}

class _ShareChooser extends StatefulWidget {
  const _ShareChooser({
    required this.api,
    required this.kind,
    this.friendshipId,
  });
  final SocialApi api;
  final String kind;
  final String? friendshipId;
  @override
  State<_ShareChooser> createState() => _ShareChooserState();
}

class _ShareChooserState extends State<_ShareChooser> {
  final List<SocialRow> _items = [];
  String? _cursor, _error;
  bool _busy = false;
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (_busy) return;
    _busy = true;
    try {
      final page = await widget.api.request(
        'GET',
        '/api/v1/${widget.kind == 'record' ? 'events' : 'storylines'}?limit=20${_cursor != null ? '&cursor=${Uri.encodeQueryComponent(_cursor!)}' : ''}',
      );
      if (mounted) {
        setState(() {
          _items.addAll(socialRows(page['items']));
          _cursor = page['nextCursor']?.toString();
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    } finally {
      _busy = false;
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('选择自己的内容')),
    body: ListView.builder(
      itemCount: _items.length + 1,
      itemBuilder: (context, index) {
        if (index == _items.length) {
          return Column(
            children: [
              if (_error != null) Text(_error!),
              if (_cursor != null)
                TextButton(onPressed: _load, child: const Text('加载更多')),
            ],
          );
        }
        final r = _items[index];
        return ListTile(
          title: Text(r['title'] as String? ?? '未命名'),
          trailing: const Icon(Icons.share_outlined),
          onTap: () => shareSocialContent(
            context,
            widget.api,
            eventId: widget.kind == 'record' ? (r['id'] as num).toInt() : null,
            storylineId: widget.kind == 'storyline' ? r['id'] as String : null,
            friendshipId: widget.friendshipId,
          ),
        );
      },
    ),
  );
}

class NotificationsView extends StatefulWidget {
  const NotificationsView({super.key, required this.api, required this.onChat});
  final SocialApi api;
  final ValueChanged<String> onChat;
  @override
  State<NotificationsView> createState() => _NotificationsViewState();
}

class _NotificationsViewState extends State<NotificationsView> {
  final List<SocialRow> _items = [];
  String? _error;
  bool _hasMore = true, _busy = false;
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (_busy) return;
    _busy = true;
    try {
      final rows = socialRows(
        await widget.api.request(
          'GET',
          '/api/v1/notifications?visibleOnly=true${_items.isEmpty ? '' : '&before=${_items.last['id']}'}',
        ),
      );
      if (mounted) {
        setState(() {
          _items.addAll(rows);
          _hasMore = rows.length == 30;
        });
      }
      if (rows.isNotEmpty) {
        await widget.api.request(
          'PUT',
          '/api/v1/notifications/read?visibleOnly=true',
          body: {'throughId': rows.first['id']},
        );
      }
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    } finally {
      _busy = false;
    }
  }

  void _open(SocialRow n) {
    final path = n['target'] as String? ?? '';
    if (path == '/events?scope=joint' || path == '/events') {
      Navigator.push<void>(
        context,
        MaterialPageRoute(
          builder: (_) => JointRecordsView(
            auth: widget.api.auth,
            session: widget.api.session,
          ),
        ),
      );
      return;
    }
    final id = RegExp(r'^/messages/([\da-f-]{36})$').firstMatch(path)?.group(1);
    if (id != null) {
      widget.onChat(id);
      return;
    }
    final record = RegExp(r'^/joint-records/(\d+)$').firstMatch(path)?.group(1);
    if (record != null) {
      Navigator.push<void>(
        context,
        MaterialPageRoute(
          builder: (_) => SharedContentView(
            auth: widget.api.auth,
            session: widget.api.session,
            eventId: int.parse(record),
          ),
        ),
      );
      return;
    }
    Navigator.push<void>(
      context,
      MaterialPageRoute(
        builder: (_) => FriendsView(
          api: widget.api,
          initialPanel: n['kind'] == 'friend-request' ? 'requests' : '',
          onChat: (friend) async {
            final c = await widget.api.request(
              'POST',
              '/api/v1/conversations',
              body: {'friendshipId': friend},
            );
            widget.onChat(c['id'] as String);
          },
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('好友通知')),
    body: ListView.builder(
      itemCount: _items.length + 1,
      itemBuilder: (context, i) {
        if (i == _items.length) {
          return Column(
            children: [
              if (_error != null) Text(_error!),
              if (_hasMore)
                TextButton(onPressed: _load, child: const Text('查看更早通知')),
            ],
          );
        }
        final n = _items[i];
        return ListTile(
          title: Text(n['text'] as String),
          subtitle: Text(
            socialMessageTime(n['createdAt'] as String, full: true),
          ),
          trailing: const Icon(Icons.chevron_right),
          onTap: () => _open(n),
        );
      },
    ),
  );
}

class JointRecordsView extends StatefulWidget {
  const JointRecordsView({
    super.key,
    required this.auth,
    required this.session,
  });
  final AuthService auth;
  final AuthSession session;
  @override
  State<JointRecordsView> createState() => _JointRecordsViewState();
}

class _JointRecordsViewState extends State<JointRecordsView> {
  late final SocialApi _api = SocialApi(widget.auth, widget.session);
  List<SocialRow> _items = [];
  final Map<String, String> _authors = {};
  String? _cursor, _error;
  bool _more = true, _busy = false;
  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _api.close();
    super.dispose();
  }

  Future<void> _load() async {
    if (_busy) return;
    _busy = true;
    try {
      final page = await _api.request(
        'GET',
        '/api/v1/friends/shared-records?limit=20${_cursor != null ? '&before=$_cursor' : ''}',
      );
      final rows = socialRows(page['items']);
      final people = socialRows(
        await _api.request(
          'POST',
          '/api/v1/people/profiles',
          identity: true,
          body: rows.map((r) => r['authorId']).toSet().toList(),
        ),
      );
      if (mounted) {
        setState(() {
          _items.addAll(rows);
          _cursor = page['nextCursor'] as String?;
          _more = _cursor != null;
          for (final p in people) {
            _authors[p['id'] as String] = p['nickname'] as String;
          }
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    } finally {
      _busy = false;
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('共同参与的记录')),
    body: ListView.builder(
      itemCount: _items.length + 1,
      itemBuilder: (context, i) {
        if (i == _items.length) {
          return Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              children: [
                if (_items.isEmpty) const Text('好友在记录中选择你后，会出现在这里。'),
                if (_error != null) Text(_error!),
                if (_more)
                  TextButton(onPressed: _load, child: const Text('加载更多')),
              ],
            ),
          );
        }
        final r = _items[i];
        return ListTile(
          title: Text(r['title'] as String),
          subtitle: Text(
            '作者：${_authors[r['authorId']] ?? '好友'} · ${r['happenedAt'] ?? '未填写时间'}',
          ),
          onTap: () async {
            await Navigator.push<void>(
              context,
              MaterialPageRoute(
                builder: (_) => SharedContentView(
                  auth: widget.auth,
                  session: widget.session,
                  eventId: (r['id'] as num).toInt(),
                ),
              ),
            );
            if (mounted) {
              _items = [];
              _cursor = null;
              await _load();
            }
          },
        );
      },
    ),
  );
}
