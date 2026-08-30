import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/events/event_model.dart';
import 'package:passingtrace_mobile/events/events_api.dart';

class _FakeAuthService extends AuthService {
  _FakeAuthService(this._token);

  final String _token;
  int refreshCalls = 0;

  @override
  Future<AuthSession> ensureFreshToken(AuthSession current) async {
    refreshCalls += 1;
    return AuthSession(
      identityBaseUrl: current.identityBaseUrl,
      deviceId: current.deviceId,
      deviceSecret: current.deviceSecret,
      accessToken: _token,
      refreshToken: current.refreshToken,
      idToken: current.idToken,
      accessTokenExpiration: current.accessTokenExpiration,
    );
  }
}

class _ExpiredAuthService extends AuthService {
  @override
  Future<AuthSession> ensureFreshToken(AuthSession current) async {
    throw const AuthSessionExpiredException();
  }
}

AuthSession _session(String token) => AuthSession(
  identityBaseUrl: 'https://id.test',
  deviceId: 'dev-1',
  deviceSecret: 'sec-1',
  accessToken: token,
  accessTokenExpiration: DateTime.now().add(const Duration(minutes: 10)),
);

Map<String, String> _lastHeaders(List<http.Request> captured) {
  if (captured.isEmpty) return const {};
  return captured.last.headers;
}

Uri _lastUri(List<http.Request> captured) {
  if (captured.isEmpty) throw StateError('no captured request');
  return captured.last.url;
}

Map<String, dynamic> _lastBody(List<http.Request> captured) {
  if (captured.isEmpty || captured.last.body.isEmpty) return const {};
  return jsonDecode(captured.last.body) as Map<String, dynamic>;
}

void main() {
  group('EventApiClient', () {
    test('过期登录被转换成安全的 401 业务错误', () async {
      var requested = false;
      final client = EventApiClient(
        auth: _ExpiredAuthService(),
        baseUrl: 'https://events.test',
        httpClient: MockClient((_) async {
          requested = true;
          return http.Response('{}', 200);
        }),
      );

      await expectLater(
        client.list(_session('expired')),
        throwsA(
          isA<EventApiException>()
              .having((error) => error.status, 'status', 401)
              .having((error) => error.message, 'message', '登录状态已过期，请重新登录。'),
        ),
      );
      expect(requested, isFalse);
      client.close();
    });

    test('list 拼接查询参数并发送 Bearer', () async {
      final captured = <http.Request>[];
      final mock = MockClient((request) async {
        captured.add(request);
        return http.Response(
          jsonEncode({'items': <Map<String, dynamic>>[], 'nextCursor': null}),
          200,
          headers: {'content-type': 'application/json'},
        );
      });
      final auth = _FakeAuthService('access-1');
      final client = EventApiClient(
        auth: auth,
        baseUrl: 'https://events.test',
        httpClient: mock,
      );

      await client.list(
        _session('stale'),
        limit: 20,
        cursor: 5,
        kind: EventKind.trace,
        status: EventStatus.completed,
      );

      final uri = _lastUri(captured);
      expect(uri.path, '/api/v1/events');
      expect(uri.queryParameters['limit'], '20');
      expect(uri.queryParameters['cursor'], '5');
      expect(uri.queryParameters['kind'], '0');
      expect(uri.queryParameters['status'], '1');
      expect(_lastHeaders(captured)['Authorization'], 'Bearer access-1');
      // `_bearerHeaders` 会在每次请求前调一次 `ensureFreshToken`；
      // token 本就没过期时这是单次查询，不会触发 refresh 流程。
      expect(auth.refreshCalls, 1);

      client.close();
    });

    test('get 拒绝非法 id', () async {
      final mock = MockClient((_) async => http.Response('{}', 200));
      final client = EventApiClient(
        auth: _FakeAuthService('t'),
        baseUrl: 'https://events.test',
        httpClient: mock,
      );
      expect(() => client.get(_session('t'), 0), throwsArgumentError);
      expect(() => client.get(_session('t'), -1), throwsArgumentError);
      client.close();
    });

    test('create 必传 Idempotency-Key 并把 kind 转回数字', () async {
      final captured = <http.Request>[];
      final mock = MockClient((request) async {
        captured.add(request);
        return http.Response(
          jsonEncode({
            'id': 1,
            'kind': 1,
            'status': 0,
            'title': '下周东京',
            'rawContent': null,
            'happenedAt': null,
            'plannedAt': '2026-08-25T10:00:00+09:00',
            'completedAt': null,
            'timezone': 'Asia/Tokyo',
            'visibility': 0,
            'sourceRevision': 1,
            'version': 10,
            'createdAt': '2026-08-20T10:00:00+00:00',
            'updatedAt': '2026-08-20T10:00:00+00:00',
          }),
          201,
          headers: {'content-type': 'application/json'},
        );
      });
      final client = EventApiClient(
        auth: _FakeAuthService('access-2'),
        baseUrl: 'https://events.test',
        httpClient: mock,
      );

      await client.create(
        _session('stale'),
        kind: EventKind.plan,
        title: '下周东京',
        timezone: 'Asia/Tokyo',
        plannedAt: DateTime.utc(2026, 8, 25, 1, 0),
        idempotencyKey: 'idem-fixed',
      );

      expect(_lastHeaders(captured)['Idempotency-Key'], 'idem-fixed');
      final body = _lastBody(captured);
      expect(body['kind'], 1);
      expect(body['title'], '下周东京');
      expect(body['timezone'], 'Asia/Tokyo');
      expect(body['plannedAt'], '2026-08-25T01:00:00.000Z');

      client.close();
    });

    test('create 缺少 Idempotency-Key 时直接抛 ArgumentError（不发请求）', () async {
      var called = false;
      final mock = MockClient((_) async {
        called = true;
        return http.Response('{}', 201);
      });
      final client = EventApiClient(
        auth: _FakeAuthService('t'),
        baseUrl: 'https://events.test',
        httpClient: mock,
      );
      expect(
        () => client.create(
          _session('t'),
          kind: EventKind.trace,
          timezone: 'UTC',
          idempotencyKey: '',
        ),
        throwsArgumentError,
      );
      expect(called, isFalse);
      client.close();
    });

    test('update 必传 If-Match', () async {
      final captured = <http.Request>[];
      final mock = MockClient((request) async {
        captured.add(request);
        return http.Response(
          jsonEncode({
            'id': 9,
            'kind': 0,
            'status': 0,
            'title': '新',
            'rawContent': null,
            'happenedAt': null,
            'plannedAt': null,
            'completedAt': null,
            'timezone': 'Asia/Tokyo',
            'visibility': 0,
            'sourceRevision': 2,
            'version': 1285,
            'createdAt': '2026-08-18T10:00:00+00:00',
            'updatedAt': '2026-08-18T10:00:00+00:00',
          }),
          200,
          headers: {'content-type': 'application/json'},
        );
      });
      final client = EventApiClient(
        auth: _FakeAuthService('access-3'),
        baseUrl: 'https://events.test',
        httpClient: mock,
      );

      await client.update(
        _session('stale'),
        9,
        title: '新',
        timezone: 'Asia/Tokyo',
        version: 1284,
      );

      expect(_lastHeaders(captured)['If-Match'], '1284');
      expect(_lastUri(captured).path, '/api/v1/events/9');
      client.close();
    });

    test('remove 必传 If-Match', () async {
      final captured = <http.Request>[];
      final mock = MockClient((request) async {
        captured.add(request);
        return http.Response('', 204);
      });
      final client = EventApiClient(
        auth: _FakeAuthService('access-4'),
        baseUrl: 'https://events.test',
        httpClient: mock,
      );
      await client.remove(_session('stale'), 9, version: 7);
      expect(_lastHeaders(captured)['If-Match'], '7');
      expect(captured.last.method, 'DELETE');
      client.close();
    });

    test('业务错误抛 EventApiException 并保留 ProblemDetails', () async {
      final mock = MockClient(
        (_) async => http.Response(
          jsonEncode({'status': 409, 'title': '版本冲突', 'detail': '内容已被他人修改。'}),
          409,
          headers: const {
            'content-type': 'application/problem+json; charset=utf-8',
          },
        ),
      );
      final client = EventApiClient(
        auth: _FakeAuthService('t'),
        baseUrl: 'https://events.test',
        httpClient: mock,
      );
      try {
        await client.update(
          _session('t'),
          1,
          title: 'x',
          timezone: 'UTC',
          version: 1,
        );
        fail('应该抛 EventApiException');
      } on EventApiException catch (e) {
        expect(e.status, 409);
        expect(e.message, '内容已被他人修改。');
        expect(e.problem?.title, '版本冲突');
      }
      client.close();
    });

    test('401 触发一次 ensureFreshToken 后重试', () async {
      var calls = 0;
      final mock = MockClient((request) async {
        calls += 1;
        if (calls == 1) {
          return http.Response(
            jsonEncode({'status': 401, 'title': 'expired'}),
            401,
            headers: {'content-type': 'application/problem+json'},
          );
        }
        return http.Response(
          jsonEncode({
            'id': 1,
            'kind': 0,
            'status': 0,
            'title': null,
            'rawContent': null,
            'happenedAt': null,
            'plannedAt': null,
            'completedAt': null,
            'timezone': 'UTC',
            'visibility': 0,
            'sourceRevision': 0,
            'version': 1,
            'createdAt': '2026-08-18T10:00:00+00:00',
            'updatedAt': '2026-08-18T10:00:00+00:00',
          }),
          200,
          headers: {'content-type': 'application/json'},
        );
      });
      final auth = _FakeAuthService('access-renewed');
      final client = EventApiClient(
        auth: auth,
        baseUrl: 'https://events.test',
        httpClient: mock,
      );
      final event = await client.get(_session('stale'), 1);
      expect(event.id, 1);
      expect(calls, 2);
      expect(auth.refreshCalls, greaterThanOrEqualTo(2));
      client.close();
    });

    test('401 重试后仍 401 抛 EventApiException', () async {
      final mock = MockClient(
        (_) async => http.Response(
          jsonEncode({'status': 401, 'title': 'expired'}),
          401,
          headers: {'content-type': 'application/problem+json'},
        ),
      );
      final auth = _FakeAuthService('access-renewed');
      final client = EventApiClient(
        auth: auth,
        baseUrl: 'https://events.test',
        httpClient: mock,
      );
      expect(
        () => client.get(_session('stale'), 1),
        throwsA(
          isA<EventApiException>().having((e) => e.status, 'status', 401),
        ),
      );
      client.close();
    });
  });

  // 防止引入未使用符号的 lint 报错
}
