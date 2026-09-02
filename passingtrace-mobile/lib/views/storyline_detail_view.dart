import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/event_model.dart';
import '../events/events_api.dart';
import '../events/media_api.dart';
import '../storylines/storyline_api.dart';
import '../storylines/storyline_id.dart';
import '../storylines/storyline_model.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';
import 'event_detail_view.dart';

class StorylineDetailView extends StatefulWidget {
  const StorylineDetailView({
    super.key,
    required this.auth,
    required this.session,
    required this.storylineId,
    this.apiClient,
    this.eventApiClient,
    this.mediaApiClient,
  });

  final AuthService auth;
  final AuthSession session;
  final String storylineId;
  final StorylineApiClient? apiClient;
  final EventApiClient? eventApiClient;
  final MediaApiClient? mediaApiClient;

  @override
  State<StorylineDetailView> createState() => _StorylineDetailViewState();
}

class _StorylineDetailViewState extends State<StorylineDetailView> {
  late StorylineApiClient _api;
  late EventApiClient _eventApi;
  MediaApiClient? _mediaApi;
  bool _ownsApi = false;
  bool _loading = true;
  bool _changing = false;
  String? _error;
  StorylineDetailModel? _story;
  final Map<String, Future<Uri>> _imageUrls = {};

  @override
  void initState() {
    super.initState();
    _init();
  }

  Future<void> _init() async {
    if (widget.apiClient != null && widget.eventApiClient != null) {
      _api = widget.apiClient!;
      _eventApi = widget.eventApiClient!;
      _mediaApi = widget.mediaApiClient;
    } else {
      final base = await widget.auth.getEventsApiBaseUrl();
      _api = StorylineApiClient(auth: widget.auth, baseUrl: base);
      _eventApi = EventApiClient(auth: widget.auth, baseUrl: base);
      _mediaApi = MediaApiClient(auth: widget.auth, baseUrl: base);
      _ownsApi = true;
    }
    await _load();
  }

  @override
  void dispose() {
    if (_ownsApi) {
      _api.close();
      _eventApi.close();
      _mediaApi?.close();
    }
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final value = await _api.get(widget.session, widget.storylineId);
      if (mounted) {
        setState(() {
          _story = value;
          for (final node in value.nodes) {
            final mediaId = node.imageMediaAssetId;
            if (mediaId != null && _mediaApi != null) {
              _imageUrls.putIfAbsent(
                mediaId,
                () => _mediaApi!.access(widget.session, mediaId),
              );
            }
          }
        });
      }
    } catch (error) {
      if (mounted) setState(() => _error = '$error');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _change(Map<String, dynamic> body) async {
    final story = _story;
    if (story == null || _changing) return;
    setState(() => _changing = true);
    try {
      final result = await _api.change(
        widget.session,
        story.id,
        story.version,
        body,
        newStorylineKey(),
      );
      if (!mounted) return;
      setState(() => _story = result.storyline);
      final undo = result.undoRevision;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: const Text('故事线已更新'),
          action: undo == null
              ? null
              : SnackBarAction(label: '撤销', onPressed: () => _undo(undo)),
        ),
      );
    } on EventApiException catch (error) {
      if (error.status == 409) await _load();
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(error.message)));
      }
    } finally {
      if (mounted) setState(() => _changing = false);
    }
  }

  Future<void> _undo(int revision) async {
    final story = _story;
    if (story == null) return;
    try {
      final result = await _api.restore(
        widget.session,
        story.id,
        revision,
        story.version,
        newStorylineKey(),
      );
      if (mounted) setState(() => _story = result.storyline);
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('无法撤销：$error')));
      }
    }
  }

  Future<String?> _pickParent() async {
    final story = _story;
    if (story == null) return null;
    return showDialog<String?>(
      context: context,
      builder: (context) => SimpleDialog(
        title: const Text('放在哪里？'),
        children: [
          SimpleDialogOption(
            onPressed: () => Navigator.pop(context, ''),
            child: const ListTile(
              leading: TraceIcon(TraceGlyph.target),
              title: Text('作为新的起点'),
            ),
          ),
          for (final entry in story.outline.map((x) => story.node(x.nodeKey)))
            SimpleDialogOption(
              onPressed: () => Navigator.pop(context, entry.key),
              child: ListTile(
                leading: const TraceIcon(TraceGlyph.storyline),
                title: Text(
                  '接在「${entry.title}」之后',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ),
        ],
      ),
    );
  }

  Future<bool> _pickBranch() async =>
      await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('连接方式'),
          content: const Text('“继续”表示顺序发生；“分支”表示从该节点走出另一条路径。'),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('继续'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('创建分支'),
            ),
          ],
        ),
      ) ??
      false;

  Future<String?> _pickStage() async {
    final story = _story;
    if (story == null) return null;
    return showDialog<String?>(
      context: context,
      builder: (context) => SimpleDialog(
        title: const Text('选择阶段'),
        children: [
          SimpleDialogOption(
            onPressed: () => Navigator.pop(context, ''),
            child: const Text('未分组'),
          ),
          for (final stage in story.stages)
            SimpleDialogOption(
              onPressed: () => Navigator.pop(context, stage.key),
              child: Text(stage.title),
            ),
        ],
      ),
    );
  }

  Future<void> _addExisting() async {
    final page = await _eventApi.list(widget.session, limit: 50);
    if (!mounted) return;
    final existing = _story!.nodes.map((x) => x.eventId).toSet();
    final choices = page.items.where((x) => !existing.contains(x.id)).toList();
    final selected = await showModalBottomSheet<EventModel>(
      context: context,
      isScrollControlled: true,
      builder: (context) => SafeArea(
        child: DraggableScrollableSheet(
          expand: false,
          initialChildSize: .65,
          minChildSize: .4,
          maxChildSize: .9,
          builder: (context, controller) => ListView.builder(
            controller: controller,
            padding: const EdgeInsets.all(18),
            itemCount: choices.length + 1,
            itemBuilder: (context, index) {
              if (index == 0) {
                return Padding(
                  padding: const EdgeInsets.only(bottom: 14),
                  child: Text(
                    '添加已有记录',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                );
              }
              final item = choices[index - 1];
              return ListTile(
                minTileHeight: 58,
                leading: TraceIcon(
                  item.kind == EventKind.plan
                      ? TraceGlyph.calendar
                      : TraceGlyph.journal,
                ),
                title: Text(item.title ?? '无标题记录'),
                subtitle: Text(
                  item.rawContent ?? '没有正文',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                onTap: () => Navigator.pop(context, item),
              );
            },
          ),
        ),
      ),
    );
    if (selected == null || !mounted) return;
    final parent = await _pickParent();
    if (parent == null || !mounted) return;
    final stage = await _pickStage();
    if (stage == null || !mounted) return;
    final branch = parent.isEmpty ? false : await _pickBranch();
    await _change({
      'operation': 'add-existing-event',
      'nodeKey': newStorylineKey(),
      'eventId': selected.id,
      'sourceRevision': selected.sourceRevision,
      'stageKey': stage.isEmpty ? null : stage,
      'parentNodeKey': parent.isEmpty ? null : parent,
      'createBranch': branch,
    });
  }

  Future<void> _addPlan() async {
    final title = TextEditingController();
    final content = TextEditingController();
    DateTime? planned;
    final accepted = await showDialog<bool>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setLocal) => AlertDialog(
          title: const Text('添加轻量计划'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: title,
                decoration: const InputDecoration(labelText: '标题'),
              ),
              const SizedBox(height: 10),
              TraceRowButton(
                glyph: TraceGlyph.calendar,
                title: planned == null
                    ? '预计时间（可选）'
                    : '${planned!.month}月${planned!.day}日',
                subtitle: '保存后可进入完整计划补充',
                onTap: () async {
                  final date = await showDatePicker(
                    context: context,
                    initialDate: DateTime.now(),
                    firstDate: DateTime.now().subtract(
                      const Duration(days: 3650),
                    ),
                    lastDate: DateTime.now().add(const Duration(days: 3650)),
                  );
                  if (date != null) setLocal(() => planned = date);
                },
              ),
              const SizedBox(height: 10),
              TextField(
                controller: content,
                maxLines: 3,
                decoration: const InputDecoration(labelText: '简短说明（可选）'),
              ),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('继续'),
            ),
          ],
        ),
      ),
    );
    if (accepted != true || title.text.trim().isEmpty || !mounted) {
      title.dispose();
      content.dispose();
      return;
    }
    final parent = await _pickParent();
    if (parent == null || !mounted) return;
    final stage = await _pickStage();
    if (stage == null || !mounted) return;
    final branch = parent.isEmpty ? false : await _pickBranch();
    await _change({
      'operation': 'add-plan',
      'nodeKey': newStorylineKey(),
      'newPlan': {
        'title': title.text.trim(),
        'plannedAt': planned?.toUtc().toIso8601String(),
        'rawContent': content.text.trim().isEmpty ? null : content.text.trim(),
        'timezone': DateTime.now().timeZoneName,
      },
      'stageKey': stage.isEmpty ? null : stage,
      'parentNodeKey': parent.isEmpty ? null : parent,
      'createBranch': branch,
    });
    title.dispose();
    content.dispose();
  }

  Future<void> _showAdd() async {
    final choice = await showModalBottomSheet<int>(
      context: context,
      builder: (context) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(18, 16, 18, 24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('快捷补充', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 14),
              TraceRowButton(
                glyph: TraceGlyph.journal,
                title: '添加已有记录或计划',
                subtitle: '从自己的记录库中选择',
                onTap: () => Navigator.pop(context, 1),
              ),
              const SizedBox(height: 10),
              TraceRowButton(
                glyph: TraceGlyph.calendar,
                title: '直接创建轻量计划',
                subtitle: '保存后也会出现在未来安排中',
                onTap: () => Navigator.pop(context, 2),
              ),
            ],
          ),
        ),
      ),
    );
    if (choice == 1) await _addExisting();
    if (choice == 2) await _addPlan();
  }

  Future<void> _nodeActions(
    StorylineNodeModel node,
    StorylineOutlineNode outline,
  ) async {
    final action = await showModalBottomSheet<String>(
      context: context,
      builder: (context) => SafeArea(
        child: Wrap(
          children: [
            if (node.revisionState == 'updated')
              ListTile(
                minTileHeight: 54,
                leading: const TraceIcon(TraceGlyph.refresh),
                title: const Text('同步最新内容'),
                onTap: () => Navigator.pop(context, 'sync'),
              ),
            ListTile(
              minTileHeight: 54,
              leading: const TraceIcon(TraceGlyph.storyline),
              title: const Text('移动到其他阶段'),
              onTap: () => Navigator.pop(context, 'stage'),
            ),
            ListTile(
              minTileHeight: 54,
              leading: TraceIcon(
                TraceGlyph.delete,
                color: context.traceColors.danger,
              ),
              title: Text(
                '从故事线移除',
                style: TextStyle(color: context.traceColors.danger),
              ),
              onTap: () => Navigator.pop(context, 'remove'),
            ),
          ],
        ),
      ),
    );
    if (action == 'sync') {
      await _change({'operation': 'sync-node', 'nodeKey': node.key});
    }
    if (action == 'stage') {
      final stage = await _pickStage();
      if (stage != null) {
        await _change({
          'operation': 'move-node-to-stage',
          'nodeKey': node.key,
          'stageKey': stage.isEmpty ? null : stage,
        });
      }
    }
    if (action == 'remove') {
      if (outline.outgoingCount == 0) {
        await _change({'operation': 'remove-node', 'nodeKey': node.key});
      } else if (outline.incomingCount == 1 && outline.outgoingCount == 1) {
        await _change({
          'operation': 'remove-node-and-reconnect',
          'nodeKey': node.key,
        });
      } else if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('这个节点位于复杂分支或汇合中，请使用网页整理。')),
        );
      }
    }
  }

  void _openNode(StorylineNodeModel node) {
    Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => EventDetailView(
          auth: widget.auth,
          session: widget.session,
          eventId: node.eventId,
        ),
      ),
    );
  }

  String _date(DateTime? value) =>
      value == null ? '时间未定' : '${value.year}年${value.month}月${value.day}日';

  @override
  Widget build(BuildContext context) {
    final story = _story;
    return Scaffold(
      appBar: TraceAppBar(
        title: '故事线详情',
        leading: TraceIconButton(
          glyph: TraceGlyph.chevronLeft,
          tooltip: '返回',
          onPressed: () => Navigator.pop(context),
        ),
        trailing: TraceIconButton(
          glyph: TraceGlyph.add,
          tooltip: '快捷补充',
          onPressed: _changing ? null : _showAdd,
        ),
      ),
      body: _body(story),
      floatingActionButton: story == null
          ? null
          : FloatingActionButton.extended(
              onPressed: _changing ? null : _showAdd,
              icon: _changing
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const TraceIcon(TraceGlyph.add),
              label: const Text('补充节点'),
            ),
    );
  }

  Widget _body(StorylineDetailModel? story) {
    if (_loading) return const Center(child: CircularProgressIndicator());
    if (_error != null) {
      return Center(
        child: Padding(padding: const EdgeInsets.all(24), child: Text(_error!)),
      );
    }
    if (story == null) return const SizedBox.shrink();
    final allStages = [
      ...story.stages,
      const StorylineStageModel(key: '', title: '未分组', semanticOrder: 999),
    ];
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 26, 20, 100),
        children: [
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: [
              TraceTag(label: story.categoryLabel, category: true),
              TraceTag(label: story.status.label),
              if (story.layoutState == 2) const TraceTag(label: '网页待整理'),
            ],
          ),
          const SizedBox(height: 14),
          Text(
            story.title,
            style: const TextStyle(
              fontSize: 30,
              fontWeight: FontWeight.w800,
              letterSpacing: -1,
            ),
          ),
          if (story.description != null) ...[
            const SizedBox(height: 8),
            Text(
              story.description!,
              style: TextStyle(
                color: context.traceColors.inkSecondary,
                height: 1.6,
              ),
            ),
          ],
          const SizedBox(height: 18),
          Text(
            '${_date(story.rangeStart)} — ${_date(story.rangeEnd)} · ${story.nodes.length} 个节点',
            style: TextStyle(
              fontSize: 11,
              color: context.traceColors.inkTertiary,
            ),
          ),
          const SizedBox(height: 30),
          for (final stage in allStages)
            if (story.outline.any((x) => (x.stageKey ?? '') == stage.key))
              _StageTimeline(
                stage: stage,
                entries:
                    story.outline
                        .where((x) => (x.stageKey ?? '') == stage.key)
                        .toList()
                      ..sort(
                        (a, b) =>
                            a.topologicalOrder.compareTo(b.topologicalOrder),
                      ),
                story: story,
                imageUrls: _imageUrls,
                onOpen: _openNode,
                onActions: _nodeActions,
              ),
        ],
      ),
    );
  }
}

class _StageTimeline extends StatelessWidget {
  const _StageTimeline({
    required this.stage,
    required this.entries,
    required this.story,
    required this.imageUrls,
    required this.onOpen,
    required this.onActions,
  });
  final StorylineStageModel stage;
  final List<StorylineOutlineNode> entries;
  final StorylineDetailModel story;
  final Map<String, Future<Uri>> imageUrls;
  final ValueChanged<StorylineNodeModel> onOpen;
  final Future<void> Function(StorylineNodeModel, StorylineOutlineNode)
  onActions;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 28),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Text(
              stage.title,
              style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800),
            ),
            const SizedBox(width: 8),
            Text(
              '${entries.length} 个节点',
              style: TextStyle(
                fontSize: 10,
                color: context.traceColors.inkTertiary,
              ),
            ),
          ],
        ),
        const SizedBox(height: 12),
        for (final outline in entries)
          _TimelineNode(
            node: story.node(outline.nodeKey),
            outline: outline,
            imageUrl: nodeImage(outline),
            onOpen: onOpen,
            onActions: onActions,
          ),
      ],
    ),
  );

  Future<Uri>? nodeImage(StorylineOutlineNode outline) {
    final mediaId = story.node(outline.nodeKey).imageMediaAssetId;
    return mediaId == null ? null : imageUrls[mediaId];
  }
}

class _TimelineNode extends StatelessWidget {
  const _TimelineNode({
    required this.node,
    required this.outline,
    required this.imageUrl,
    required this.onOpen,
    required this.onActions,
  });
  final StorylineNodeModel node;
  final StorylineOutlineNode outline;
  final Future<Uri>? imageUrl;
  final ValueChanged<StorylineNodeModel> onOpen;
  final Future<void> Function(StorylineNodeModel, StorylineOutlineNode)
  onActions;

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.only(
      left: (outline.depth.clamp(0, 3) * 14).toDouble(),
      bottom: 10,
    ),
    child: IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            width: 24,
            child: Column(
              children: [
                Container(
                  width: 12,
                  height: 12,
                  margin: const EdgeInsets.only(top: 22),
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: node.emphasis == 2
                        ? context.traceColors.accent
                        : context.traceColors.primary,
                    border: Border.all(
                      color: context.traceColors.surface,
                      width: 2,
                    ),
                  ),
                ),
                Expanded(
                  child: Container(
                    width: 1,
                    color: context.traceColors.lineStrong,
                  ),
                ),
              ],
            ),
          ),
          Expanded(
            child: Material(
              color: context.traceColors.surface,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(14),
                side: BorderSide(
                  color: node.emphasis == 2
                      ? context.traceColors.accent
                      : context.traceColors.line,
                ),
              ),
              clipBehavior: Clip.antiAlias,
              child: InkWell(
                onTap: () => onOpen(node),
                onLongPress: () => onActions(node, outline),
                child: ConstrainedBox(
                  constraints: const BoxConstraints(minHeight: 118),
                  child: Padding(
                    padding: const EdgeInsets.all(14),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        if (imageUrl != null) ...[
                          FutureBuilder<Uri>(
                            future: imageUrl,
                            builder: (context, snapshot) {
                              if (!snapshot.hasData) {
                                return Container(
                                  height: 128,
                                  color: context.traceColors.surfaceSoft,
                                  alignment: Alignment.center,
                                  child: snapshot.hasError
                                      ? TraceIcon(
                                          TraceGlyph.image,
                                          color:
                                              context.traceColors.inkTertiary,
                                        )
                                      : const CircularProgressIndicator(
                                          strokeWidth: 2,
                                        ),
                                );
                              }
                              return SizedBox(
                                height: 150,
                                width: double.infinity,
                                child: Image.network(
                                  snapshot.data.toString(),
                                  fit: BoxFit.cover,
                                  errorBuilder: (_, _, _) => Container(
                                    color: context.traceColors.surfaceSoft,
                                    alignment: Alignment.center,
                                    child: TraceIcon(
                                      TraceGlyph.image,
                                      color: context.traceColors.inkTertiary,
                                    ),
                                  ),
                                ),
                              );
                            },
                          ),
                          const SizedBox(height: 12),
                        ],
                        Wrap(
                          spacing: 5,
                          runSpacing: 4,
                          children: [
                            if (outline.startsBranch)
                              const TraceTag(label: '分支'),
                            if (outline.isMerge)
                              TraceTag(
                                label: '来自 ${outline.incomingCount} 条路径',
                              ),
                            if (node.revisionState == 'updated')
                              const TraceTag(label: '内容已更新', ai: true),
                            TraceTag(label: node.status.label),
                          ],
                        ),
                        const SizedBox(height: 8),
                        Text(
                          node.title,
                          style: const TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        if (node.rawContent != null) ...[
                          const SizedBox(height: 5),
                          Text(
                            node.rawContent!,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 12,
                              color: context.traceColors.inkSecondary,
                              height: 1.45,
                            ),
                          ),
                        ],
                        const SizedBox(height: 10),
                        Row(
                          children: [
                            if (node.place != null) ...[
                              TraceIcon(
                                TraceGlyph.mapPin,
                                size: 14,
                                color: context.traceColors.inkTertiary,
                              ),
                              const SizedBox(width: 4),
                              Expanded(
                                child: Text(
                                  node.place!,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: TextStyle(
                                    fontSize: 10,
                                    color: context.traceColors.inkTertiary,
                                  ),
                                ),
                              ),
                            ] else
                              const Spacer(),
                            SizedBox.square(
                              dimension: 48,
                              child: TraceIconButton(
                                glyph: TraceGlyph.menu,
                                tooltip: '节点操作',
                                onPressed: () => onActions(node, outline),
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    ),
  );
}
