import 'package:flutter/material.dart';

import '../events/event_model.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';

class EventKindBadge extends StatelessWidget {
  const EventKindBadge(this.kind, {super.key});

  final EventKind kind;

  @override
  Widget build(BuildContext context) =>
      TraceTag(label: kind.label, category: true);
}

class EventStatusBadge extends StatelessWidget {
  const EventStatusBadge(this.status, {super.key});

  final EventStatus status;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final color = switch (status) {
      EventStatus.planned => colors.accent,
      EventStatus.completed => colors.success,
      EventStatus.cancelled => colors.inkTertiary,
    };
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        border: Border.all(color: color),
        borderRadius: BorderRadius.circular(99),
      ),
      child: Text(
        status.label,
        style: TextStyle(
          color: color,
          fontSize: 10,
          fontWeight: FontWeight.w700,
        ),
      ),
    );
  }
}

class EventCard extends StatelessWidget {
  const EventCard({super.key, required this.event, required this.onTap});

  final EventModel event;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final title = event.title?.trim().isNotEmpty == true
        ? event.title!.trim()
        : event.rawContent?.trim().isNotEmpty == true
        ? event.rawContent!.trim()
        : '未命名记录';
    final summary = event.rawContent?.trim() ?? '';
    final category = event.effectiveClassification.primaryCategory;
    final tags = event.effectiveClassification.tags.take(2).toList();
    final location = event.locations.firstOrNull;

    return Material(
      color: colors.surface,
      borderRadius: BorderRadius.circular(18),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Ink(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            border: Border.all(color: colors.line),
            borderRadius: BorderRadius.circular(18),
            boxShadow: [
              BoxShadow(
                color: colors.ink.withValues(alpha: 0.05),
                blurRadius: 18,
                offset: const Offset(0, 6),
              ),
            ],
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(
                    _timeLabel(event),
                    style: TextStyle(color: colors.inkTertiary, fontSize: 11),
                  ),
                  const Spacer(),
                  Text(
                    _metaLabel(event),
                    style: TextStyle(color: colors.inkTertiary, fontSize: 11),
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
                  fontSize: 16,
                  height: 1.4,
                  fontWeight: FontWeight.w700,
                  letterSpacing: -0.15,
                ),
              ),
              if (summary.isNotEmpty && summary != title) ...[
                const SizedBox(height: 7),
                Text(
                  summary,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    color: colors.inkSecondary,
                    fontSize: 13,
                    height: 1.6,
                  ),
                ),
              ],
              const SizedBox(height: 13),
              Row(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Expanded(
                    child: Wrap(
                      spacing: 6,
                      runSpacing: 6,
                      children: [
                        TraceTag(
                          label: category?.displayName ?? event.kind.label,
                          category: true,
                        ),
                        ...tags.map(
                          (tag) =>
                              TraceTag(label: tag.displayName, ai: tag.isAi),
                        ),
                      ],
                    ),
                  ),
                  if (location != null) ...[
                    const SizedBox(width: 8),
                    Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        TraceIcon(
                          TraceGlyph.mapPin,
                          size: 15,
                          color: colors.inkTertiary,
                        ),
                        const SizedBox(width: 3),
                        ConstrainedBox(
                          constraints: const BoxConstraints(maxWidth: 86),
                          child: Text(
                            location.name,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              color: colors.inkTertiary,
                              fontSize: 11,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ],
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  static String _timeLabel(EventModel event) {
    final value = event.kind == EventKind.plan
        ? event.plannedAt
        : event.happenedAt;
    final local = (value ?? event.createdAt).toLocal();
    String two(int number) => number.toString().padLeft(2, '0');
    return '${two(local.hour)}:${two(local.minute)}';
  }

  static String _metaLabel(EventModel event) {
    if (event.media.isNotEmpty) return '${event.media.length} 个附件';
    return event.status.label;
  }
}
