// Event 相关的视觉辅助：徽章、卡片、状态色。
// 复用现有「纸·墨·朱砂·青」配色，不引入新主题。

import 'package:flutter/material.dart';

import '../events/event_model.dart';
import '../main.dart';

class EventKindBadge extends StatelessWidget {
  const EventKindBadge(this.kind, {super.key});

  final EventKind kind;

  @override
  Widget build(BuildContext context) {
    final color = PassingTraceApp.coral;
    return _PillBadge(text: kind.label, color: color);
  }
}

class EventStatusBadge extends StatelessWidget {
  const EventStatusBadge(this.status, {super.key});

  final EventStatus status;

  @override
  Widget build(BuildContext context) {
    final Color color;
    switch (status) {
      case EventStatus.planned:
        color = PassingTraceApp.sage;
        break;
      case EventStatus.completed:
        color = const Color(0xff2e6a4a);
        break;
      case EventStatus.cancelled:
        color = PassingTraceApp.ink.withValues(alpha: 0.4);
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
  const EventCard({
    super.key,
    required this.event,
    required this.onTap,
  });

  final EventModel event;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final title = event.title?.isNotEmpty == true
        ? event.title!
        : (event.rawContent?.isNotEmpty == true ? event.rawContent! : '（无标题）');
    final summary = event.rawContent?.isNotEmpty == true ? event.rawContent! : '';
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
                          color: PassingTraceApp.ink.withValues(alpha: 0.5),
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
                    style: const TextStyle(
                      color: PassingTraceApp.ink,
                      fontFamily: 'serif',
                      fontSize: 17,
                      fontWeight: FontWeight.w600,
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
                        color: PassingTraceApp.ink.withValues(alpha: 0.55),
                        fontSize: 12,
                        height: 1.6,
                      ),
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
