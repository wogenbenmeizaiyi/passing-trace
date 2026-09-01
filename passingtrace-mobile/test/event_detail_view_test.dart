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

  testWidgets('图片附件进入详情页后自动请求预览且不显示文件名卡片', (tester) async {
    var accessRequested = false;
    final responseBody = jsonEncode({
      'id': 43,
      'kind': 0,
      'status': 1,
      'title': '东北菜',
      'rawContent': '和朋友一起吃晚饭。',
      'happenedAt': '2026-08-31T18:30:00+08:00',
      'plannedAt': null,
      'completedAt': null,
      'timezone': 'Asia/Shanghai',
      'visibility': 0,
      'sourceRevision': 1,
      'version': 1,
      'createdAt': '2026-08-31T18:30:00+08:00',
      'updatedAt': '2026-08-31T18:30:00+08:00',
      'media': [
        {
          'id': 'd0bc30f4-ee5a-4ded-8bed-7b34bf3e8d09',
          'fileName': 'Chinese-food-in-Harbin.jpg',
          'kind': 1,
          'contentType': 'image/jpeg',
          'size': 209000,
          'status': 4,
          'sortOrder': 0,
        },
      ],
      'semanticStatus': null,
      'semanticSummary': null,
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
          headers: {'content-type': 'application/json; charset=utf-8'},
        ),
      ),
    );
    final mediaApi = MediaApiClient(
      auth: auth,
      baseUrl: 'http://events.test',
      httpClient: MockClient((request) async {
        accessRequested = request.url.path.endsWith('/access');
        return http.Response(
          jsonEncode({
            'url': 'http://127.0.0.1:1/private-image.jpg',
            'expiresAt': '2026-08-31T19:00:00Z',
            'inline': true,
          }),
          200,
        );
      }),
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
          eventId: 43,
          eventApiClient: eventApi,
          mediaApiClient: mediaApi,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('附件'),
      300,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.pumpAndSettle();

    expect(accessRequested, isTrue);
    expect(find.text('Chinese-food-in-Harbin.jpg'), findsNothing);
    expect(
      find.bySemanticsLabel('查看图片 Chinese-food-in-Harbin.jpg'),
      findsOneWidget,
    );
  });
}

class _FakeAuthService extends AuthService {
  @override
  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async => current;
}
