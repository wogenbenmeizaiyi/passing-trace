import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter_appauth/flutter_appauth.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';

class _InvalidGrantAppAuth extends FlutterAppAuth {
  const _InvalidGrantAppAuth();

  @override
  Future<TokenResponse> token(TokenRequest request) async {
    throw PlatformException(
      code: 'token_failed',
      message: 'Failed to get token',
      details: const {'error': 'invalid_grant'},
    );
  }
}

void main() {
  test('AuthException exposes a safe user-facing message', () {
    const exception = AuthException('登录已过期');

    expect(exception.toString(), '登录已过期');
  });

  test('USB 调试默认地址指向 adb reverse 端口', () {
    expect(AuthService.defaultIdentityUrl, 'http://localhost:56229');
    expect(AuthService.defaultEventsApiUrl, 'http://localhost:54934');
    expect(minimumPasswordLength, 8);
  });

  group('AuthService Events API 地址', () {
    const channel = MethodChannel(
      'plugins.it_nomads.com/flutter_secure_storage',
    );
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

    test('refresh token 失效时只清除令牌并保留设备配置', () async {
      store.addAll({
        'identity_url': 'http://localhost:56229',
        'events_api_url': 'http://localhost:54934',
        'device_id': 'device-1',
        'device_secret': 'secret-1',
        'access_token': 'expired-access',
        'refresh_token': 'expired-refresh',
        'id_token': 'expired-id',
        'access_token_expires_at': DateTime.now()
            .subtract(const Duration(minutes: 5))
            .toUtc()
            .toIso8601String(),
      });
      final expiringAuth = AuthService(
        storage: const FlutterSecureStorage(),
        appAuth: const _InvalidGrantAppAuth(),
      );
      final session = await expiringAuth.restore();

      await expectLater(
        expiringAuth.ensureFreshToken(session!),
        throwsA(
          isA<AuthSessionExpiredException>().having(
            (error) => error.message,
            'message',
            '登录状态已过期，请重新登录。',
          ),
        ),
      );

      expect(store['identity_url'], 'http://localhost:56229');
      expect(store['events_api_url'], 'http://localhost:54934');
      expect(store['device_id'], 'device-1');
      expect(store['device_secret'], 'secret-1');
      expect(store, isNot(contains('access_token')));
      expect(store, isNot(contains('refresh_token')));
      expect(store, isNot(contains('id_token')));
      expect(store, isNot(contains('access_token_expires_at')));
    });

    test('服务端 invalid_device 被识别为设备凭据失效', () async {
      final invalidDeviceAuth = AuthService(
        storage: const FlutterSecureStorage(),
        httpClient: MockClient(
          (_) async => http.Response.bytes(
            utf8.encode('{"title":"invalid_device","detail":"移动设备凭据无效。"}'),
            403,
            headers: {'content-type': 'application/problem+json'},
          ),
        ),
      );
      final session = AuthSession(
        identityBaseUrl: 'http://localhost:56229',
        deviceId: 'missing-device',
        deviceSecret: 'expired-secret',
      );

      await expectLater(
        invalidDeviceAuth.login(session),
        throwsA(
          isA<DeviceCredentialsInvalidException>().having(
            (error) => error.message,
            'message',
            '此手机的设备凭据已失效，请重新登录绑定。',
          ),
        ),
      );
    });
  });
}
