import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'passingtrace_theme.dart';

abstract interface class AppearancePreferenceStore {
  Future<String?> read(String key);

  Future<void> write(String key, String value);
}

class SecureAppearancePreferenceStore implements AppearancePreferenceStore {
  const SecureAppearancePreferenceStore([
    this._storage = const FlutterSecureStorage(),
  ]);

  final FlutterSecureStorage _storage;

  @override
  Future<String?> read(String key) => _storage.read(key: key);

  @override
  Future<void> write(String key, String value) =>
      _storage.write(key: key, value: value);
}

class AppearanceController extends ChangeNotifier {
  AppearanceController([this._store = const SecureAppearancePreferenceStore()]);

  static const paletteKey = 'passingtrace.appearance.palette';
  static const modeKey = 'passingtrace.appearance.mode';

  final AppearancePreferenceStore _store;
  PassingTracePalette _palette = PassingTracePalette.pine;
  ThemeMode _mode = ThemeMode.system;

  PassingTracePalette get palette => _palette;
  ThemeMode get mode => _mode;

  Future<void> load() async {
    try {
      final values = await Future.wait([
        _store.read(paletteKey),
        _store.read(modeKey),
      ]);
      _palette =
          PassingTracePaletteInfo.fromStorageValue(values[0]) ??
          PassingTracePalette.pine;
      _mode = _themeModeFromStorage(values[1]) ?? ThemeMode.system;
    } catch (error, stackTrace) {
      debugPrint('Unable to restore appearance preferences: $error');
      debugPrintStack(stackTrace: stackTrace);
    }
    notifyListeners();
  }

  Future<void> setPalette(PassingTracePalette value) async {
    if (_palette == value) return;
    _palette = value;
    notifyListeners();
    await _persist(paletteKey, value.storageValue);
  }

  Future<void> setMode(ThemeMode value) async {
    if (_mode == value) return;
    _mode = value;
    notifyListeners();
    await _persist(modeKey, value.name);
  }

  Future<void> _persist(String key, String value) async {
    try {
      await _store.write(key, value);
    } catch (error, stackTrace) {
      debugPrint('Unable to save appearance preference: $error');
      debugPrintStack(stackTrace: stackTrace);
    }
  }

  static ThemeMode? _themeModeFromStorage(String? value) {
    for (final mode in ThemeMode.values) {
      if (mode.name == value) return mode;
    }
    return null;
  }
}

class AppearanceScope extends InheritedNotifier<AppearanceController> {
  const AppearanceScope({
    super.key,
    required AppearanceController controller,
    required super.child,
  }) : super(notifier: controller);

  static AppearanceController of(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<AppearanceScope>();
    assert(scope != null, 'AppearanceScope is missing above this context.');
    return scope!.notifier!;
  }
}
