import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/events/events_api.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';
import 'package:passingtrace_mobile/views/events_list_view.dart';

void main() {
  testWidgets('筛选按钮在时间线滚动后保持固定位置', (tester) async {
    final events = List.generate(
      30,
      (index) => {
        'id': index + 1,
        'kind': 0,
        'status': 1,
        'title': '第 ${index + 1} 条记录',
        'rawContent': '记录正文',
        'happenedAt':
            '2026-08-${(index % 28 + 1).toString().padLeft(2, '0')}T18:30:00+08:00',
        'plannedAt': null,
        'completedAt': null,
        'timezone': 'Asia/Shanghai',
        'sourceRevision': 1,
        'version': 1,
        'createdAt': '2026-08-31T18:30:00+08:00',
        'updatedAt': '2026-08-31T18:30:00+08:00',
        'media': <Object>[],
        'semanticStatus': null,
        'semanticSummary': null,
        'manualClassification': null,
        'effectiveClassification': null,
        'locations': <Object>[],
      },
    );
    final auth = _FakeAuthService();
    final api = EventApiClient(
      auth: auth,
      baseUrl: 'http://events.test',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/v1/event-taxonomy') {
          return http.Response('unavailable', 503);
        }
        return http.Response(
          jsonEncode({'items': events, 'nextCursor': null}),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
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
        home: EventsListView(auth: auth, session: session, eventApiClient: api),
      ),
    );
    await tester.pumpAndSettle();

    final filter = find.byKey(const Key('events-filter-button'));
    final before = tester.getTopLeft(filter);
    await tester.drag(find.byType(ListView), const Offset(0, -700));
    await tester.pumpAndSettle();
    final after = tester.getTopLeft(filter);

    expect(filter, findsOneWidget);
    expect(after, before);
    api.close();
  });
}

class _FakeAuthService extends AuthService {
  @override
  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async => current;
}
