// Event 相关的视觉辅助：徽章、卡片、状态色。

import 'package:flutter/material.dart';

import '../events/event_model.dart';
import '../theme/passingtrace_theme.dart';

class EventKindBadge extends StatelessWidget {
  const EventKindBadge(this.kind, {super.key});

  final EventKind kind;

  @override
  Widget build(BuildContext context) {
    final color = context.traceColors.primary;
    return _PillBadge(text: kind.label, color: color);
  }
}

class EventStatusBadge extends StatelessWidget {
  const EventStatusBadge(this.status, {super.key});

  final EventStatus status;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final Color color;
    switch (status) {
      case EventStatus.planned:
        color = colors.accent;
        break;
      case EventStatus.completed:
        color = colors.success;
        break;
      case EventStatus.cancelled:
        color = colors.inkTertiary;
        break;
    }
    return _PillBadge(text: status.label, color: color);
  }
}

class _PillBadge extends StatelessWidget {
  const _PillBadge({required this.text, required this.color});

  final String text;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
    decoration: BoxDecoration(
      border: Border.all(color: color, width: 1),
      borderRadius: BorderRadius.circular(99),
    ),
    child: Text(
      text,
      style: TextStyle(
        color: color,
        fontSize: 10,
        fontWeight: FontWeight.w700,
        letterSpacing: 0.8,
      ),
    ),
  );
}

class EventCard extends StatelessWidget {
  const EventCard({super.key, required this.event, required this.onTap});

  final EventModel event;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final title = event.title?.isNotEmpty == true
        ? event.title!
        : (event.rawContent?.isNotEmpty == true ? event.rawContent! : '（无标题）');
    final summary = event.rawContent?.isNotEmpty == true
        ? event.rawContent!
        : '';
    final timeText = _timeLabel(event);
    return InkWell(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 18),
        decoration: BoxDecoration(
          border: Border(
            bottom: BorderSide(color: Theme.of(context).dividerColor),
          ),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      EventKindBadge(event.kind),
                      const SizedBox(width: 6),
                      EventStatusBadge(event.status),
                      const Spacer(),
                      Text(
                        timeText,
                        style: TextStyle(
                          color: colors.inkTertiary,
                          fontSize: 11,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 7),
                  Text(
                    title,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: colors.ink,
                      fontSize: 17,
                      fontWeight: FontWeight.w700,
                      height: 1.35,
                    ),
                  ),
                  if (summary.isNotEmpty && summary != title) ...[
                    const SizedBox(height: 4),
                    Text(
                      summary,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: colors.inkSecondary,
                        fontSize: 12,
                        height: 1.6,
                      ),
                    ),
                  ],
                  if (event.effectiveClassification.primaryCategory != null ||
                      event.effectiveClassification.tags.isNotEmpty) ...[
                    const SizedBox(height: 7),
                    Text(
                      [
                        if (event.effectiveClassification.primaryCategory
                            case final value?)
                          value.displayName,
                        ...event.effectiveClassification.tags
                            .take(2)
                            .map((x) => '${x.isAi ? '✦' : ''}${x.displayName}'),
                      ].join(' · '),
                      style: TextStyle(color: colors.primary, fontSize: 11),
                    ),
                  ],
                ],
              ),
            ),
            const Padding(
              padding: EdgeInsets.only(left: 8, top: 4),
              child: Icon(Icons.chevron_right, size: 20),
            ),
          ],
        ),
      ),
    );
  }

  static String _timeLabel(EventModel event) {
    if (event.kind == EventKind.plan) {
      return '计划：${_isoShort(event.plannedAt)}';
    }
    return '发生：${_isoShort(event.happenedAt)}';
  }

  static String _isoShort(DateTime? value) {
    if (value == null) return '—';
    final local = value.toLocal();
    String two(int n) => n.toString().padLeft(2, '0');
    return '${local.year}-${two(local.month)}-${two(local.day)} '
        '${two(local.hour)}:${two(local.minute)}';
  }
}
