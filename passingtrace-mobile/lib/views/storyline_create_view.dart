import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/event_model.dart';
import '../events/events_api.dart';
import '../storylines/storyline_api.dart';
import '../storylines/storyline_id.dart';
import '../storylines/storyline_model.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';

class StorylineCreateView extends StatefulWidget {
  const StorylineCreateView({
    super.key,
    required this.auth,
    required this.session,
  });

  final AuthService auth;
  final AuthSession session;

  @override
  State<StorylineCreateView> createState() => _StorylineCreateViewState();
}

class _StorylineCreateViewState extends State<StorylineCreateView> {
  final _title = TextEditingController();
  final _description = TextEditingController();
  final _planTitle = TextEditingController();
  final _planContent = TextEditingController();
  final _query = TextEditingController();
  late StorylineApiClient _storyApi;
  late EventApiClient _eventApi;
  bool _apiReady = false;
  int _step = 0;
  String _category = 'trip';
  bool _loading = true;
  bool _saving = false;
  bool _newPlan = false;
  String? _error;
  List<EventModel> _events = [];
  EventModel? _selected;
  DateTime? _plannedAt;

  @override
  void initState() {
    super.initState();
    _init();
  }

  Future<void> _init() async {
    final base = await widget.auth.getEventsApiBaseUrl();
    _storyApi = StorylineApiClient(auth: widget.auth, baseUrl: base);
    _eventApi = EventApiClient(auth: widget.auth, baseUrl: base);
    _apiReady = true;
    await _loadEvents();
  }

  @override
  void dispose() {
    _title.dispose();
    _description.dispose();
    _planTitle.dispose();
    _planContent.dispose();
    _query.dispose();
    if (_apiReady) {
      _storyApi.close();
      _eventApi.close();
    }
    super.dispose();
  }

  Future<void> _loadEvents() async {
    setState(() => _loading = true);
    try {
      final page = await _eventApi.list(
        widget.session,
        limit: 30,
        query: _query.text.trim().isEmpty ? null : _query.text.trim(),
      );
      if (mounted) setState(() => _events = page.items);
    } catch (error) {
      if (mounted) setState(() => _error = '$error');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _pickTime() async {
    final date = await showDatePicker(
      context: context,
      initialDate: _plannedAt ?? DateTime.now(),
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (date == null || !mounted) return;
    final time = await showTimePicker(
      context: context,
      initialTime: TimeOfDay.fromDateTime(_plannedAt ?? DateTime.now()),
    );
    if (time != null) {
      setState(
        () => _plannedAt = DateTime(
          date.year,
          date.month,
          date.day,
          time.hour,
          time.minute,
        ),
      );
    }
  }

  Future<void> _save() async {
    if (_title.text.trim().isEmpty) {
      setState(() => _error = '请填写故事线标题。');
      return;
    }
    if ((_newPlan && _planTitle.text.trim().isEmpty) ||
        (!_newPlan && _selected == null)) {
      setState(() => _error = '请选择第一条记录，或创建一个计划。');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    final stageKey = newStorylineKey();
    final nodeKey = newStorylineKey();
    final Map<String, Object?> node = _newPlan
        ? {
            'key': nodeKey,
            'nodeType': 'new-plan',
            'newPlan': {
              'title': _planTitle.text.trim(),
              'plannedAt': _plannedAt?.toUtc().toIso8601String(),
              'rawContent': _planContent.text.trim().isEmpty
                  ? null
                  : _planContent.text.trim(),
              'timezone': DateTime.now().timeZoneName,
            },
            'stageKey': stageKey,
            'semanticOrder': 0,
            'emphasis': 1,
          }
        : {
            'key': nodeKey,
            'nodeType': 'existing-event',
            'eventId': _selected!.id,
            'sourceRevision': _selected!.sourceRevision,
            'stageKey': stageKey,
            'semanticOrder': 0,
            'emphasis': 1,
          };
    try {
      final result = await _storyApi.create(widget.session, {
        'title': _title.text.trim(),
        'description': _description.text.trim().isEmpty
            ? null
            : _description.text.trim(),
        'categoryKey': _category,
        'status': StorylineStatus.ongoing.value,
        'coverMediaAssetId': null,
        'tags': <String>[],
        'stages': [
          {'key': stageKey, 'title': '开始', 'semanticOrder': 0},
        ],
        'nodes': [node],
        'edges': <Object>[],
        'webCanvasLayout': null,
      }, newStorylineKey());
      if (mounted) Navigator.pop(context, result.storyline.id);
    } catch (error) {
      if (mounted) setState(() => _error = '$error');
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: TraceAppBar(
      title: '新建故事线',
      leading: TraceIconButton(
        glyph: TraceGlyph.chevronLeft,
        tooltip: '返回',
        onPressed: () => Navigator.pop(context),
      ),
    ),
    bottomNavigationBar: TracePrimaryActionBar(
      label: _step == 0 ? '下一步' : '创建故事线',
      loading: _saving,
      onPressed: _saving
          ? null
          : () => _step == 0 ? setState(() => _step = 1) : _save(),
    ),
    body: ListView(
      padding: const EdgeInsets.fromLTRB(20, 24, 20, 120),
      children: [
        Row(
          children: [
            const _StepDot(active: true, label: '1', title: '基本信息'),
            Expanded(child: Divider(color: context.traceColors.lineStrong)),
            _StepDot(active: _step == 1, label: '2', title: '第一节点'),
          ],
        ),
        const SizedBox(height: 30),
        if (_error != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 16),
            child: Text(
              _error!,
              style: TextStyle(color: context.traceColors.danger),
            ),
          ),
        if (_step == 0) ..._buildMetadata() else ..._buildFirstNode(),
      ],
    ),
  );

  List<Widget> _buildMetadata() => [
    const TraceFieldLabel('故事线标题'),
    TextField(
      controller: _title,
      maxLength: 120,
      decoration: const InputDecoration(hintText: '例如：黄山旅行'),
    ),
    const SizedBox(height: 16),
    const TraceFieldLabel('主分类'),
    DropdownButtonFormField<String>(
      initialValue: _category,
      items: const [
        DropdownMenuItem(value: 'trip', child: Text('行程旅行')),
        DropdownMenuItem(value: 'activity', child: Text('活动纪实')),
        DropdownMenuItem(value: 'project', child: Text('项目过程')),
        DropdownMenuItem(value: 'challenge', child: Text('目标挑战')),
        DropdownMenuItem(value: 'lifecycle', child: Text('成长陪伴')),
        DropdownMenuItem(value: 'series', child: Text('主题系列')),
        DropdownMenuItem(value: 'life-period', child: Text('生活阶段')),
        DropdownMenuItem(value: 'other', child: Text('其他')),
      ],
      onChanged: (value) => setState(() => _category = value ?? 'other'),
    ),
    const SizedBox(height: 16),
    const TraceFieldLabel('说明（可选）'),
    TextField(
      controller: _description,
      maxLength: 2000,
      minLines: 4,
      maxLines: 7,
      decoration: const InputDecoration(hintText: '记下这段经历想要收集什么'),
    ),
  ];

  List<Widget> _buildFirstNode() => [
    Text('选择第一条记录或计划', style: Theme.of(context).textTheme.titleLarge),
    const SizedBox(height: 6),
    Text(
      '保存后可以继续从详情页快捷补充。',
      style: TextStyle(color: context.traceColors.inkTertiary, fontSize: 12),
    ),
    const SizedBox(height: 18),
    Row(
      children: [
        Expanded(
          child: _ModeButton(
            label: '已有记录',
            selected: !_newPlan,
            onTap: () => setState(() => _newPlan = false),
          ),
        ),
        Expanded(
          child: _ModeButton(
            label: '直接写计划',
            selected: _newPlan,
            onTap: () => setState(() => _newPlan = true),
          ),
        ),
      ],
    ),
    const SizedBox(height: 18),
    if (_newPlan) ...[
      const TraceFieldLabel('计划标题'),
      TextField(
        controller: _planTitle,
        decoration: const InputDecoration(hintText: '准备做什么'),
      ),
      const SizedBox(height: 12),
      TraceRowButton(
        glyph: TraceGlyph.calendar,
        title: _plannedAt == null
            ? '预计时间（可选）'
            : '${_plannedAt!.month}月${_plannedAt!.day}日 ${_plannedAt!.hour.toString().padLeft(2, '0')}:${_plannedAt!.minute.toString().padLeft(2, '0')}',
        subtitle: '不选也可以稍后补充',
        onTap: _pickTime,
      ),
      const SizedBox(height: 12),
      TextField(
        controller: _planContent,
        minLines: 3,
        maxLines: 5,
        decoration: const InputDecoration(hintText: '简短说明（可选）'),
      ),
    ] else ...[
      TextField(
        controller: _query,
        onSubmitted: (_) => _loadEvents(),
        decoration: InputDecoration(
          hintText: '搜索自己的记录',
          suffixIcon: IconButton(
            onPressed: _loadEvents,
            icon: const TraceIcon(TraceGlyph.search),
          ),
        ),
      ),
      const SizedBox(height: 12),
      if (_loading)
        const Center(child: CircularProgressIndicator())
      else
        for (final item in _events) _eventChoice(item),
    ],
  ];

  Widget _eventChoice(EventModel item) => Padding(
    padding: const EdgeInsets.only(bottom: 8),
    child: Material(
      color: _selected?.id == item.id
          ? context.traceColors.primarySoft
          : context.traceColors.surface,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: BorderSide(
          color: _selected?.id == item.id
              ? context.traceColors.primary
              : context.traceColors.line,
        ),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => setState(() => _selected = item),
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 72),
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Row(
              children: [
                TraceIcon(
                  item.kind == EventKind.plan
                      ? TraceGlyph.calendar
                      : TraceGlyph.journal,
                  color: context.traceColors.primaryStrong,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        item.title ?? '无标题记录',
                        style: const TextStyle(fontWeight: FontWeight.w700),
                      ),
                      const SizedBox(height: 3),
                      Text(
                        item.rawContent ?? '没有正文',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          fontSize: 11,
                          color: context.traceColors.inkTertiary,
                        ),
                      ),
                    ],
                  ),
                ),
                if (_selected?.id == item.id) const TraceIcon(TraceGlyph.check),
              ],
            ),
          ),
        ),
      ),
    ),
  );
}

class _StepDot extends StatelessWidget {
  const _StepDot({
    required this.active,
    required this.label,
    required this.title,
  });
  final bool active;
  final String label;
  final String title;

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Container(
        width: 34,
        height: 34,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          color: active
              ? context.traceColors.primary
              : context.traceColors.surfaceSoft,
          border: Border.all(
            color: active
                ? context.traceColors.primary
                : context.traceColors.lineStrong,
          ),
        ),
        child: Center(
          child: Text(
            label,
            style: TextStyle(
              color: active
                  ? context.traceColors.onPrimary
                  : context.traceColors.inkTertiary,
              fontWeight: FontWeight.w700,
            ),
          ),
        ),
      ),
      const SizedBox(height: 5),
      Text(
        title,
        style: TextStyle(
          fontSize: 10,
          color: active
              ? context.traceColors.ink
              : context.traceColors.inkTertiary,
        ),
      ),
    ],
  );
}

class _ModeButton extends StatelessWidget {
  const _ModeButton({
    required this.label,
    required this.selected,
    required this.onTap,
  });
  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
    color: Colors.transparent,
    child: InkWell(
      onTap: onTap,
      child: Container(
        height: 52,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          border: Border(
            bottom: BorderSide(
              width: 2,
              color: selected
                  ? context.traceColors.primary
                  : context.traceColors.line,
            ),
          ),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
            color: selected
                ? context.traceColors.primaryStrong
                : context.traceColors.inkTertiary,
          ),
        ),
      ),
    ),
  );
}
