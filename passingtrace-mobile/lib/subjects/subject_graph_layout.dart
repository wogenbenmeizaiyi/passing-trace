import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'subject_model.dart';

const subjectNodeDiameter = 160.0;
const subjectNodeSpacing = subjectNodeDiameter + 24;

/// Keep the dragged node under the pointer and push overlapping neighbours out.
/// Repeating the pair pass propagates movement through a crowded cluster.
void separateSubjectNodes(Map<String, Offset> positions, String pinnedId) {
  final ids = positions.keys.toList()..sort();
  for (var pass = 0; pass < 80; pass++) {
    var largestOverlap = 0.0;
    for (var i = 0; i < ids.length; i++) {
      for (var j = i + 1; j < ids.length; j++) {
        final a = ids[i], b = ids[j];
        final delta = positions[b]! - positions[a]!;
        final distance = delta.distance;
        final overlap = subjectNodeSpacing - distance;
        if (overlap <= .01) continue;
        largestOverlap = math.max(largestOverlap, overlap);
        final angle = (i + j * ids.length) * 2.399963229728653;
        final direction = distance > .0001
            ? delta / distance
            : Offset(math.cos(angle), math.sin(angle));
        // A tiny margin avoids residual intersections caused by rounding.
        final push = direction * (overlap + .02);
        if (a == pinnedId) {
          positions[b] = positions[b]! + push;
        } else if (b == pinnedId) {
          positions[a] = positions[a]! - push;
        } else {
          positions[a] = positions[a]! - push / 2;
          positions[b] = positions[b]! + push / 2;
        }
      }
    }
    if (largestOverlap <= .01) break;
  }
}

class SubjectLayout {
  SubjectLayout(SubjectGraph graph) {
    final adjacent = {for (final n in graph.nodes) n.id: <String>[]};
    for (final r in graph.relations) {
      adjacent[r.from]?.add(r.to);
      adjacent[r.to]?.add(r.from);
    }
    final depths = {graph.rootId: 0}, queue = [graph.rootId];
    for (var i = 0; i < queue.length; i++) {
      for (final next in adjacent[queue[i]] ?? <String>[]) {
        if (!depths.containsKey(next)) {
          depths[next] = depths[queue[i]]! + 1;
          parents[next] = queue[i];
          queue.add(next);
        }
      }
    }
    positions[graph.rootId] = Offset.zero;
    var radius = 0.0;
    for (var level = 1; level <= depths.values.fold(0, math.max); level++) {
      final ring = graph.nodes.where((n) => depths[n.id] == level).toList()
        ..sort((a, b) => a.id.compareTo(b.id));
      radius = math.max(radius + 260, ring.length * 260 / (2 * math.pi));
      for (var i = 0; i < ring.length; i++) {
        final angle = 2 * math.pi * i / ring.length - math.pi / 2;
        positions[ring[i].id] = Offset(
          math.cos(angle) * radius,
          math.sin(angle) * radius,
        );
      }
    }
  }
  final Map<String, Offset> positions = {};
  final Map<String, String> parents = {};
  Set<String> pathsToRoot(Set<String> matches, String root) {
    final kept = {...matches, root};
    for (final id in matches) {
      String? next = id;
      while ((next = parents[next]) != null) {
        kept.add(next!);
      }
    }
    return kept;
  }
}
