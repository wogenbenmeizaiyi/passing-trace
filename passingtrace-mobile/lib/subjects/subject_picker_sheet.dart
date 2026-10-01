import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_icons.dart';
import 'subject_model.dart';

Future<List<String>?> showSubjectPickerSheet({
  required BuildContext context,
  required List<SubjectModel> subjects,
  required List<String> selectedIds,
}) => showModalBottomSheet<List<String>>(
  context: context,
  isScrollControlled: true,
  useSafeArea: true,
  showDragHandle: true,
  barrierLabel: '关闭人物选择',
  barrierColor: context.traceColors.ink.withValues(alpha: .34),
  builder: (context) => LayoutBuilder(
    builder: (context, constraints) {
      final inset = MediaQuery.viewInsetsOf(context).bottom;
      final availableHeight = math.max(0.0, constraints.maxHeight - inset);
      return Padding(
        padding: EdgeInsets.only(bottom: inset),
        child: SizedBox(
          height: math.min(680, availableHeight * .94),
          child: _SubjectPickerSheet(
            subjects: subjects,
            selectedIds: selectedIds,
          ),
        ),
      );
    },
  ),
);

class _SubjectPickerSheet extends StatefulWidget {
  const _SubjectPickerSheet({
    required this.subjects,
    required this.selectedIds,
  });

  final List<SubjectModel> subjects;
  final List<String> selectedIds;

  @override
  State<_SubjectPickerSheet> createState() => _SubjectPickerSheetState();
}

class _SubjectPickerSheetState extends State<_SubjectPickerSheet> {
  final _query = TextEditingController();
  late final Set<String> _selected = {...widget.selectedIds};
  String _kind = '';

  @override
  void dispose() {
    _query.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final query = _query.text.trim().toLowerCase();
    final matches = widget.subjects.where((subject) {
      final name = '${subject.name}${subject.isSelf ? ' 自己' : ''}';
      return (_kind.isEmpty || '${subject.kind}' == _kind) &&
          name.toLowerCase().contains(query);
    }).toList();
    return Material(
      key: const Key('subject-picker-sheet'),
      color: colors.surface,
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 0, 12, 0),
            child: Row(
              children: [
                Expanded(
                  child: Semantics(
                    header: true,
                    child: Text(
                      '选择人物',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                  ),
                ),
                PopupMenuButton<String>(
                  tooltip: '按类型筛选',
                  initialValue: _kind,
                  icon: TraceIcon(
                    TraceGlyph.filter,
                    color: _kind.isEmpty ? colors.inkSecondary : colors.primary,
                  ),
                  onSelected: (value) => setState(() => _kind = value),
                  itemBuilder: (_) => [
                    for (final (kind, name) in const [
                      ('', '全部'),
                      ('0', '人'),
                      ('1', '宠物'),
                      ('2', '物品'),
                    ])
                      CheckedPopupMenuItem(
                        value: kind,
                        checked: kind == _kind,
                        child: Text(name),
                      ),
                  ],
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 8, 20, 16),
            child: TextField(
              key: const Key('subject-picker-search'),
              controller: _query,
              decoration: InputDecoration(
                labelText: '搜索人物、宠物或物品',
                isDense: true,
                contentPadding: const EdgeInsets.symmetric(
                  horizontal: 16,
                  vertical: 14,
                ),
                prefixIcon: const Padding(
                  padding: EdgeInsets.all(12),
                  child: TraceIcon(TraceGlyph.search),
                ),
                suffixIcon: query.isEmpty
                    ? null
                    : IconButton(
                        tooltip: '清空搜索',
                        icon: const TraceIcon(TraceGlyph.close),
                        onPressed: () => setState(_query.clear),
                      ),
              ),
              textInputAction: TextInputAction.search,
              onChanged: (_) => setState(() {}),
              onSubmitted: (_) => FocusScope.of(context).unfocus(),
            ),
          ),
          Divider(height: 1, color: colors.line),
          Expanded(
            child: matches.isEmpty
                ? Center(
                    child: Padding(
                      padding: const EdgeInsets.all(20),
                      child: Text(
                        widget.subjects.isEmpty ? '暂无可选人物' : '没有找到匹配的人物',
                      ),
                    ),
                  )
                : ListView.builder(
                    keyboardDismissBehavior:
                        ScrollViewKeyboardDismissBehavior.onDrag,
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    itemCount: matches.length,
                    itemBuilder: (_, index) {
                      final subject = matches[index];
                      return CheckboxListTile(
                        key: ValueKey('subject-option-${subject.id}'),
                        contentPadding: const EdgeInsets.symmetric(
                          horizontal: 20,
                        ),
                        title: Text(
                          subject.isSelf ? '自己' : subject.name,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                        ),
                        subtitle: Text(['人', '宠物', '物品'][subject.kind]),
                        value: _selected.contains(subject.id),
                        onChanged: (checked) => setState(() {
                          if (checked == true) {
                            _selected.add(subject.id);
                          } else {
                            _selected.remove(subject.id);
                          }
                        }),
                      );
                    },
                  ),
          ),
          Divider(height: 1, color: colors.line),
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
                      key: const Key('subject-picker-confirm'),
                      onPressed: () =>
                          Navigator.of(context).pop(_selected.toList()),
                      child: Text('确定（${_selected.length}）'),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
