import 'package:flutter/services.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/auth_service.dart';

void main() {
  test('AuthException exposes a safe user-facing message', () {
    const exception = AuthException('登录已过期');

    expect(exception.toString(), '登录已过期');
  });

  group('AuthService Events API 地址', () {
    const channel = MethodChannel('plugins.it_nomads.com/flutter_secure_storage');
    final store = <String, String>{};

    setUp(() {
      TestWidgetsFlutterBinding.ensureInitialized();
      store.clear();
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(channel, (call) async {
        switch (call.method) {
          case 'readAll':
            return Map<String, String>.from(store);
          case 'read':
            final args = (call.arguments as Map?)?.cast<String, Object?>();
            final key = args?['key'] as String?;
            return key == null ? null : store[key];
          case 'write':
            final args = (call.arguments as Map?)?.cast<String, Object?>();
            final key = args?['key'] as String?;
            final value = args?['value'] as String?;
            if (key != null) {
              if (value == null) {
                store.remove(key);
              } else {
                store[key] = value;
              }
            }
            return null;
          case 'delete':
            final args = (call.arguments as Map?)?.cast<String, Object?>();
            final key = args?['key'] as String?;
            if (key != null) store.remove(key);
            return null;
          case 'deleteAll':
            store.clear();
            return null;
        }
        return null;
      });
    });

    tearDown(() {
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
          .setMockMethodCallHandler(channel, null);
    });

    late AuthService auth;

    setUp(() {
      auth = AuthService(storage: const FlutterSecureStorage());
    });

    test('未保存时回落到 defaultEventsApiUrl', () async {
      final url = await auth.getEventsApiBaseUrl();
      expect(url, AuthService.defaultEventsApiUrl);
    });

    test('setEventsApiBaseUrl 后能读回', () async {
      await auth.setEventsApiBaseUrl('https://10.0.2.2:54934/');
      expect(await auth.getEventsApiBaseUrl(), 'https://10.0.2.2:54934');
    });

    test('setEventsApiBaseUrl 传空字符串会重置为默认值', () async {
      await auth.setEventsApiBaseUrl('https://override.test');
      await auth.setEventsApiBaseUrl('');
      expect(await auth.getEventsApiBaseUrl(), AuthService.defaultEventsApiUrl);
    });

    test('非法 URL 抛 AuthException', () async {
      expect(
        () => auth.setEventsApiBaseUrl('not a url'),
        throwsA(isA<AuthException>()),
      );
    });
  });
}
