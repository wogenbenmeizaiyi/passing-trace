import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

import '../events/media_api.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_icons.dart';
import 'subject_graph_controller.dart';
import 'subject_graph_layout.dart';
import 'subject_model.dart';
import 'subject_graph_node.dart';

class SubjectGraphCanvas extends StatefulWidget {
  const SubjectGraphCanvas({
    super.key,
    required this.graph,
    required this.controller,
    required this.matches,
    required this.kept,
    required this.nickname,
    required this.onOpen,
    this.avatar,
    this.coverLoader,
  });

  final SubjectGraph graph;
  final SubjectGraphController controller;
  final Set<String> matches, kept;
  final String nickname;
  final Uint8List? avatar;
  final Future<MediaAccessTarget> Function(String)? coverLoader;
  final ValueChanged<String> onOpen;

  @override
  State<SubjectGraphCanvas> createState() => _SubjectGraphCanvasState();
}

class _SubjectGraphCanvasState extends State<SubjectGraphCanvas>
    with SingleTickerProviderStateMixin {
  String? _dragging;
  Offset? _pointer;
  late final Ticker _ticker;
  Duration? _lastTick;

  @override
  void initState() {
    super.initState();
    _ticker = createTicker(_tick);
    widget.controller.addListener(_startSimulation);
    _startSimulation();
  }

  @override
  void didUpdateWidget(covariant SubjectGraphCanvas oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.controller != widget.controller) {
      oldWidget.controller.removeListener(_startSimulation);
      widget.controller.addListener(_startSimulation);
      _ticker.stop();
      _lastTick = null;
      _startSimulation();
    }
  }

  void _startSimulation() {
    if (widget.controller.isSimulating && !_ticker.isActive) {
      _lastTick = null;
      _ticker.start();
    }
  }

  void _tick(Duration elapsed) {
    final seconds = _lastTick == null
        ? 1 / 60
        : (elapsed - _lastTick!).inMicroseconds /
              Duration.microsecondsPerSecond;
    _lastTick = elapsed;
    if (!widget.controller.advance(seconds.clamp(0.0, 1 / 30))) {
      _ticker.stop();
      _lastTick = null;
    }
  }

  @override
  void dispose() {
    widget.controller.removeListener(_startSimulation);
    _ticker.dispose();
    widget.controller.endDrag();
    super.dispose();
  }

  void _dragStart(String id, DragStartDetails details) => setState(() {
    _dragging = id;
    _pointer = details.globalPosition;
    widget.controller.beginDrag(id);
  });

  void _dragUpdate(String id, DragUpdateDetails details) {
    final previous = _pointer;
    _pointer = details.globalPosition;
    if (previous == null) return;
    // Global deltas stay stable as the node itself moves underneath the finger.
    final zoom = widget.controller.transform.value.getMaxScaleOnAxis();
    widget.controller.move(id, (details.globalPosition - previous) / zoom);
  }

  void _dragEnd() => setState(() {
    _dragging = null;
    _pointer = null;
    widget.controller.endDrag();
  });

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return LayoutBuilder(
      builder: (context, constraints) {
        widget.controller.viewportSize = constraints.biggest;
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted) widget.controller.centerIfNeeded();
        });
        return AnimatedBuilder(
          animation: widget.controller,
          builder: (context, _) {
            final radius = widget.controller.radius;
            final origin = Offset(radius, radius);
            final positions = widget.controller.positions;
            return Stack(
              children: [
                InteractiveViewer(
                  transformationController: widget.controller.transform,
                  panEnabled: _dragging == null,
                  constrained: false,
                  boundaryMargin: const EdgeInsets.all(500),
                  minScale: .1,
                  maxScale: 2,
                  child: SizedBox(
                    width: radius * 2,
                    height: radius * 2,
                    child: Stack(
                      children: [
                        Positioned.fill(
                          child: CustomPaint(
                            painter: _RelationPainter(
                              widget.graph.relations
                                  .where(
                                    (r) =>
                                        widget.kept.contains(r.from) &&
                                        widget.kept.contains(r.to),
                                  )
                                  .toList(),
                              Map.of(positions),
                              origin,
                              colors.primaryStrong,
                            ),
                          ),
                        ),
                        for (final s in widget.graph.nodes.where(
                          (s) => widget.kept.contains(s.id),
                        ))
                          Positioned(
                            left:
                                origin.dx +
                                positions[s.id]!.dx -
                                subjectNodeDiameter / 2,
                            top:
                                origin.dy +
                                positions[s.id]!.dy -
                                subjectNodeDiameter / 2,
                            width: subjectNodeDiameter,
                            height: subjectNodeDiameter,
                            child: GestureDetector(
                              key: ValueKey('subject-node-${s.id}'),
                              dragStartBehavior: DragStartBehavior.down,
                              onPanStart: (details) =>
                                  _dragStart(s.id, details),
                              onPanUpdate: (details) =>
                                  _dragUpdate(s.id, details),
                              onPanEnd: (_) => _dragEnd(),
                              onPanCancel: _dragEnd,
                              child: Opacity(
                                opacity: widget.matches.contains(s.id)
                                    ? 1
                                    : .45,
                                child: Material(
                                  color: colors.surface,
                                  clipBehavior: Clip.antiAlias,
                                  shape: CircleBorder(
                                    side: BorderSide(
                                      color: s.isSelf
                                          ? colors.primaryStrong
                                          : colors.lineStrong,
                                      width: s.isSelf ? 2 : 1,
                                    ),
                                  ),
                                  child: InkWell(
                                    onTap: () => widget.onOpen(s.id),
                                    customBorder: const CircleBorder(),
                                    child: SubjectGraphNode(
                                      subject: s,
                                      nickname: widget.nickname,
                                      accountAvatar: widget.avatar,
                                      cover: s.isSelf || s.coverMediaId == null
                                          ? null
                                          : widget.coverLoader?.call(
                                              s.coverMediaId!,
                                            ),
                                    ),
                                  ),
                                ),
                              ),
                            ),
                          ),
                      ],
                    ),
                  ),
                ),
                Positioned(
                  right: 20,
                  bottom: 80,
                  child: FloatingActionButton.small(
                    heroTag: 'subjects-center',
                    backgroundColor: colors.surface,
                    foregroundColor: colors.primaryStrong,
                    onPressed: widget.controller.center,
                    tooltip: '回到自己',
                    child: const TraceIcon(TraceGlyph.target),
                  ),
                ),
              ],
            );
          },
        );
      },
    );
  }
}

class _RelationPainter extends CustomPainter {
  _RelationPainter(this.relations, this.positions, this.origin, this.color);

  final List<SubjectRelation> relations;
  final Map<String, Offset> positions;
  final Offset origin;
  final Color color;

  void _arrow(Canvas canvas, Offset tip, Offset direction, Paint paint) {
    final angle = math.atan2(direction.dy, direction.dx);
    for (final offset in [-.5, .5]) {
      canvas.drawLine(
        tip,
        tip - Offset(math.cos(angle + offset), math.sin(angle + offset)) * 12,
        paint,
      );
    }
  }

  @override
  void paint(Canvas canvas, Size size) {
    for (final r in relations) {
      final fromCenter = positions[r.from]! + origin;
      final toCenter = positions[r.to]! + origin;
      final delta = toCenter - fromCenter;
      final distance = delta.distance;
      final edgeRadius = subjectNodeDiameter / 2 + 1;
      if (distance <= edgeRadius * 2) continue;
      final direction = delta / distance;
      final from = fromCenter + direction * edgeRadius;
      final to = toCenter - direction * edgeRadius;
      final paint = Paint()
        ..color = r.endedAt == null ? color : Colors.grey
        ..strokeWidth = 1.5;
      canvas.drawLine(from, to, paint);
      _arrow(canvas, to, direction, paint);
      if (!r.directed) _arrow(canvas, from, -direction, paint);
    }
  }

  @override
  bool shouldRepaint(covariant _RelationPainter oldDelegate) => true;
}
