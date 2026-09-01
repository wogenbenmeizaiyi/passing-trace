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

  test('正式版在 App 内下载并校验安装包', () async {
    Map<String, Object?>? request;
    final service = AppUpdateService(
      installer: (value) async => request = value,
      environment: production,
    );
    final update = AppUpdateInfo(
      updateAvailable: true,
      required: false,
      versionName: '1.0.4',
      versionCode: 7,
      publishedAt: DateTime.utc(2026, 9, 1),
      sha256: List.filled(64, 'a').join(),
      size: 1024,
      notes: '更新',
      downloadUrl: Uri.parse('https://example.com/PassingTrace.apk'),
    );

    await service.download(update);

    expect(request?['url'], update.downloadUrl.toString());
    expect(request?['versionCode'], 7);
    expect(request?['sha256'], update.sha256);
    expect(request?['size'], 1024);
  });
}
