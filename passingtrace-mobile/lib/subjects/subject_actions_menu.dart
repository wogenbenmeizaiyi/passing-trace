import 'package:flutter/material.dart';

import '../theme/passingtrace_theme.dart';
import 'subject_model.dart';

enum SubjectAction { end, resume, correct, delete }

class SubjectActionsMenu extends StatelessWidget {
  const SubjectActionsMenu({
    super.key,
    required this.subject,
    required this.busy,
    required this.onSelected,
  });
  final SubjectModel subject;
  final bool busy;
  final ValueChanged<SubjectAction> onSelected;

  @override
  Widget build(BuildContext context) {
    if (subject.isSelf) return const SizedBox.shrink();
    final colors = context.traceColors;
    PopupMenuItem<SubjectAction> item(
      SubjectAction action,
      String label,
      IconData icon, {
      bool danger = false,
    }) {
      final color = danger ? colors.danger : colors.ink;
      return PopupMenuItem(
        value: action,
        child: Row(
          children: [
            Icon(icon, size: 22, color: color),
            const SizedBox(width: 12),
            Expanded(
              child: Text(label, style: TextStyle(color: color)),
            ),
          ],
        ),
      );
    }

    return PopupMenuButton<SubjectAction>(
      key: const Key('subject-detail-more'),
      tooltip: '更多操作',
      enabled: !busy,
      icon: const Icon(Icons.more_vert),
      constraints: const BoxConstraints(minWidth: 220, maxWidth: 300),
      onSelected: onSelected,
      itemBuilder: (_) => [
        if (subject.state == 1) ...[
          if (subject.endReason != 'deceased')
            item(SubjectAction.resume, '恢复档案', Icons.restore),
          item(SubjectAction.correct, '纠正结束信息', Icons.edit_calendar_outlined),
          const PopupMenuDivider(),
        ],
        PopupMenuItem(
          enabled: false,
          height: 36,
          child: Text(
            '危险操作',
            style: TextStyle(fontSize: 12, color: colors.inkTertiary),
          ),
        ),
        if (subject.state == 0)
          item(
            SubjectAction.end,
            '结束档案',
            Icons.stop_circle_outlined,
            danger: true,
          ),
        item(
          SubjectAction.delete,
          '申请删除档案',
          Icons.delete_outline,
          danger: true,
        ),
      ],
    );
  }
}
