import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../theme/passingtrace_theme.dart';

class SubjectFilterSelection {
  const SubjectFilterSelection({this.query = '', this.kind = ''});

  final String query, kind;

  int get activeCount => (query.isEmpty ? 0 : 1) + (kind.isEmpty ? 0 : 1);
}

Future<SubjectFilterSelection?> showSubjectFilterSheet({
  required BuildContext context,
  required SubjectFilterSelection selection,
}) => showModalBottomSheet<SubjectFilterSelection>(
  context: context,
  isScrollControlled: true,
  useSafeArea: true,
  showDragHandle: true,
  barrierLabel: '关闭人物筛选',
  barrierColor: context.traceColors.ink.withValues(alpha: .34),
  builder: (context) => Padding(
    padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
    child: SizedBox(
      height: math.min(MediaQuery.sizeOf(context).height * .74, 640),
      child: _SubjectFilterSheet(selection: selection),
    ),
  ),
);

class _SubjectFilterSheet extends StatefulWidget {
  const _SubjectFilterSheet({required this.selection});

  final SubjectFilterSelection selection;

  @override
  State<_SubjectFilterSheet> createState() => _SubjectFilterSheetState();
}

class _SubjectFilterSheetState extends State<_SubjectFilterSheet> {
  late final TextEditingController _query;
  late String _kind;

  @override
  void initState() {
    super.initState();
    _query = TextEditingController(text: widget.selection.query);
    _kind = widget.selection.kind;
  }

  @override
  void dispose() {
    _query.dispose();
    super.dispose();
  }

  void _reset() => setState(() {
    _query.clear();
    _kind = '';
  });

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Material(
      key: const Key('subject-filter-sheet'),
      color: colors.surface,
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 0, 12, 12),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    '筛选人物',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ),
                TextButton(onPressed: _reset, child: const Text('重置')),
              ],
            ),
          ),
          Divider(height: 1, color: colors.line),
          Expanded(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  TextFormField(
                    controller: _query,
                    decoration: const InputDecoration(
                      labelText: '搜索人物、宠物或物品',
                      prefixIcon: Icon(Icons.search),
                    ),
                    textInputAction: TextInputAction.done,
                    onFieldSubmitted: (_) => FocusScope.of(context).unfocus(),
                  ),
                  const SizedBox(height: 20),
                  const Text('人物类型'),
                  const SizedBox(height: 10),
                  Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: [
                      for (final (kind, label) in const [
                        ('', '全部'),
                        ('0', '人'),
                        ('1', '宠物'),
                        ('2', '物品'),
                      ])
                        ChoiceChip(
                          label: Text(label),
                          selected: _kind == kind,
                          showCheckmark: false,
                          onSelected: (_) => setState(() => _kind = kind),
                        ),
                    ],
                  ),
                ],
              ),
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
                      onPressed: () => Navigator.of(context).pop(
                        SubjectFilterSelection(
                          query: _query.text.trim(),
                          kind: _kind,
                        ),
                      ),
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
  }
}
