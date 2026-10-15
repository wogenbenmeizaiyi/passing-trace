import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:video_player/video_player.dart';

import '../auth_service.dart';
import '../user_facing_error.dart';
import 'social_api.dart';
import 'social_widgets.dart';

class SharedContentView extends StatefulWidget {
  const SharedContentView({
    super.key,
    required this.auth,
    required this.session,
    this.shareId,
    this.eventId,
  });
  final AuthService auth;
  final AuthSession session;
  final String? shareId;
  final int? eventId;
  @override
  State<SharedContentView> createState() => _SharedContentViewState();
}

class _SharedContentViewState extends State<SharedContentView>
    with WidgetsBindingObserver {
  late final SocialApi _api = SocialApi(widget.auth, widget.session);
  late final SocialFeed _feed = SocialFeed(widget.auth, widget.session);
  SocialRow? _document;
  String? _error;
  String _author = '作者';
  bool _own = false;
  int _version = 0;
  int _feedVersion = -1;
  bool _loading = false, _reloadPending = false;
  String get _endpoint => widget.shareId != null
      ? '/api/v1/shares/${widget.shareId}'
      : '/api/v1/friends/shared-records/${widget.eventId}';
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _feed.addListener(_changed);
    _feed.start();
    _load();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _feed.removeListener(_changed);
    _feed.dispose();
    _api.close();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _feed.start();
      _load();
    } else {
      _feed.stop();
    }
  }

  void _changed() {
    if (_feedVersion == _feed.revision) return;
    _feedVersion = _feed.revision;
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    if (_loading) {
      _reloadPending = true;
      return;
    }
    _loading = true;
    _reloadPending = false;
    setState(() {
      _document = null;
      _error = null;
      _version++;
    });
    try {
      final result = await _api.request('GET', _endpoint);
      final doc = Map<String, dynamic>.from(
        (widget.shareId != null ? result['document'] : result) as Map,
      );
      final people = socialRows(
        await _api.request(
          'POST',
          '/api/v1/people/profiles',
          identity: true,
          body: [doc['authorId']],
        ),
      );
      final me = await _api.request('GET', '/api/v1/people/me', identity: true);
      if (mounted) {
        setState(() {
          _document = doc;
          _author = people.isEmpty ? '作者' : people.first['nickname'] as String;
          _own = doc['authorId'] == me['profile']['id'];
        });
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => _error = userFacingErrorMessage(e, fallback: '内容已不可查看。'),
        );
      }
    } finally {
      _loading = false;
      if (_reloadPending && mounted) _load();
    }
  }

  Future<void> _remove() async {
    if (!await confirmSocial(
      context,
      widget.shareId == null
          ? '移除与你的关联后，你将无法再查看该记录。'
          : '停止分享后，好友无法再打开内容。已保存的图片和截图无法收回。',
    )) {
      return;
    }
    try {
      await _api.request(
        'DELETE',
        widget.shareId == null ? '$_endpoint/participation' : _endpoint,
      );
      if (mounted) Navigator.pop(context);
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    }
  }

  String _titleFor(String key) {
    final nodes = socialRows(_document?['nodes']);
    final node = nodes.where((n) => n['key'] == key);
    if (node.isEmpty) return '节点';
    final records = socialRows(_document?['records'])
        .where((r) => r['eventId'] == node.first['eventId']);
    return records.isEmpty ? '不可查看的节点' : records.first['title'] as String;
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(widget.shareId == null ? '共同参与的记录' : '好友分享'),
      actions: [
        IconButton(
          onPressed: _load,
          icon: const Icon(Icons.refresh),
          tooltip: '刷新内容',
        ),
      ],
    ),
    body: ListView(
      padding: const EdgeInsets.all(20),
      children: [
        if (_error != null) Text(_error!),
        if (_document == null && _error == null)
          const Center(child: CircularProgressIndicator()),
        if (_document != null) ...[
          Text(
            _document!['title'] as String,
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          Text('作者：$_author · ${widget.shareId == null ? '随作者更新' : '发送时版本'}'),
          if (_document!['description'] != null)
            Text(_document!['description'] as String),
          if (_document!['available'] == true) ...[
            if (socialRows(_document!['stages']).isNotEmpty)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 16),
                child: Text(
                  socialRows(_document!['stages'])
                      .map((s) => s['title'])
                      .join(' → '),
                ),
              ),
            for (final r in socialRows(_document!['records']))
              Card(
                child: ExpansionTile(
                  key: ValueKey('$_version-${r['eventId']}'),
                  initiallyExpanded: _document!['kind'] == 'record',
                  title: Text(r['title'] as String),
                  subtitle: r['available'] == true
                      ? Text(
                          '${r['status'] == 'Completed'
                              ? '已完成'
                              : r['status'] == 'Planned'
                              ? '待执行'
                              : '已取消'} · ${r['happenedAt'] ?? r['plannedAt'] ?? '未填写时间'}',
                        )
                      : null,
                  children: [
                    if (r['available'] == true)
                      Padding(
                        padding: const EdgeInsets.all(16),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            if ((r['labels'] as List?)?.isNotEmpty == true)
                              Text((r['labels'] as List).join(' · ')),
                            SelectableText(r['content'] as String? ?? ''),
                            for (final place in socialRows(r['places']))
                              Padding(
                                padding: const EdgeInsets.symmetric(
                                  vertical: 8,
                                ),
                                child: Text(
                                  '${place['name']}\n${place['address'] ?? ''}',
                                ),
                              ),
                            for (final media in socialRows(r['media']))
                              _SharedAttachment(
                                api: _api,
                                path: '$_endpoint/media/${media['id']}',
                                media: media,
                              ),
                          ],
                        ),
                      ),
                  ],
                ),
              ),
            if (socialRows(_document!['edges']).isNotEmpty) ...[
              const SizedBox(height: 16),
              Text('节点关联', style: Theme.of(context).textTheme.titleMedium),
              for (final e in socialRows(_document!['edges']))
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Text(
                    '${_titleFor(e['source'] as String)} → ${_titleFor(e['target'] as String)} ${e['label'] ?? ''}',
                  ),
                ),
            ],
            if (widget.shareId == null || _own)
              TextButton(
                onPressed: _remove,
                child: Text(widget.shareId == null ? '移除与我的关联' : '停止这次分享'),
              ),
          ],
        ],
      ],
    ),
  );
}

class _SharedAttachment extends StatefulWidget {
  const _SharedAttachment({
    required this.api,
    required this.path,
    required this.media,
  });
  final SocialApi api;
  final String path;
  final SocialRow media;
  @override
  State<_SharedAttachment> createState() => _SharedAttachmentState();
}

class _SharedAttachmentState extends State<_SharedAttachment> {
  Uint8List? _image;
  VideoPlayerController? _video;
  bool _busy = false;
  String? _error;
  @override
  void dispose() {
    _video?.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() => _busy = true);
    try {
      final mime = widget.media['mimeType'] as String;
      if (mime.startsWith('video/')) {
        final access = await widget.api.mediaAccess(widget.path);
        final video = VideoPlayerController.networkUrl(
          access.$1,
          httpHeaders: access.$2,
        );
        await video.initialize();
        if (!mounted) {
          await video.dispose();
          return;
        }
        _video = video;
      } else {
        final bytes = await widget.api.bytes(widget.path);
        if (!mounted) return;
        if (mime.startsWith('image/')) {
          _image = bytes;
        } else {
          await FilePicker.saveFile(
            fileName: widget.media['name'] as String,
            bytes: bytes,
          );
        }
      }
      if (mounted) setState(() => _error = null);
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 8),
    child: Column(
      children: [
        if (_image != null)
          Image.memory(_image!, semanticLabel: widget.media['name'] as String),
        if (_video != null) ...[
          AspectRatio(
            aspectRatio: _video!.value.aspectRatio,
            child: VideoPlayer(_video!),
          ),
          TextButton(
            onPressed: () => setState(() {
              _video!.value.isPlaying ? _video!.pause() : _video!.play();
            }),
            child: Text(_video!.value.isPlaying ? '暂停' : '播放'),
          ),
        ],
        if (_image == null && _video == null)
          OutlinedButton.icon(
            onPressed: _busy ? null : _load,
            icon: const Icon(Icons.attach_file),
            label: Text(_busy ? '正在加载…' : '查看附件 · ${widget.media['name']}'),
          ),
        if (_error != null) Text(_error!),
      ],
    ),
  );
}

Future<void> shareSocialContent(
  BuildContext context,
  SocialApi api, {
  int? eventId,
  String? storylineId,
  String? friendshipId,
}) async {
  await showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _ShareSheet(
      api: api,
      eventId: eventId,
      storylineId: storylineId,
      friendshipId: friendshipId,
    ),
  );
}

class _ShareSheet extends StatefulWidget {
  const _ShareSheet({
    required this.api,
    this.eventId,
    this.storylineId,
    this.friendshipId,
  });
  final SocialApi api;
  final int? eventId;
  final String? storylineId, friendshipId;
  @override
  State<_ShareSheet> createState() => _ShareSheetState();
}

class _ShareSheetState extends State<_ShareSheet> {
  List<SocialRow> _friends = [], _sent = [];
  SocialRow? _preview;
  late String? _friend = widget.friendshipId;
  String _key = messageKey();
  String? _error;
  bool _busy = false;
  SocialRow get _payload => {
    'clientMessageId': _key,
    'kind': widget.eventId == null ? 'storyline' : 'record',
    'eventId': widget.eventId,
    'storylineId': widget.storylineId,
  };
  String get _sharesPath =>
      '/api/v1/shares?${widget.eventId == null ? 'storylineId=${widget.storylineId}' : 'eventId=${widget.eventId}'}';
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final data = await Future.wait([
        widget.api.request('GET', '/api/v1/friends'),
        widget.api.request('POST', '/api/v1/shares/preview', body: _payload),
        widget.api.request('GET', _sharesPath),
      ]);
      if (mounted) {
        setState(() {
          _friends = socialRows(data[0]);
          _preview = Map<String, dynamic>.from(data[1] as Map);
          _sent = socialRows(data[2]);
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    }
  }

  Future<void> _send() async {
    if (_friend == null || _busy) return;
    setState(() => _busy = true);
    try {
      final c = await widget.api.request(
        'POST',
        '/api/v1/conversations',
        body: {'friendshipId': _friend},
      );
      await widget.api.request(
        'POST',
        '/api/v1/conversations/${c['id']}/messages',
        body: _payload,
      );
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('已分享给好友')));
        Navigator.pop(context);
      }
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _revoke(String id) async {
    if (!await confirmSocial(context, '停止这次分享？好友将无法再打开该版本。')) return;
    try {
      await widget.api.request('DELETE', '/api/v1/shares/$id');
      await _load();
    } catch (e) {
      if (mounted) setState(() => _error = userFacingErrorMessage(e));
    }
  }

  String _recipientName(String id) {
    final matches = _friends.where((f) => (f['person'] as Map)['id'] == id);
    return matches.isEmpty ? '已结束好友关系的接收人' : socialName(matches.first);
  }

  @override
  Widget build(BuildContext context) => SafeArea(
    child: SingleChildScrollView(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('分享给好友', style: Theme.of(context).textTheme.titleLarge),
          if (_error != null) Text(_error!),
          if (_preview != null) ...[
            const SizedBox(height: 16),
            Text(_preview!['title'] as String),
            Text('包含 ${socialRows(_preview!['records']).length} 条记录及附件'),
            ExpansionTile(
              title: const Text('查看将分享的内容'),
              children: [
                for (final record in socialRows(_preview!['records']))
                  ListTile(
                    title: Text(record['title'] as String),
                    subtitle: Text(
                      '${record['content'] ?? ''}${socialRows(record['media']).isEmpty ? '' : '\n附件：${socialRows(record['media']).map((m) => m['name']).join('、')}'}',
                    ),
                  ),
              ],
            ),
            const Text('好友看到发送时的版本，后续修改不会同步。'),
          ],
          const SizedBox(height: 16),
          DropdownButtonFormField<String>(
            initialValue: _friends.any((f) => f['id'] == _friend)
                ? _friend
                : null,
            decoration: const InputDecoration(labelText: '接收好友'),
            items: [
              for (final f in _friends)
                DropdownMenuItem(
                  value: f['id'] as String,
                  child: Text(socialName(f)),
                ),
            ],
            onChanged: _busy
                ? null
                : (v) => setState(() {
                    _friend = v;
                    _key = messageKey();
                  }),
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: _busy || _friend == null || _preview == null
                ? null
                : _send,
            child: Text(_busy ? '正在发送…' : '确认分享'),
          ),
          if (_sent.isNotEmpty)
            ExpansionTile(
              title: const Text('管理已发送的分享'),
              children: [
                for (final s in _sent)
                  ListTile(
                    title: Text(_recipientName(s['recipientId'] as String)),
                    subtitle: Text(s['createdAt'] as String),
                    trailing: s['revokedAt'] == null
                        ? TextButton(
                            onPressed: () => _revoke(s['id'] as String),
                            child: const Text('停止分享'),
                          )
                        : const Text('已停止'),
                  ),
              ],
            ),
        ],
      ),
    ),
  );
}
