// 详情页：拉取并展示 Event 完整字段，编辑 / 删除入口。

import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/event_datetime.dart';
import '../events/event_model.dart';
import '../events/events_api.dart';
import '../main.dart';
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
    setState(() => _api = EventApiClient(auth: widget.auth, baseUrl: baseUrl));
    await _load();
  }

  @override
  void dispose() {
    _api.close();
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
            style: FilledButton.styleFrom(backgroundColor: PassingTraceApp.coral),
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
          style: TextStyle(fontFamily: 'serif', fontWeight: FontWeight.w600, fontSize: 19),
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
              fontFamily: 'serif',
              fontSize: 28,
              height: 1.3,
              fontWeight: FontWeight.w600,
            ),
          ),
          if (event.title?.isNotEmpty == true &&
              event.rawContent?.isNotEmpty == true) ...[
            const SizedBox(height: 16),
            Text(
              event.rawContent!,
              style: const TextStyle(
                fontSize: 15,
                height: 1.85,
              ),
            ),
          ],
          const SizedBox(height: 28),
          _DetailRow(label: event.kind == EventKind.plan ? '计划时间' : '发生时间', value: time),
          _DetailRow(label: '时区', value: event.timezone),
          if (event.completedAt != null)
            _DetailRow(label: '完成时间', value: formatLocal(event.completedAt)),
          const Divider(height: 36),
          Text(
            'SOURCE',
            style: TextStyle(
              fontSize: 11,
              letterSpacing: 1.5,
              fontWeight: FontWeight.w800,
              color: PassingTraceApp.ink.withValues(alpha: 0.5),
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
              color: PassingTraceApp.ink.withValues(alpha: 0.5),
            ),
          ),
        ),
        Expanded(
          child: Text(
            value,
            style: const TextStyle(
              fontSize: 14,
              fontFamily: 'serif',
            ),
          ),
        ),
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
            style: const TextStyle(
              fontFamily: 'serif',
              fontSize: 22,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 10),
          Text(
            message,
            textAlign: TextAlign.center,
            style: TextStyle(
              color: PassingTraceApp.ink.withValues(alpha: 0.6),
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
