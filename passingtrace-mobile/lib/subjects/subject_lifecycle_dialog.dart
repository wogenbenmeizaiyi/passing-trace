import 'package:flutter/material.dart';

import '../events/event_datetime.dart';
import '../theme/passingtrace_theme.dart';
import 'subject_date_input.dart';
import 'subject_model.dart';

Future<Map<String, dynamic>?> showSubjectLifecycleDialog({
  required BuildContext context,
  required SubjectModel subject,
  required String operation,
  required List<SubjectEntry> plans,
  String? correctsId,
}) => showDialog<Map<String, dynamic>>(
  context: context,
  builder: (_) => _SubjectLifecycleDialog(
    subject: subject,
    operation: operation,
    plans: plans,
    correctsId: correctsId,
  ),
);

class _SubjectLifecycleDialog extends StatefulWidget {
  const _SubjectLifecycleDialog({
    required this.subject,
    required this.operation,
    required this.plans,
    this.correctsId,
  });

  final SubjectModel subject;
  final String operation;
  final List<SubjectEntry> plans;
  final String? correctsId;

  @override
  State<_SubjectLifecycleDialog> createState() =>
      _SubjectLifecycleDialogState();
}

class _SubjectLifecycleDialogState extends State<_SubjectLifecycleDialog> {
  final _form = GlobalKey<FormState>();
  final _selected = <String>{};
  final _note = TextEditingController();
  late final TextEditingController _date;
  late String _reason;
  bool _undo = false;

  bool get _ending => widget.operation == 'end';
  String get _title => switch (widget.operation) {
    'end' => '结束人物档案',
    'resume' => '恢复人物档案',
    _ => '纠正结束信息',
  };

  @override
  void initState() {
    super.initState();
    _reason = widget.subject.endReason ?? 'other';
    _date = TextEditingController(
      text: widget.operation == 'correct'
          ? toWallClockLocal(DateTime.tryParse(widget.subject.endedAt ?? ''))
          : toWallClockLocal(DateTime.now()),
    );
  }

  @override
  void dispose() {
    _date.dispose();
    _note.dispose();
    super.dispose();
  }

  void _submit() {
    if (!_form.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    Navigator.of(context).pop(<String, dynamic>{
      'operation': widget.operation,
      'effectiveAt': toIsoWithOffset(_date.text, defaultTimezone()),
      'reason': _reason,
      'note': _note.text,
      'undoEnd': _undo,
      if (widget.operation == 'correct' && widget.correctsId != null)
        'correctsId': widget.correctsId,
      'cancelPlans': widget.plans
          .where((plan) => _selected.contains(plan.id))
          .map((plan) => {'id': plan.id, 'version': plan.version})
          .toList(),
    });
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Dialog(
      key: const Key('subject-lifecycle-dialog'),
      backgroundColor: colors.surface,
      insetPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 24),
      constraints: const BoxConstraints(maxWidth: 560),
      clipBehavior: Clip.antiAlias,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 20, 20, 16),
            child: Semantics(
              namesRoute: true,
              header: true,
              child: Text(
                _title,
                style: Theme.of(context).textTheme.titleLarge
                    ?.copyWith(fontSize: 22, fontWeight: FontWeight.w700),
              ),
            ),
          ),
          Divider(height: 1, color: colors.line),
          Flexible(
            child: SingleChildScrollView(
              key: const Key('subject-lifecycle-scroll'),
              padding: const EdgeInsets.fromLTRB(20, 20, 20, 24),
              keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
              child: Form(
                key: _form,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(
                      key: const Key('subject-lifecycle-summary'),
                      padding: const EdgeInsets.all(14),
                      decoration: BoxDecoration(
                        color: colors.surfaceSoft,
                        borderRadius: BorderRadius.circular(12),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            widget.subject.name,
                            style: const TextStyle(fontWeight: FontWeight.w600),
                          ),
                          const SizedBox(height: 8),
                          Text(
                            switch (widget.operation) {
                              'end' => '资料与历史保留。结束后仍可补录、回忆或安排后续事项。',
                              'resume' => '恢复后继续记录，过去的结束信息仍保留；已取消的计划不会自动恢复。',
                              _ => '可以修正结束信息，或撤销误标。原有历史仍会保留。',
                            },
                            style: TextStyle(
                              color: colors.inkSecondary,
                              height: 1.5,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 24),
                    SubjectDateInput(
                      key: const Key('subject-lifecycle-date'),
                      controller: _date,
                      label: '实际生效时间',
                      requiredDate: true,
                    ),
                    if (widget.operation != 'resume') ...[
                      const SizedBox(height: 24),
                      DropdownButtonFormField<String>(
                        key: const Key('subject-lifecycle-reason'),
                        initialValue: _reason,
                        isExpanded: true,
                        menuMaxHeight: 320,
                        decoration: const InputDecoration(labelText: '原因'),
                        items: subjectReasons.entries
                            .map(
                              (reason) => DropdownMenuItem(
                                value: reason.key,
                                child: Text(reason.value),
                              ),
                            )
                            .toList(),
                        onChanged: (value) => _reason = value!,
                      ),
                    ],
                    const SizedBox(height: 24),
                    TextFormField(
                      key: const Key('subject-lifecycle-note'),
                      controller: _note,
                      decoration: const InputDecoration(
                        labelText: '说明（可选）',
                        hintText: '补充这次状态变化的情况',
                        alignLabelWithHint: true,
                      ),
                      minLines: 2,
                      maxLines: 4,
                      keyboardType: TextInputType.multiline,
                    ),
                    if (widget.operation == 'correct') ...[
                      const SizedBox(height: 24),
                      CheckboxListTile(
                        contentPadding: const EdgeInsets.symmetric(
                          horizontal: 8,
                        ),
                        title: const Text('纠正误标，撤销本次结束'),
                        value: _undo,
                        onChanged: (value) => setState(() => _undo = value!),
                      ),
                    ],
                    if (_ending) ...[
                      const SizedBox(height: 28),
                      Semantics(
                        header: true,
                        child: Text(
                          '待执行计划',
                          style: Theme.of(context).textTheme.titleMedium
                              ?.copyWith(fontWeight: FontWeight.w700),
                        ),
                      ),
                      const SizedBox(height: 10),
                      Text(
                        '所属计划默认保留。仅勾选需要取消的计划，其他引用档案也会看到取消结果。',
                        style: TextStyle(
                          color: colors.inkSecondary,
                          height: 1.5,
                        ),
                      ),
                      const SizedBox(height: 16),
                      if (widget.plans.isEmpty)
                        Text(
                          '没有待执行计划',
                          style: TextStyle(color: colors.inkTertiary),
                        ),
                      for (final plan in widget.plans) ...[
                        CheckboxListTile(
                          key: ValueKey('subject-lifecycle-plan-${plan.id}'),
                          tileColor: colors.surfaceSoft,
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(12),
                          ),
                          contentPadding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 4,
                          ),
                          title: Text(plan.title),
                          value: _selected.contains(plan.id),
                          onChanged: (value) => setState(() {
                            value == true
                                ? _selected.add(plan.id)
                                : _selected.remove(plan.id);
                          }),
                        ),
                        const SizedBox(height: 8),
                      ],
                    ],
                  ],
                ),
              ),
            ),
          ),
          Divider(height: 1, color: colors.line),
          Padding(
            key: const Key('subject-lifecycle-actions'),
            padding: const EdgeInsets.fromLTRB(20, 16, 20, 20),
            child: Row(
              children: [
                Expanded(
                  flex: 2,
                  child: OutlinedButton(
                    style: OutlinedButton.styleFrom(
                      minimumSize: const Size(0, 48),
                      padding: const EdgeInsets.symmetric(
                        horizontal: 12,
                        vertical: 12,
                      ),
                    ),
                    onPressed: () => Navigator.of(context).pop(),
                    child: const Text('取消'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  flex: 3,
                  child: FilledButton(
                    style: FilledButton.styleFrom(
                      minimumSize: const Size(0, 48),
                      padding: const EdgeInsets.symmetric(
                        horizontal: 12,
                        vertical: 12,
                      ),
                      backgroundColor: _ending ? colors.danger : colors.primary,
                      foregroundColor: _ending
                          ? Colors.white
                          : colors.onPrimary,
                    ),
                    onPressed: _submit,
                    child: Text(_ending ? '确认结束' : '保存'),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
