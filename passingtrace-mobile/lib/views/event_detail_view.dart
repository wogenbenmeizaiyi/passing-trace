// 详情页：拉取并展示 Event 完整字段，编辑 / 删除入口。

import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:video_player/video_player.dart';

import '../auth_service.dart';
import '../events/event_datetime.dart';
import '../events/event_model.dart';
import '../events/events_api.dart';
import '../events/media_api.dart';
import '../theme/passingtrace_theme.dart';
import 'event_form_view.dart';
import 'event_widgets.dart';

class EventDetailView extends StatefulWidget {
  const EventDetailView({
    super.key,
    required this.auth,
    required this.session,
    required this.eventId,
  });

  final AuthService auth;
  final AuthSession session;
  final int eventId;

  @override
  State<EventDetailView> createState() => _EventDetailViewState();
}

class _EventDetailViewState extends State<EventDetailView> {
  late EventApiClient _api;
  late MediaApiClient _mediaApi;
  EventModel? _event;
  String? _error;
  bool _loading = true;
  bool _deleting = false;

  @override
  void initState() {
    super.initState();
    _initApi();
  }

  Future<void> _initApi() async {
    final baseUrl = await widget.auth.getEventsApiBaseUrl();
    if (!mounted) return;
    setState(() {
      _api = EventApiClient(auth: widget.auth, baseUrl: baseUrl);
      _mediaApi = MediaApiClient(auth: widget.auth, baseUrl: baseUrl);
    });
    await _load();
  }

  @override
  void dispose() {
    _api.close();
    _mediaApi.close();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final event = await _api.get(widget.session, widget.eventId);
      if (!mounted) return;
      setState(() => _event = event);
    } on EventApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = '加载失败：$e');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _edit() async {
    final event = _event;
    if (event == null) return;
    final updated = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => EventFormView(
          auth: widget.auth,
          session: widget.session,
          eventId: event.id,
        ),
      ),
    );
    if (updated == true) {
      await _load();
    }
  }

  Future<void> _delete() async {
    final event = _event;
    if (event == null) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('删除这条记录？'),
        content: const Text('删除后将从时间线中消失，且不会被其他设备看到。'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            style: FilledButton.styleFrom(
              backgroundColor: context.traceColors.danger,
            ),
            child: const Text('删除'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    setState(() => _deleting = true);
    try {
      await _api.remove(widget.session, event.id, version: event.version);
      if (!mounted) return;
      Navigator.of(context).pop(true);
    } on EventApiException catch (e) {
      if (!mounted) return;
      String message = e.message;
      if (e.status == 409) {
        message = '内容已被他人修改，请刷新后重试。';
        await _load();
      } else if (e.status == 404) {
        if (mounted) Navigator.of(context).pop(true);
        return;
      } else if (e.status == 428) {
        message = '本地版本信息缺失，请刷新页面。';
        await _load();
      }
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('删除失败：$e')));
    } finally {
      if (mounted) setState(() => _deleting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text(
          '记录详情',
          style: TextStyle(fontWeight: FontWeight.w700, fontSize: 19),
        ),
        actions: [
          if (_event != null)
            IconButton(
              tooltip: '编辑',
              onPressed: _edit,
              icon: const Icon(Icons.edit_outlined),
            ),
          if (_event != null)
            IconButton(
              tooltip: '删除',
              onPressed: _deleting ? null : _delete,
              icon: const Icon(Icons.delete_outline),
            ),
        ],
      ),
      body: _buildBody(),
    );
  }

  Widget _buildBody() {
    if (_loading) return const Center(child: CircularProgressIndicator());
    if (_error != null && _event == null) {
      return _ErrorView(message: _error!, onRetry: _load);
    }
    final event = _event;
    if (event == null) return const SizedBox.shrink();

    final title = event.title?.isNotEmpty == true
        ? event.title!
        : (event.rawContent?.isNotEmpty == true ? event.rawContent! : '（无标题）');
    final time = event.kind == EventKind.plan
        ? formatLocal(event.plannedAt)
        : formatLocal(event.happenedAt);

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 20, 20, 48),
        children: [
          Row(
            children: [
              EventKindBadge(event.kind),
              const SizedBox(width: 6),
              EventStatusBadge(event.status),
            ],
          ),
          const SizedBox(height: 14),
          Text(
            title,
            style: const TextStyle(
              fontSize: 28,
              height: 1.3,
              fontWeight: FontWeight.w700,
            ),
          ),
          if (event.effectiveClassification.primaryCategory != null ||
              event.effectiveClassification.tags.isNotEmpty) ...[
            const SizedBox(height: 12),
            Wrap(
              spacing: 7,
              runSpacing: 7,
              children: [
                if (event.effectiveClassification.primaryCategory
                    case final category?)
                  Chip(
                    label: Text(category.displayName),
                    visualDensity: VisualDensity.compact,
                  ),
                for (final tag in event.effectiveClassification.tags.take(10))
                  Chip(
                    label: Text('${tag.isAi ? '✦ ' : ''}${tag.displayName}'),
                    visualDensity: VisualDensity.compact,
                  ),
              ],
            ),
          ],
          if (event.title?.isNotEmpty == true &&
              event.rawContent?.isNotEmpty == true) ...[
            const SizedBox(height: 16),
            Text(
              event.rawContent!,
              style: const TextStyle(fontSize: 15, height: 1.85),
            ),
          ],
          if (event.media.isNotEmpty) ...[
            const SizedBox(height: 24),
            const Text(
              '附件',
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 10),
            for (final media in event.media) _buildMedia(media),
          ],
          if (event.locations.isNotEmpty) ...[
            const SizedBox(height: 20),
            Card(
              elevation: 0,
              child: ListTile(
                leading: const Icon(Icons.place_outlined),
                title: Text(event.locations.first.name),
                subtitle: Text(event.locations.first.address ?? '已保存的地点'),
                trailing: event.locations.first.canNavigate
                    ? const Icon(Icons.directions_outlined)
                    : null,
                onTap: event.locations.first.canNavigate
                    ? () => _navigate(event, event.locations.first)
                    : null,
              ),
            ),
          ],
          if (event.semanticStatus != null) ...[
            const SizedBox(height: 24),
            Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(
                color: context.traceColors.primarySoft,
                borderRadius: BorderRadius.circular(16),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Row(
                    children: [
                      Icon(Icons.auto_awesome, size: 18),
                      SizedBox(width: 8),
                      Text(
                        'AI 分析',
                        style: TextStyle(fontWeight: FontWeight.w700),
                      ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Text(event.semanticSummary ?? '状态：${event.semanticStatus}'),
                ],
              ),
            ),
          ],
          const SizedBox(height: 28),
          _DetailRow(
            label: event.kind == EventKind.plan ? '计划时间' : '发生时间',
            value: time,
          ),
          if (event.completedAt != null)
            _DetailRow(label: '完成时间', value: formatLocal(event.completedAt)),
          const Divider(height: 36),
          Text(
            'SOURCE',
            style: TextStyle(
              fontSize: 11,
              letterSpacing: 1.5,
              fontWeight: FontWeight.w800,
              color: context.traceColors.inkTertiary,
            ),
          ),
          const SizedBox(height: 8),
          _DetailRow(label: 'Source 修订', value: '${event.sourceRevision}'),
          _DetailRow(label: '并发令牌 (version)', value: '${event.version}'),
          _DetailRow(label: '可见性', value: '仅自己可见'),
          _DetailRow(label: '创建', value: formatLocal(event.createdAt)),
          _DetailRow(label: '最后更新', value: formatLocal(event.updatedAt)),
        ],
      ),
    );
  }

  Future<void> _navigate(EventModel event, EventLocationModel location) async {
    try {
      final target = await _api.navigationTarget(
        widget.session,
        event.id,
        location.id!,
      );
      final lat = (target['latitude'] as num).toDouble();
      final lon = (target['longitude'] as num).toDouble();
      final name = target['name'] as String;
      final app = Uri.parse(
        'amapuri://route/plan/?sourceApplication=PassingTrace&dlat=$lat&dlon=$lon&dname=${Uri.encodeComponent(name)}&dev=0&t=0',
      );
      if (await canLaunchUrl(app)) {
        await launchUrl(app, mode: LaunchMode.externalApplication);
        return;
      }
      final web = Uri.https('uri.amap.com', '/navigation', {
        'to': '$lon,$lat,$name',
        'mode': 'car',
        'policy': '1',
        'src': 'PassingTrace',
      });
      if (!await launchUrl(web, mode: LaunchMode.externalApplication)) {
        throw StateError('无法打开地图应用。');
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('导航失败：$e')));
      }
    }
  }

  Widget _buildMedia(MediaAssetModel media) => Card(
    elevation: 0,
    child: ListTile(
      leading: Icon(
        media.kind == MediaKind.image
            ? Icons.image_outlined
            : media.kind == MediaKind.video
            ? Icons.play_circle_outline
            : Icons.description_outlined,
      ),
      title: Text(media.fileName, maxLines: 1, overflow: TextOverflow.ellipsis),
      subtitle: Text(_formatBytes(media.size)),
      trailing: const Icon(Icons.open_in_new, size: 18),
      onTap: () => _openMedia(media),
    ),
  );

  Future<void> _openMedia(MediaAssetModel media) async {
    try {
      final url = await _mediaApi.access(widget.session, media.id);
      if (!mounted) return;
      if (media.kind == MediaKind.image) {
        await showDialog<void>(
          context: context,
          builder: (_) => Dialog.fullscreen(
            backgroundColor: Colors.black,
            child: Stack(
              children: [
                Center(child: InteractiveViewer(child: Image.network('$url'))),
                SafeArea(
                  child: IconButton(
                    color: Colors.white,
                    onPressed: () => Navigator.pop(context),
                    icon: const Icon(Icons.close),
                  ),
                ),
              ],
            ),
          ),
        );
      } else if (media.kind == MediaKind.video) {
        await showDialog<void>(
          context: context,
          builder: (_) => _VideoDialog(url: url),
        );
      } else if (!await launchUrl(url, mode: LaunchMode.externalApplication)) {
        throw StateError('系统中没有可处理该文件的应用。');
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('打开附件失败：$error')));
      }
    }
  }

  static String _formatBytes(int size) {
    if (size >= 1024 * 1024 * 1024) {
      return '${(size / (1024 * 1024 * 1024)).toStringAsFixed(1)} GB';
    }
    if (size >= 1024 * 1024) {
      return '${(size / (1024 * 1024)).toStringAsFixed(1)} MB';
    }
    if (size >= 1024) return '${(size / 1024).toStringAsFixed(1)} KB';
    return '$size B';
  }
}

class _VideoDialog extends StatefulWidget {
  const _VideoDialog({required this.url});
  final Uri url;

  @override
  State<_VideoDialog> createState() => _VideoDialogState();
}

class _VideoDialogState extends State<_VideoDialog> {
  late final VideoPlayerController _controller;
  late final Future<void> _ready;

  @override
  void initState() {
    super.initState();
    _controller = VideoPlayerController.networkUrl(widget.url);
    _ready = _controller.initialize().then((_) => _controller.play());
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Dialog(
    backgroundColor: Colors.black,
    child: FutureBuilder<void>(
      future: _ready,
      builder: (_, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const SizedBox(
            height: 240,
            child: Center(child: CircularProgressIndicator()),
          );
        }
        return Stack(
          alignment: Alignment.bottomCenter,
          children: [
            AspectRatio(
              aspectRatio: _controller.value.aspectRatio,
              child: VideoPlayer(_controller),
            ),
            VideoProgressIndicator(_controller, allowScrubbing: true),
            Center(
              child: IconButton.filled(
                onPressed: () => setState(() {
                  _controller.value.isPlaying
                      ? _controller.pause()
                      : _controller.play();
                }),
                icon: Icon(
                  _controller.value.isPlaying ? Icons.pause : Icons.play_arrow,
                ),
              ),
            ),
          ],
        );
      },
    ),
  );
}

class _DetailRow extends StatelessWidget {
  const _DetailRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 6),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 120,
          child: Text(
            label,
            style: TextStyle(
              fontSize: 12,
              color: context.traceColors.inkTertiary,
            ),
          ),
        ),
        Expanded(child: Text(value, style: const TextStyle(fontSize: 14))),
      ],
    ),
  );
}

class _ErrorView extends StatelessWidget {
  const _ErrorView({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 32),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            '无法加载',
            style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 10),
          Text(
            message,
            textAlign: TextAlign.center,
            style: TextStyle(
              color: context.traceColors.inkSecondary,
              fontSize: 13,
            ),
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: onRetry,
            style: FilledButton.styleFrom(
              shape: const RoundedRectangleBorder(),
              minimumSize: const Size(140, 44),
            ),
            child: const Text('重试'),
          ),
        ],
      ),
    ),
  );
}
