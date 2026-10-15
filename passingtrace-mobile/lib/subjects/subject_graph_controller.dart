import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'subject_graph_layout.dart';
import 'subject_graph_simulation.dart';
import 'subject_model.dart';

class SubjectGraphSnapshot {
  SubjectGraphSnapshot(this.positions, this.transform, this.radius);

  final Map<String, Offset> positions;
  final Matrix4 transform;
  final double radius;
}

class SubjectGraphController extends ChangeNotifier {
  SubjectGraphController(SubjectGraph graph, {SubjectGraphSnapshot? restored}) {
    _rootId = graph.rootId;
    final defaults = SubjectLayout(graph).positions;
    positions.addAll({
      for (final id in defaults.keys)
        id: restored?.positions[id] ?? defaults[id]!,
    });
    separateSubjectNodes(positions, _rootId);
    _simulation = SubjectGraphSimulation(graph, positions);
    if (restored == null) _simulation.wake();
    radius = math.max(restored?.radius ?? 300, _requiredRadius);
    if (restored != null) {
      transform.value = restored.transform.clone();
      _centered = true;
      _compensateOrigin(radius - restored.radius);
    }
  }

  final positions = <String, Offset>{};
  final transform = TransformationController();
  late String _rootId;
  late final SubjectGraphSimulation _simulation;
  late double radius;
  Size viewportSize = Size.zero;
  bool _centered = false;

  double get _requiredRadius => positions.values.fold(
    300,
    (value, p) => math.max(value, math.max(p.dx.abs(), p.dy.abs()) + 220),
  );

  void _compensateOrigin(double growth) {
    if (growth == 0) return;
    transform.value = transform.value.clone()
      ..translateByDouble(-growth, -growth, 0, 1);
  }

  void _growScene() {
    final required = _requiredRadius;
    if (required <= radius) return;
    final nextRadius = required + 300;
    _compensateOrigin(nextRadius - radius);
    radius = nextRadius;
  }

  void reconcile(SubjectGraph graph) {
    _rootId = graph.rootId;
    final defaults = SubjectLayout(graph).positions;
    positions.removeWhere((id, _) => !defaults.containsKey(id));
    for (final entry in defaults.entries) {
      positions.putIfAbsent(entry.key, () => entry.value);
    }
    separateSubjectNodes(positions, _rootId);
    _simulation.updateGraph(graph);
    // Unchanged data on return should keep the settled view still.
    _growScene();
    notifyListeners();
  }

  void move(String id, Offset delta) {
    if (!positions.containsKey(id)) return;
    _simulation.move(id, delta);
    _growScene();
    notifyListeners();
  }

  bool get isSimulating => _simulation.active;

  void beginDrag(String id) {
    _simulation.pin(id);
    notifyListeners();
  }

  void endDrag() {
    _simulation.release();
    notifyListeners();
  }

  bool advance(double seconds) {
    final active = _simulation.step(seconds);
    _growScene();
    notifyListeners();
    return active;
  }

  void centerIfNeeded() {
    if (!_centered) center();
  }

  void center() {
    if (viewportSize.isEmpty) return;
    final root = positions[_rootId]! + Offset(radius, radius);
    const zoom = .65;
    transform.value = Matrix4.identity()
      ..translateByDouble(
        viewportSize.width / 2 - root.dx * zoom,
        viewportSize.height / 2 - root.dy * zoom,
        0,
        1,
      )
      ..scaleByDouble(zoom, zoom, zoom, 1);
    _centered = true;
  }

  SubjectGraphSnapshot snapshot() =>
      SubjectGraphSnapshot(Map.of(positions), transform.value.clone(), radius);

  @override
  void dispose() {
    transform.dispose();
    super.dispose();
  }
}
