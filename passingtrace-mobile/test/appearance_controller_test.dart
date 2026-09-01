import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/theme/appearance_controller.dart';
import 'package:passingtrace_mobile/theme/appearance_sheet.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

void main() {
  test('restores the saved palette and display mode', () async {
    final store = _MemoryAppearanceStore({
      AppearanceController.paletteKey: PassingTracePalette.plum.storageValue,
      AppearanceController.modeKey: ThemeMode.dark.name,
    });
    final controller = AppearanceController(store);

    await controller.load();

    expect(controller.palette, PassingTracePalette.plum);
    expect(controller.mode, ThemeMode.dark);
  });

  test('invalid saved values fall back to pine and system mode', () async {
    final store = _MemoryAppearanceStore({
      AppearanceController.paletteKey: 'unknown',
      AppearanceController.modeKey: 'unknown',
    });
    final controller = AppearanceController(store);

    await controller.load();

    expect(controller.palette, PassingTracePalette.pine);
    expect(controller.mode, ThemeMode.system);
  });

  testWidgets('appearance sheet changes and persists the selected theme', (
    tester,
  ) async {
    final store = _MemoryAppearanceStore();
    final controller = AppearanceController(store);

    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: AppearanceScope(
          controller: controller,
          child: const Scaffold(body: AppearanceSheet()),
        ),
      ),
    );

    await tester.tap(find.text('潮汐'));
    await tester.pump();
    await tester.tap(find.text('深色'));
    await tester.pump();

    expect(controller.palette, PassingTracePalette.tide);
    expect(controller.mode, ThemeMode.dark);
    expect(
      store.values[AppearanceController.paletteKey],
      PassingTracePalette.tide.storageValue,
    );
    expect(store.values[AppearanceController.modeKey], ThemeMode.dark.name);
  });
}

class _MemoryAppearanceStore implements AppearancePreferenceStore {
  _MemoryAppearanceStore([Map<String, String>? initial])
    : values = {...?initial};

  final Map<String, String> values;

  @override
  Future<String?> read(String key) async => values[key];

  @override
  Future<void> write(String key, String value) async {
    values[key] = value;
  }
}
