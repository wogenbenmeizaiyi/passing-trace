import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_canvas.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_controller.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_layout.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_simulation.dart';
import 'package:passingtrace_mobile/subjects/subject_model.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

SubjectGraph graph({bool withNewNode = false}) => SubjectGraph.fromJson({
  'rootId': 'self',
  'nodes': [
    for (final id in ['self', 'cat', if (withNewNode) 'new'])
      {
        'id': id,
        'name': id,
        'kind': id == 'self' ? 0 : 1,
        'isSelf': id == 'self',
        'state': 0,
      },
  ],
  'relations': [
    for (final id in ['cat', if (withNewNode) 'new'])
      {
        'id': id,
        'fromSubjectId': 'self',
        'toSubjectId': id,
        'directed': false,
        'label': '不显示的关系名称',
      },
  ],
});

void expectSeparated(Map<String, Offset> positions) {
  final points = positions.values.toList();
  for (var i = 0; i < points.length; i++) {
    for (var j = i + 1; j < points.length; j++) {
      expect(
        (points[i] - points[j]).distance,
        greaterThanOrEqualTo(subjectNodeSpacing - .1),
      );
    }
  }
}

void main() {
  test('拉远会拉回相连节点，靠近会推开，拖住的节点始终固定', () {
    for (final distance in [190.0, 900.0]) {
      final positions = {'self': Offset.zero, 'cat': Offset(distance, 0)};
      final simulation = SubjectGraphSimulation(graph(), positions)..pin('cat');
      simulation.step(1 / 60);
      expect(positions['cat'], Offset(distance, 0));
      expect(
        positions['self']!.dx,
        distance > subjectLinkDistance ? greaterThan(0) : lessThan(0),
      );
      for (var i = 0; i < 600 && simulation.active; i++) {
        simulation.step(1 / 60);
        expect(positions['cat'], Offset(distance, 0));
        expectSeparated(positions);
      }
      expect(
        (positions['cat']! - positions['self']!).distance,
        closeTo(subjectLinkDistance, 15),
      );
      expect(simulation.active, isFalse);
    }
  });
  test('松手后继续拉近并稳定，关联节点跟随，重复关系不重复施加力', () {
    final positions = {'self': Offset.zero, 'cat': const Offset(900, 0)};
    final simulation = SubjectGraphSimulation(graph(), positions)..pin('cat');
    simulation.step(1 / 60);
    simulation.release();
    final releasedCat = positions['cat']!;
    simulation.step(1 / 60);
    expect(positions['cat']!.dx, lessThan(releasedCat.dx));
    for (var i = 0; i < 1200 && simulation.active; i++) {
      simulation.step(1 / 60);
      expectSeparated(positions);
    }
    expect(simulation.active, isFalse);
    expect(
      (positions['cat']! - positions['self']!).distance,
      closeTo(subjectLinkDistance, 15),
    );
    final stopped = Map.of(positions);
    simulation.step(1 / 60);
    expect(positions, stopped);
    final duplicateGraph = graph();
    duplicateGraph.relations.add(duplicateGraph.relations.single);
    final other = Map.of(stopped);
    final once = SubjectGraphSimulation(graph(), Map.of(stopped))..pin('cat');
    final twice = SubjectGraphSimulation(duplicateGraph, other)..pin('cat');
    once.move('cat', const Offset(100, 0));
    twice.move('cat', const Offset(100, 0));
    for (var i = 0; i < 60; i++) {
      once.step(1 / 60);
      twice.step(1 / 60);
    }
    expect(twice.positions, once.positions);
  });
  test('拖动节点固定在指针下，碰撞沿拥挤节点传播，远处节点不动', () {
    final positions = {
      'dragged': Offset.zero,
      'near': const Offset(80, 0),
      'next': const Offset(240, 0),
      'far': const Offset(2000, 1000),
    };
    separateSubjectNodes(positions, 'dragged');
    expect(positions['dragged'], Offset.zero);
    expect(positions['near']!.dx, greaterThan(80));
    expect(positions['next']!.dx, greaterThan(240));
    expect(positions['far'], const Offset(2000, 1000));
    expectSeparated(positions);
  });
  test('完全重合的密集节点能够分开，没有无效坐标', () {
    final positions = {for (var i = 0; i < 30; i++) 'node-$i': Offset.zero};
    separateSubjectNodes(positions, 'node-0');
    expect(positions['node-0'], Offset.zero);
    expectSeparated(positions);
    expect(
      positions.values.every((p) => p.dx.isFinite && p.dy.isFinite),
      isTrue,
    );
  });
  test('刷新、恢复和新增节点保留手动布局，扩大画布时已有节点不跳动', () {
    final controller = SubjectGraphController(graph());
    controller.viewportSize = const Size(800, 600);
    controller.center();
    final selfBefore = MatrixUtils.transformPoint(
      controller.transform.value,
      controller.positions['self']! +
          Offset(controller.radius, controller.radius),
    );
    controller.move('cat', const Offset(2000, 0));
    final selfAfter = MatrixUtils.transformPoint(
      controller.transform.value,
      controller.positions['self']! +
          Offset(controller.radius, controller.radius),
    );
    expect((selfAfter - selfBefore).distance, lessThan(.001));
    final restored = SubjectGraphController(
      graph(),
      restored: controller.snapshot(),
    );
    expect(restored.positions, controller.positions);
    expect(restored.transform.value, controller.transform.value);
    final savedCat = restored.positions['cat'];
    restored.reconcile(graph(withNewNode: true));
    expect(restored.positions['cat'], savedCat);
    expect(restored.positions.containsKey('new'), isTrue);
    expectSeparated(restored.positions);
    restored.dispose();
    controller.dispose();
  });
  test('回到自己跟随已经移动的自身节点，视图缓存不会共享可变坐标', () {
    final controller = SubjectGraphController(graph());
    controller.viewportSize = const Size(800, 600);
    controller.move('self', const Offset(450, 100));
    final saved = controller.snapshot();
    controller.move('self', const Offset(100, 100));
    expect(saved.positions['self'], const Offset(450, 100));
    controller.center();
    final center = MatrixUtils.transformPoint(
      controller.transform.value,
      controller.positions['self']! +
          Offset(controller.radius, controller.radius),
    );
    expect((center - const Offset(400, 300)).distance, lessThan(.001));
    controller.dispose();
  });
  testWidgets('缩放后直接拖动节点会推动邻居，松手不打开档案，画布仍可平移和缩放', (tester) async {
    final data = graph(), controller = SubjectGraphController(graph());
    final opened = <String>[];
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: Scaffold(
          body: SubjectGraphCanvas(
            graph: data,
            controller: controller,
            matches: const {'self', 'cat'},
            kept: const {'self', 'cat'},
            nickname: '我',
            onOpen: opened.add,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('不显示的关系名称'), findsNothing);
    final initialSelf = controller.positions['self']!;
    final initialCat = controller.positions['cat']!;
    final initialTransform = controller.transform.value.clone();
    final drag = await tester.startGesture(
      tester.getCenter(find.byKey(const ValueKey('subject-node-cat'))),
    );
    await drag.moveBy(const Offset(0, 143));
    await tester.pump(const Duration(milliseconds: 16));
    expect(opened, isEmpty);
    expect(controller.positions['cat']!.dy, closeTo(initialCat.dy + 220, 1));
    expect(controller.positions['self'], isNot(initialSelf));
    expectSeparated(controller.positions);
    expect(controller.transform.value, initialTransform);
    await drag.up();
    await tester.pumpAndSettle();
    expect(controller.isSimulating, isFalse);
    expect(
      (controller.positions['cat']! - controller.positions['self']!).distance,
      closeTo(subjectLinkDistance, 15),
    );
    final positions = Map.of(controller.positions);
    await tester.dragFrom(const Offset(40, 450), const Offset(60, -20));
    await tester.pumpAndSettle();
    expect(controller.positions, positions);
    expect(controller.transform.value, isNot(initialTransform));
    final oldZoom = controller.transform.value.getMaxScaleOnAxis();
    final first = await tester.createGesture(pointer: 11);
    final second = await tester.createGesture(pointer: 12);
    await first.down(const Offset(60, 500));
    await second.down(const Offset(220, 500));
    await first.moveTo(const Offset(40, 500));
    await second.moveTo(const Offset(260, 500));
    await tester.pump();
    await first.up();
    await second.up();
    await tester.pumpAndSettle();
    expect(
      controller.transform.value.getMaxScaleOnAxis(),
      greaterThan(oldZoom),
    );
    await tester.tap(find.byKey(const ValueKey('subject-node-cat')));
    await tester.pumpAndSettle();
    expect(opened, ['cat']);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    controller.dispose();
  });
}
