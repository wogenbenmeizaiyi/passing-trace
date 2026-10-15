import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../theme/passingtrace_theme.dart';
import 'subject_date_input.dart';

class SubjectTimelineFilter {
  const SubjectTimelineFilter({
    this.groupBy = 'month',
    this.kind = '',
    this.state = '',
    this.from = '',
    this.to = '',
  });
  final String groupBy, kind, state, from, to;
  int get activeCount =>
      (kind.isEmpty ? 0 : 1) +
      (state.isEmpty ? 0 : 1) +
      (from.isEmpty && to.isEmpty ? 0 : 1);
  String get summary => [
    groupBy == 'day' ? '按日' : '按月',
    kind == 'Plan'
        ? '计划'
        : kind == 'Record'
        ? '记录'
        : '全部内容',
    {'Planned': '待执行', 'Completed': '已完成', 'Cancelled': '已取消'}[state] ?? '全部状态',
    if (from.isNotEmpty || to.isNotEmpty) '已限定日期',
  ].join(' · ');
}

Future<SubjectTimelineFilter?> showSubjectTimelineFilterSheet({
  required BuildContext context,
  required SubjectTimelineFilter selection,
}) => showModalBottomSheet<SubjectTimelineFilter>(
  context: context,
  isScrollControlled: true,
  useSafeArea: true,
  showDragHandle: true,
  barrierLabel: '关闭时间轴筛选',
  builder: (context) => Padding(
    padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
    child: SizedBox(
      height: math.min(
        (MediaQuery.sizeOf(context).height -
                MediaQuery.viewInsetsOf(context).bottom) *
            .9,
        720,
      ),
      child: _TimelineFilterSheet(selection: selection),
    ),
  ),
);

class _TimelineFilterSheet extends StatefulWidget {
  const _TimelineFilterSheet({required this.selection});
  final SubjectTimelineFilter selection;
  @override
  State<_TimelineFilterSheet> createState() => _TimelineFilterSheetState();
}

class _TimelineFilterSheetState extends State<_TimelineFilterSheet> {
  final _form = GlobalKey<FormState>();
  late final TextEditingController _from, _to;
  late String _groupBy, _kind, _state;
  String? _error;
  @override
  void initState() {
    super.initState();
    _from = TextEditingController(text: widget.selection.from);
    _to = TextEditingController(text: widget.selection.to);
    _groupBy = widget.selection.groupBy;
    _kind = widget.selection.kind;
    _state = widget.selection.state;
  }

  @override
  void dispose() {
    _from.dispose();
    _to.dispose();
    super.dispose();
  }

  void _apply() {
    if (!_form.currentState!.validate()) return;
    final from = DateTime.tryParse(_from.text.replaceFirst(' ', 'T'));
    final to = DateTime.tryParse(_to.text.replaceFirst(' ', 'T'));
    if (from != null && to != null && from.isAfter(to)) {
      setState(() => _error = '截止时间不能早于起始时间');
      return;
    }
    Navigator.of(context).pop(
      SubjectTimelineFilter(
        groupBy: _groupBy,
        kind: _kind,
        state: _state,
        from: _from.text,
        to: _to.text,
      ),
    );
  }

  @override
  Widget build(BuildContext context) => Material(
    key: const Key('subject-timeline-filter-sheet'),
    color: context.traceColors.surface,
    child: Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 0, 12, 12),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  '筛选时间轴',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
              ),
              TextButton(
                onPressed: () {
                  _form.currentState?.reset();
                  setState(() {
                    _groupBy = 'month';
                    _kind = '';
                    _state = '';
                    _from.clear();
                    _to.clear();
                    _error = null;
                  });
                },
                child: const Text('重置'),
              ),
            ],
          ),
        ),
        const Divider(height: 1),
        Expanded(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(20),
            keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
            child: Form(
              key: _form,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _choices('时间分组', _groupBy, const [
                    ('month', '按月'),
                    ('day', '按日'),
                  ], (value) => setState(() => _groupBy = value)),
                  const SizedBox(height: 24),
                  _choices('内容类型', _kind, const [
                    ('', '全部内容'),
                    ('Record', '记录'),
                    ('Plan', '计划'),
                  ], (value) => setState(() => _kind = value)),
                  const SizedBox(height: 24),
                  _choices('状态', _state, const [
                    ('', '全部状态'),
                    ('Planned', '待执行'),
                    ('Completed', '已完成'),
                    ('Cancelled', '已取消'),
                  ], (value) => setState(() => _state = value)),
                  const SizedBox(height: 24),
                  const Text('日期范围（可留空）'),
                  const SizedBox(height: 16),
                  SubjectDateInput(controller: _from, label: '起始时间'),
                  const SizedBox(height: 20),
                  SubjectDateInput(controller: _to, label: '截止时间'),
                  if (_error != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 12),
                      child: Semantics(
                        liveRegion: true,
                        child: Text(
                          _error!,
                          style: TextStyle(color: context.traceColors.danger),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
        const Divider(height: 1),
        SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 12, 20, 14),
            child: Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    onPressed: () => Navigator.of(context).pop(),
                    child: const Text('取消'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: FilledButton(
                    onPressed: _apply,
                    child: const Text('应用筛选'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    ),
  );

  Widget _choices(
    String label,
    String value,
    List<(String, String)> choices,
    ValueChanged<String> onChanged,
  ) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(label),
      const SizedBox(height: 10),
      Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          for (final (key, name) in choices)
            ChoiceChip(
              label: Text(name),
              selected: value == key,
              showCheckmark: false,
              onSelected: (_) => onChanged(key),
            ),
        ],
      ),
    ],
  );
}
