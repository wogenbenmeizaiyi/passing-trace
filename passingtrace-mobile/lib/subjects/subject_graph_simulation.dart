import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'subject_graph_layout.dart';
import 'subject_model.dart';

const subjectLinkDistance = 260.0;

/// Springs hold related nodes at a comfortable distance. Charge and collision
/// keep every circle apart; damped velocity lets the graph settle after release.
class SubjectGraphSimulation {
  SubjectGraphSimulation(SubjectGraph graph, this.positions) {
    updateGraph(graph);
  }

  final Map<String, Offset> positions;
  final _velocities = <String, Offset>{};
  List<(String, String)> _links = [];
  String? _pinnedId;
  bool active = false;
  double _alpha = 1;
  int _quietFrames = 0;

  void updateGraph(SubjectGraph graph) {
    final seen = <String>{};
    _links = [
      for (final relation in graph.relations)
        if (positions.containsKey(relation.from) &&
            positions.containsKey(relation.to) &&
            relation.from != relation.to &&
            seen.add(([relation.from, relation.to]..sort()).join(':')))
          (relation.from, relation.to),
    ];
    _velocities.removeWhere((id, _) => !positions.containsKey(id));
  }

  void wake() {
    active = true;
    _alpha = 1;
    _quietFrames = 0;
  }

  void pin(String id) {
    if (!positions.containsKey(id)) return;
    _pinnedId = id;
    _velocities[id] = Offset.zero;
    wake();
  }

  void release() {
    _pinnedId = null;
    if (active) wake();
  }

  void move(String id, Offset delta) {
    if (!positions.containsKey(id)) return;
    positions[id] = positions[id]! + delta;
    _velocities[id] = Offset.zero;
    separateSubjectNodes(positions, id);
    wake();
  }

  bool step(double seconds) {
    if (!active || seconds <= 0) return active;
    final time = (seconds * 60).clamp(.1, 2.0);
    final before = Map.of(positions);
    final forces = {for (final id in positions.keys) id: Offset.zero};
    for (final (a, b) in _links) {
      final delta = positions[b]! - positions[a]!;
      final distance = delta.distance;
      if (distance < .001) continue;
      final force =
          delta / distance * ((distance - subjectLinkDistance) * .045);
      forces[a] = forces[a]! + force;
      forces[b] = forces[b]! - force;
    }
    final ids = positions.keys.toList();
    for (var i = 0; i < ids.length; i++) {
      for (var j = i + 1; j < ids.length; j++) {
        final a = ids[i], b = ids[j];
        final delta = positions[b]! - positions[a]!;
        final distance = delta.distance;
        if (distance < .001) continue;
        final force =
            delta / distance * math.min(3, 10000 / (distance * distance));
        forces[a] = forces[a]! - force;
        forces[b] = forces[b]! + force;
      }
    }
    for (final id in ids) {
      if (id == _pinnedId) continue;
      var velocity =
          ((_velocities[id] ?? Offset.zero) + forces[id]! * (_alpha * time)) *
          math.pow(.72, time).toDouble();
      if (velocity.distance > 14) velocity = velocity / velocity.distance * 14;
      _velocities[id] = velocity;
      positions[id] = positions[id]! + velocity * time;
    }
    separateSubjectNodes(positions, _pinnedId ?? '');
    final movement = ids.fold(
      0.0,
      (max, id) => math.max(max, (positions[id]! - before[id]!).distance),
    );
    _alpha = math.max(
      _pinnedId == null ? 0 : .15,
      _alpha * math.pow(.98, time).toDouble(),
    );
    _quietFrames = movement < .06 ? _quietFrames + 1 : 0;
    if (_quietFrames >= 12) {
      active = false;
      _velocities.clear();
    }
    return active;
  }
}
