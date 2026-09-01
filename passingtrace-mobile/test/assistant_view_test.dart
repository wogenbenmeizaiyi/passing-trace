import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';
import 'package:passingtrace_mobile/views/assistant_view.dart';

void main() {
  testWidgets('AI 回答按 Markdown 渲染标题、粗体和列表', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: AssistantMessageContent(
            isUser: false,
            text: '**月度总结**\n\n- 第一条记录\n- 第二条记录',
          ),
        ),
      ),
    );

    expect(find.text('**月度总结**'), findsNothing);
    expect(find.text('月度总结'), findsOneWidget);
    expect(find.text('第一条记录'), findsOneWidget);
    expect(find.text('第二条记录'), findsOneWidget);
  });

  testWidgets('用户消息保持普通文本显示', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: AssistantMessageContent(isUser: true, text: '**不要解析**'),
        ),
      ),
    );

    expect(find.text('**不要解析**'), findsOneWidget);
  });

  testWidgets('Event 引用显示记录标题并可点击', (tester) async {
    int? openedEventId;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: AssistantMessageContent(
            isUser: false,
            text: '完成了阶段计划 [Event #13]',
            eventTitles: const {13: '整理项目下一阶段计划'},
            onOpenEvent: (id) => openedEventId = id,
          ),
        ),
      ),
    );

    expect(find.textContaining('Event #13'), findsNothing);
    final richText = tester
        .widgetList<SelectableText>(find.byType(SelectableText))
        .singleWhere(
          (widget) => widget.textSpan!.toPlainText().contains('整理项目下一阶段计划'),
        );
    expect(richText.textSpan!.toPlainText(), isNot(contains('Event #13')));
    final link = _findTappableSpan(richText.textSpan!)!;
    (link.recognizer! as TapGestureRecognizer).onTap!.call();
    expect(openedEventId, 13);
  });

  testWidgets('回答依据默认收起，展开后才显示记录卡片', (tester) async {
    int? openedEventId;
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: Scaffold(
          body: AssistantEvidenceDisclosure(
            records: const {13: '整理项目下一阶段计划', 8: '和朋友吃火锅'},
            onOpenEvent: (id) => openedEventId = id,
          ),
        ),
      ),
    );

    expect(find.text('相关记录'), findsOneWidget);
    expect(find.text('2 条'), findsOneWidget);
    expect(find.text('整理项目下一阶段计划'), findsNothing);

    await tester.tap(find.bySemanticsLabel('展开相关记录，共 2 条'));
    await tester.pump();

    expect(find.text('整理项目下一阶段计划'), findsOneWidget);
    expect(find.text('和朋友吃火锅'), findsOneWidget);
    await tester.tap(find.text('和朋友吃火锅'));
    expect(openedEventId, 8);

    await tester.tap(find.bySemanticsLabel('收起相关记录，共 2 条'));
    await tester.pump();
    expect(find.text('整理项目下一阶段计划'), findsNothing);
  });
}

TextSpan? _findTappableSpan(InlineSpan span) {
  if (span is! TextSpan) return null;
  if (span.recognizer is TapGestureRecognizer) return span;
  for (final child in span.children ?? const <InlineSpan>[]) {
    final found = _findTappableSpan(child);
    if (found != null) return found;
  }
  return null;
}
