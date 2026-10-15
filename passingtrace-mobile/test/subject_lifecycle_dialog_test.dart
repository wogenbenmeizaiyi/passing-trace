import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/subjects/subject_lifecycle_dialog.dart';
import 'package:passingtrace_mobile/subjects/subject_model.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

final archive = SubjectModel({
  'id': 'house',
  'name': '小屋',
  'state': 1,
  'endReason': 'sold',
  'endedAt': '2026-09-30T12:00:00Z',
});

SubjectEntry plan(int index) => SubjectEntry({
  'id': 'plan-$index',
  'title': '维护计划 $index',
  'version': index + 2,
});

Future<void> openDialog(
  WidgetTester tester, {
  String operation = 'end',
  List<SubjectEntry> plans = const [],
  double textScale = 1,
  required ValueChanged<Map<String, dynamic>?> onResult,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      theme: PassingTraceTheme.light(PassingTracePalette.pine),
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(context)
            .copyWith(textScaler: TextScaler.linear(textScale)),
        child: child!,
      ),
      home: Builder(
        builder: (context) => Scaffold(
          body: FilledButton(
            onPressed: () async => onResult(
              await showSubjectLifecycleDialog(
                context: context,
                subject: archive,
                operation: operation,
                plans: plans,
                correctsId: 'last-end',
              ),
            ),
            child: const Text('打开'),
          ),
        ),
      ),
    ),
  );
  await tester.tap(find.text('打开'));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('结束表单间距清晰，长计划列表滚动不移动按钮，仅取消勾选计划', (tester) async {
    tester.view.physicalSize = const Size(360, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    Map<String, dynamic>? result;
    await openDialog(
      tester,
      plans: [for (var i = 0; i < 30; i++) plan(i)],
      onResult: (value) => result = value,
    );
    final summary = find.byKey(const Key('subject-lifecycle-summary'));
    final date = find.byKey(const Key('subject-lifecycle-date'));
    final reason = find.byKey(const Key('subject-lifecycle-reason'));
    final note = find.byKey(const Key('subject-lifecycle-note'));
    expect(tester.getTopLeft(date).dy - tester.getBottomLeft(summary).dy, 24);
    expect(tester.getTopLeft(reason).dy - tester.getBottomLeft(date).dy, 24);
    expect(tester.getTopLeft(note).dy - tester.getBottomLeft(reason).dy, 24);
    final actions = find.byKey(const Key('subject-lifecycle-actions'));
    final actionRect = tester.getRect(actions);
    final scroll = find
        .descendant(
          of: find.byKey(const Key('subject-lifecycle-scroll')),
          matching: find.byType(Scrollable),
        )
        .first;
    final dateInput = find.descendant(
      of: date,
      matching: find.byType(TextFormField),
    );
    await tester.enterText(dateInput, '错误日期');
    await tester.tap(find.text('确认结束'));
    await tester.pumpAndSettle();
    expect(result, isNull);
    expect(find.text('日期格式应为 YYYY-MM-DD HH:mm'), findsOneWidget);
    await tester.enterText(dateInput, '2026-10-01 12:30');
    await tester.enterText(note, '出售小屋，保留其他计划');
    FocusManager.instance.primaryFocus?.unfocus();
    await tester.pumpAndSettle();
    final lastPlan = find.byKey(
      const ValueKey('subject-lifecycle-plan-plan-29'),
    );
    await tester.scrollUntilVisible(lastPlan, 250, scrollable: scroll);
    await tester.pumpAndSettle();
    expect(tester.getRect(actions), actionRect);
    expect(tester.widget<CheckboxListTile>(lastPlan).value, isFalse);
    await tester.tap(lastPlan);
    await tester.pumpAndSettle();
    await tester.tap(find.text('确认结束'));
    await tester.pumpAndSettle();
    expect(result!['operation'], 'end');
    expect(result!['reason'], 'sold');
    expect(result!['effectiveAt'], startsWith('2026-10-01T12:30:00'));
    expect(result!['note'], '出售小屋，保留其他计划');
    expect(result!['cancelPlans'], [
      {'id': 'plan-29', 'version': 31},
    ]);
    expect(find.byKey(const Key('subject-lifecycle-dialog')), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('小屏大字体和键盘下表单可滚动，取消与确认始终可见', (tester) async {
    tester.view.physicalSize = const Size(320, 568);
    tester.view.devicePixelRatio = 1;
    tester.view.padding = const FakeViewPadding(bottom: 24);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPadding);
    addTearDown(tester.view.resetViewInsets);
    var returned = false;
    Map<String, dynamic>? result;
    await openDialog(
      tester,
      textScale: 1.8,
      plans: [for (var i = 0; i < 30; i++) plan(i)],
      onResult: (value) {
        returned = true;
        result = value;
      },
    );
    tester.view.viewInsets = const FakeViewPadding(bottom: 260);
    await tester.pumpAndSettle();
    final actions = find.byKey(const Key('subject-lifecycle-actions'));
    final cancel = find.widgetWithText(OutlinedButton, '取消');
    final confirm = find.widgetWithText(FilledButton, '确认结束');
    for (final button in [cancel, confirm]) {
      final rect = tester.getRect(button);
      expect(rect.top, greaterThanOrEqualTo(0));
      expect(rect.bottom, lessThanOrEqualTo(308));
      expect(rect.left, greaterThanOrEqualTo(16));
      expect(rect.right, lessThanOrEqualTo(304));
      expect(rect.height, greaterThanOrEqualTo(48));
    }
    final actionRect = tester.getRect(actions);
    final scroll = find
        .descendant(
          of: find.byKey(const Key('subject-lifecycle-scroll')),
          matching: find.byType(Scrollable),
        )
        .first;
    await tester.drag(scroll, const Offset(0, -300));
    await tester.pumpAndSettle();
    expect(
      tester.state<ScrollableState>(scroll).position.pixels,
      greaterThan(0),
    );
    expect(tester.getRect(actions), actionRect);
    expect(tester.takeException(), isNull);
    await tester.tap(cancel);
    await tester.pumpAndSettle();
    expect(returned, isTrue);
    expect(result, isNull);
    expect(tester.takeException(), isNull);
  });

  testWidgets('恢复和纠正沿用各自字段及说明，纠正绑定原结束节点', (tester) async {
    Map<String, dynamic>? result;
    await openDialog(
      tester,
      operation: 'resume',
      plans: [plan(0)],
      onResult: (value) => result = value,
    );
    expect(find.text('恢复人物档案'), findsOneWidget);
    expect(find.textContaining('不会自动恢复'), findsOneWidget);
    expect(find.byKey(const Key('subject-lifecycle-reason')), findsNothing);
    expect(find.text('待执行计划'), findsNothing);
    await tester.tap(find.text('保存'));
    await tester.pumpAndSettle();
    expect(result!['operation'], 'resume');
    expect(result!['cancelPlans'], isEmpty);
    expect(result!.containsKey('correctsId'), isFalse);
    await openDialog(
      tester,
      operation: 'correct',
      onResult: (value) => result = value,
    );
    expect(find.text('纠正结束信息'), findsOneWidget);
    expect(find.textContaining('撤销误标'), findsOneWidget);
    final dateInput = find.descendant(
      of: find.byKey(const Key('subject-lifecycle-date')),
      matching: find.byType(TextFormField),
    );
    final date = tester.widget<TextFormField>(dateInput).controller!.text;
    expect(date, isNotEmpty);
    await tester.ensureVisible(find.text('纠正误标，撤销本次结束'));
    await tester.tap(find.text('纠正误标，撤销本次结束'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('保存'));
    await tester.pumpAndSettle();
    expect(result!['operation'], 'correct');
    expect(result!['correctsId'], 'last-end');
    expect(result!['undoEnd'], isTrue);
    expect(result!['cancelPlans'], isEmpty);
    expect(tester.takeException(), isNull);
  });
}
