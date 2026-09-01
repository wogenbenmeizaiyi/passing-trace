import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/theme/appearance_controller.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';
import 'package:passingtrace_mobile/views/settings_view.dart';

void main() {
  testWidgets('theme choices are nested under the settings appearance row', (
    tester,
  ) async {
    final controller = AppearanceController(_MemoryAppearanceStore());

    await tester.pumpWidget(
      AppearanceScope(
        controller: controller,
        child: MaterialApp(
          theme: PassingTraceTheme.light(PassingTracePalette.pine),
          home: SettingsView(onSignOut: () async {}),
        ),
      ),
    );

    expect(find.text('设置'), findsOneWidget);
    expect(find.text('主题与外观'), findsOneWidget);
    expect(find.text('松间 · 跟随系统'), findsOneWidget);
    expect(find.text('潮汐'), findsNothing);

    await tester.tap(find.text('主题与外观'));
    await tester.pumpAndSettle();

    expect(find.text('主题与外观'), findsNWidgets(2));
    expect(find.text('潮汐'), findsOneWidget);

    await tester.tap(find.text('潮汐'));
    await tester.pump();
    expect(controller.palette, PassingTracePalette.tide);
  });
}

class _MemoryAppearanceStore implements AppearancePreferenceStore {
  final Map<String, String> values = {};

  @override
  Future<String?> read(String key) async => values[key];

  @override
  Future<void> write(String key, String value) async {
    values[key] = value;
  }
}
