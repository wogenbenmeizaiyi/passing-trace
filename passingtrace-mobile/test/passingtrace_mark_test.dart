import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/theme/passingtrace_mark.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

void main() {
  testWidgets('brand mark renders at the requested size in the active theme', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.tide),
        home: const Center(child: PassingTraceMark(size: 48)),
      ),
    );

    final box = tester.getSize(find.byType(PassingTraceMark));
    expect(box, const Size.square(48));
    expect(
      find.descendant(
        of: find.byType(PassingTraceMark),
        matching: find.byType(CustomPaint),
      ),
      findsOneWidget,
    );
  });
}
