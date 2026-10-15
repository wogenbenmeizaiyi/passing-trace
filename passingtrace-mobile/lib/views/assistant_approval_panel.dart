import 'package:flutter/material.dart';

import '../events/ai_api.dart';
import '../theme/passingtrace_theme.dart';

class AssistantApprovalPanel extends StatelessWidget {
  const AssistantApprovalPanel({
    super.key,
    required this.approval,
    required this.remaining,
    required this.busy,
    required this.onDecision,
    required this.onOpen,
    this.error,
  });
  final AiApprovalModel approval;
  final int remaining;
  final bool busy;
  final String? error;
  final ValueChanged<String> onDecision;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final type = switch (approval.targetType) {
      'Storyline' => '故事线',
      'Plan' => '计划',
      'Subject' => '人物档案',
      'SubjectEntry' => '人物专属内容',
      'SubjectRelation' => '误关联',
      _ => '记录',
    };
    return Padding(
      padding: const EdgeInsets.fromLTRB(10, 0, 10, 8),
      child: Semantics(
        container: true,
        liveRegion: true,
        label: '删除授权',
        child: Container(
          constraints: BoxConstraints(
            maxHeight: MediaQuery.sizeOf(context).height * .4,
          ),
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            color: colors.surface,
            border: Border.all(color: colors.danger),
            borderRadius: BorderRadius.circular(14),
          ),
          child: SingleChildScrollView(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  '确认删除$type？',
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
                TextButton(
                  onPressed: busy ? null : onOpen,
                  child: Text(approval.title),
                ),
                Text(approval.description),
                if (remaining > 1) Text('另有 ${remaining - 1} 项等待逐一确认。'),
                if (error != null)
                  Text(error!, style: TextStyle(color: colors.danger)),
                const SizedBox(height: 8),
                Row(
                  mainAxisAlignment: MainAxisAlignment.end,
                  children: [
                    TextButton(
                      onPressed: busy ? null : () => onDecision('cancel'),
                      style: TextButton.styleFrom(
                        minimumSize: const Size(80, 48),
                      ),
                      child: const Text('取消'),
                    ),
                    const SizedBox(width: 12),
                    TextButton(
                      onPressed: busy ? null : () => onDecision('confirm'),
                      style: TextButton.styleFrom(
                        foregroundColor: colors.danger,
                        minimumSize: const Size(80, 48),
                      ),
                      child: Text(busy ? '正在处理…' : '确定'),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
