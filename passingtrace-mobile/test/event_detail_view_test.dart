import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/events/events_api.dart';
import 'package:passingtrace_mobile/events/media_api.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';
import 'package:passingtrace_mobile/views/event_detail_view.dart';

void main() {
  testWidgets('记录详情默认隐藏 AI 分析，点击标题旁图标后展开', (tester) async {
    final responseBody = jsonEncode({
      'id': 42,
      'kind': 0,
      'status': 1,
      'title': '西湖散步',
      'rawContent': '傍晚沿着湖边走了一圈。',
      'happenedAt': '2026-08-31T18:30:00+08:00',
      'plannedAt': null,
      'completedAt': null,
      'timezone': 'Asia/Shanghai',
      'visibility': 0,
      'sourceRevision': 1,
      'version': 1,
      'createdAt': '2026-08-31T18:30:00+08:00',
      'updatedAt': '2026-08-31T18:30:00+08:00',
      'media': <Object>[],
      'semanticStatus': 'Completed',
      'semanticSummary': '这是一次傍晚的轻松散步。',
      'manualClassification': {
        'primaryCategoryKey': null,
        'tags': <Object>[],
        'suppressedAiTagKeys': <String>[],
      },
      'effectiveClassification': {
        'primaryCategory': null,
        'tags': <Object>[],
        'taxonomyVersion': 'life-v1',
      },
      'locations': <Object>[],
    });
    final auth = _FakeAuthService();
    final eventApi = EventApiClient(
      auth: auth,
      baseUrl: 'http://events.test',
      httpClient: MockClient(
        (_) async => http.Response(
          responseBody,
          200,
          headers: {'content-type': 'application/json'},
        ),
      ),
    );
    final mediaApi = MediaApiClient(
      auth: auth,
      baseUrl: 'http://events.test',
      httpClient: MockClient((_) async => http.Response('{}', 200)),
    );
    final session = AuthSession(
      identityBaseUrl: 'http://identity.test',
      deviceId: 'device',
      deviceSecret: 'secret',
      accessToken: 'token',
      accessTokenExpiration: DateTime.now().toUtc().add(
        const Duration(hours: 1),
      ),
    );

    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: EventDetailView(
          auth: auth,
          session: session,
          eventId: 42,
          eventApiClient: eventApi,
          mediaApiClient: mediaApi,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('这是一次傍晚的轻松散步。'), findsNothing);
    expect(find.byTooltip('查看 AI 分析'), findsOneWidget);

    await tester.tap(find.byTooltip('查看 AI 分析'));
    await tester.pump();

    expect(find.text('AI 分析'), findsOneWidget);
    expect(find.text('这是一次傍晚的轻松散步。'), findsOneWidget);
    expect(find.byTooltip('收起 AI 分析'), findsOneWidget);

    await tester.tap(find.byTooltip('收起 AI 分析'));
    await tester.pump();

    expect(find.text('这是一次傍晚的轻松散步。'), findsNothing);
  });
}

class _FakeAuthService extends AuthService {
  @override
  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async => current;
}
