import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/build_environment.dart';
import 'package:passingtrace_mobile/update_service.dart';

void main() {
  const production = BuildEnvironment(
    channel: 'production',
    identityUrl: 'https://auth.passingtrace.com',
    eventsApiUrl: 'https://passingtrace.com',
    allowEndpointOverrides: false,
  );

  test('正式版按 versionCode 查询最新更新', () async {
    late Uri requested;
    final client = MockClient((request) async {
      requested = request.url;
      return http.Response.bytes(
        utf8.encode(
          jsonEncode({
            'updateAvailable': true,
            'required': false,
            'versionName': '1.1.0',
            'versionCode': 4,
            'publishedAt': '2026-09-01T08:00:00Z',
            'sha256': List.filled(64, 'a').join(),
            'size': 1024,
            'notes': '新版本',
            'downloadUrl': 'https://passingtrace.cn-nb1.rains3.com/signed.apk',
          }),
        ),
        200,
        headers: {'content-type': 'application/json; charset=utf-8'},
      );
    });
    final service = AppUpdateService(
      httpClient: client,
      environment: production,
    );

    final update = await service.check(currentVersionCode: 3);

    expect(requested.path, '/api/v1/app-updates/android/latest');
    expect(requested.queryParameters['currentVersionCode'], '3');
    expect(update?.versionCode, 4);
    expect(update?.updateAvailable, isTrue);
  });

  test('内测版不请求公网更新', () async {
    final client = MockClient((_) async => throw StateError('不应发起请求'));
    final service = AppUpdateService(httpClient: client);

    expect(await service.check(currentVersionCode: 1), isNull);
  });
}
