import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter_appauth/flutter_appauth.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/build_environment.dart';

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

class _RecordingAppAuth extends FlutterAppAuth {
  _RecordingAppAuth(this.responses);

  final List<TokenResponse> responses;
  final List<TokenRequest> requests = [];

  @override
  Future<TokenResponse> token(TokenRequest request) async {
    requests.add(request);
    return responses.removeAt(0);
  }
}

TokenResponse _token(
  String accessToken,
  String refreshToken, {
  Duration lifetime = const Duration(minutes: 15),
}) => TokenResponse(
  accessToken,
  refreshToken,
  DateTime.now().toUtc().add(lifetime),
  'id-$accessToken',
  'Bearer',
  mobileScopes,
  null,
);

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

    test('production 切换时清除旧环境凭据并锁定公网地址', () async {
      store.addAll({
        'identity_url': 'http://127.0.0.1:56229',
        'events_api_url': 'http://127.0.0.1:54934',
        'device_id': 'local-device',
        'device_secret': 'local-secret',
        'access_token': 'local-token',
      });
      const production = BuildEnvironment(
        channel: 'production',
        identityUrl: 'https://auth.passingtrace.com',
        eventsApiUrl: 'https://passingtrace.com',
        allowEndpointOverrides: false,
      );
      final productionAuth = AuthService(
        storage: const FlutterSecureStorage(),
        environment: production,
      );

      expect(await productionAuth.restore(), isNull);
      expect(store, {'build_channel': 'production'});
      expect(
        await productionAuth.getEventsApiBaseUrl(),
        'https://passingtrace.com',
      );

      await productionAuth.setEventsApiBaseUrl('http://127.0.0.1:54934');
      expect(
        await productionAuth.getEventsApiBaseUrl(),
        'https://passingtrace.com',
      );
      expect(store, {'build_channel': 'production'});
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

    test('刷新后旧页面传入的 session 会复用最新令牌', () async {
      store.addAll({
        'identity_url': 'http://localhost:56229',
        'device_id': 'device-1',
        'device_secret': 'secret-1',
        'access_token': 'access-old',
        'refresh_token': 'refresh-old',
        'access_token_expires_at': DateTime.now()
            .subtract(const Duration(minutes: 1))
            .toUtc()
            .toIso8601String(),
      });
      final appAuth = _RecordingAppAuth([_token('access-new', 'refresh-new')]);
      final rotatingAuth = AuthService(
        storage: const FlutterSecureStorage(),
        appAuth: appAuth,
      );
      final stale = (await rotatingAuth.restore())!;

      final refreshed = await rotatingAuth.ensureFreshToken(stale);
      final reused = await rotatingAuth.ensureFreshToken(stale);

      expect(refreshed.accessToken, 'access-new');
      expect(reused.accessToken, 'access-new');
      expect(appAuth.requests, hasLength(1));
      expect(store['refresh_token'], 'refresh-new');
    });

    test('密码登录的 handoff 直接取回授权码，不打开外部浏览器', () async {
      String? state;
      final client = MockClient((request) async {
        if (request.url.path == '/api/mobile/logins') {
          final body = jsonDecode(request.body) as Map<String, dynamic>;
          state = body['state'] as String;
          final authorizeUrl = Uri(
            scheme: 'http',
            host: 'localhost',
            port: 56229,
            path: '/connect/authorize',
            queryParameters: {
              'handoff_code': 'one-time-handoff',
              'state': state,
            },
          );
          return http.Response(
            jsonEncode({
              'authorizeUrl': authorizeUrl.toString(),
              'deviceId': 'device-2',
              'deviceSecret': 'secret-2',
            }),
            200,
          );
        }
        if (request.url.path == '/connect/authorize') {
          expect(request.followRedirects, isFalse);
          final callback = Uri(
            scheme: 'com.passingtrace.mobile',
            path: '/oauth2redirect',
            queryParameters: {'code': 'authorization-code', 'state': state},
          );
          return http.Response('', 302, headers: {'location': '$callback'});
        }
        fail('意外请求：${request.method} ${request.url}');
      });
      final appAuth = _RecordingAppAuth([
        _token('access-login', 'refresh-login'),
      ]);
      final directAuth = AuthService(
        storage: const FlutterSecureStorage(),
        appAuth: appAuth,
        httpClient: client,
      );

      final session = await directAuth.loginWithPassword(
        identityBaseUrl: 'http://localhost:56229',
        username: 'owner',
        password: 'password',
        deviceName: 'Android',
      );

      expect(session.accessToken, 'access-login');
      expect(appAuth.requests.single.authorizationCode, 'authorization-code');
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
