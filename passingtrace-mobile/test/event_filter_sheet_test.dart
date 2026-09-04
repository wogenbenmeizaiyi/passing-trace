import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/events/event_model.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';
import 'package:passingtrace_mobile/views/event_filter_sheet.dart';

const _taxonomy = EventTaxonomyModel(
  version: 'v1',
  categories: [TaxonomyItem('food', '美食'), TaxonomyItem('travel', '旅行')],
  behaviorTags: [TaxonomyItem('dining', '聚餐'), TaxonomyItem('coffee', '咖啡')],
);

void main() {
  test('时间筛选覆盖所选自然日的完整区间', () {
    final selection = EventFilterSelection(
      fromDate: DateTime(2026, 8, 3),
      toDate: DateTime(2026, 8, 28),
    );

    expect(
      DateTime.parse(selection.fromIso8601!).toLocal(),
      DateTime(2026, 8, 3),
    );
    final localEnd = DateTime.parse(selection.toIso8601!).toLocal();
    expect(localEnd.year, 2026);
    expect(localEnd.month, 8);
    expect(localEnd.day, 28);
    expect(localEnd.hour, 23);
    expect(localEnd.minute, 59);
  });

  testWidgets('点击筛选面板外部会关闭且不应用草稿', (tester) async {
    EventFilterSelection? applied;
    var completed = false;
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: Scaffold(
          body: Builder(
            builder: (context) => TextButton(
              onPressed: () async {
                applied = await showEventFilterSheet(
                  context: context,
                  selection: EventFilterSelection(kind: EventKind.trace),
                  taxonomy: _taxonomy,
                );
                completed = true;
              },
              child: const Text('打开筛选'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('打开筛选'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('event-filter-sheet')), findsOneWidget);

    await tester.tapAt(const Offset(12, 12));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('event-filter-sheet')), findsNothing);
    expect(completed, isTrue);
    expect(applied, isNull);
  });

  testWidgets('行为标签默认折叠并在应用后返回完整筛选条件', (tester) async {
    EventFilterSelection? applied;
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: Scaffold(
          body: Builder(
            builder: (context) => TextButton(
              onPressed: () async {
                applied = await showEventFilterSheet(
                  context: context,
                  selection: EventFilterSelection(),
                  taxonomy: _taxonomy,
                );
              },
              child: const Text('打开筛选'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('打开筛选'));
    await tester.pumpAndSettle();
    expect(find.text('聚餐'), findsNothing);

    await tester.tap(find.text('未来安排'));
    final tagToggle = find.byKey(const Key('filter-tags-toggle'));
    await tester.ensureVisible(tagToggle);
    await tester.tap(tagToggle);
    await tester.pumpAndSettle();
    expect(find.text('聚餐'), findsOneWidget);

    final diningTag = find.byKey(const Key('filter-tag-dining'));
    await tester.ensureVisible(diningTag);
    await tester.tap(diningTag);
    await tester.tap(find.byKey(const Key('filter-apply')));
    await tester.pumpAndSettle();

    expect(applied, isNotNull);
    expect(applied!.kind, EventKind.plan);
    expect(applied!.tagKeys, ['dining']);
  });
}
