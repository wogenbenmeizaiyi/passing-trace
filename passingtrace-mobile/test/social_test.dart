import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/social/social_api.dart';
import 'package:passingtrace_mobile/social/social_widgets.dart';
import 'package:passingtrace_mobile/social/messages_view.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';
import 'package:passingtrace_mobile/views/assistant_view.dart';

class _Auth extends AuthService {
  int refreshes = 0;
  @override
  Future<String> getEventsApiBaseUrl() async => 'https://events.test';
  @override
  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async {
    if (forceRefresh) refreshes++;
    return current;
  }
}

const session = AuthSession(
  identityBaseUrl: 'https://identity.test',
  deviceId: 'test',
  deviceSecret: 'test',
  accessToken: 'test-token',
);
http.Response json(Object? data) => http.Response(
  jsonEncode(data),
  200,
  headers: {'content-type': 'application/json; charset=utf-8'},
);

class _Feed extends SocialFeed {
  _Feed(super.auth, super.session);
  @override
  void start() {}
  void changed() {
    revision++;
    notifyListeners();
  }
}

void main() {
  test('好友 API 使用认证资料接口，401 只续期一次，消息重试保留标识', () async {
    final auth = _Auth();
    final id = messageKey();
    var attempts = 0;
    final api = SocialApi(
      auth,
      session,
      client: MockClient((r) async {
        expect(r.headers['Authorization'], 'Bearer test-token');
        expect(r.url.host, 'events.test');
        expect(jsonDecode(r.body)['clientMessageId'], id);
        if (++attempts == 1) return http.Response('', 401);
        return json({'id': 12});
      }),
    );
    final result = await api.request(
      'POST',
      '/api/v1/conversations/test/messages',
      body: {'clientMessageId': id, 'kind': 'text', 'text': '你好'},
    );
    expect(result['id'], 12);
    expect(auth.refreshes, 1);
    expect(attempts, 2);
    expect(
      id,
      matches(
        RegExp(
          r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
        ),
      ),
    );
    api.close();
  });

  testWidgets('聊天只请求当前页，失败重试不会新建消息，跨设备清除同步', (tester) async {
    final auth = _Auth();
    final feed = _Feed(auth, session);
    final requests = <Uri>[];
    final sent = <String>[];
    var cleared = false;
    final api = SocialApi(
      auth,
      session,
      client: MockClient((r) async {
        requests.add(r.url);
        if (r.method == 'POST') {
          sent.add(jsonDecode(r.body)['clientMessageId'] as String);
          if (sent.length == 1) return http.Response('', 503);
          return json({
            'id': 32,
            'senderId': '1',
            'kind': 'text',
            'text': '你好',
            'shareId': null,
            'createdAt': '2026-09-22T08:00:00Z',
          });
        }
        if (r.method == 'PUT') return http.Response('', 204);
        if (r.url.path.endsWith('/messages')) {
          expect(r.url.queryParameters['limit'], '30');
          return json({
            'items': r.url.queryParameters.containsKey('after') || cleared
                ? []
                : [
                    {
                      'id': 31,
                      'senderId': '2',
                      'kind': 'text',
                      'text': '最近消息',
                      'shareId': null,
                      'createdAt': '2026-09-22T08:00:00Z',
                    },
                  ],
            'nextCursor': null,
          });
        }
        return json({
          'id': 'chat',
          'friendshipId': 'friend',
          'person': {'nickname': '小王'},
          'peerReadThroughId': 0,
          'canSend': true,
          'clearedThroughId': cleared ? 32 : 0,
        });
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: DirectChatView(
          api: api,
          conversationId: 'chat',
          me: '1',
          feed: feed,
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('最近消息'), findsOneWidget);
    expect(requests.where((r) => r.path.endsWith('/messages')), hasLength(1));
    await tester.enterText(find.byType(TextField), '你好');
    await tester.tap(find.byTooltip('发送'));
    await tester.pumpAndSettle();
    expect(find.text('你好'), findsOneWidget);
    await tester.tap(find.text('重试'));
    await tester.pumpAndSettle();
    expect(sent, hasLength(2));
    expect(sent[0], sent[1]);
    cleared = true;
    await tester.runAsync(() async {
      feed.changed();
      await Future<void>.delayed(const Duration(milliseconds: 100));
    });
    await tester.pumpAndSettle();
    expect(find.text('最近消息'), findsNothing);
    await tester.pumpWidget(const SizedBox.shrink());
    feed.dispose();
    api.close();
  });

  for (final dark in [false, true]) {
    testWidgets('参与者选择在${dark ? '深色' : '浅色'}大字体下保存真实好友关联', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final api = SocialApi(
        _Auth(),
        session,
        client: MockClient(
          (r) async => json([
            {
              'id': 'friend',
              'person': {'id': '2', 'nickname': '小王', 'hasAvatar': false},
              'remark': '同学',
            },
          ]),
        ),
      );
      List<String>? selected;
      await tester.pumpWidget(
        MaterialApp(
          theme: dark
              ? PassingTraceTheme.dark(PassingTracePalette.pine)
              : PassingTraceTheme.light(PassingTracePalette.pine),
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context)
                .copyWith(textScaler: const TextScaler.linear(1.5)),
            child: child!,
          ),
          home: Scaffold(
            body: Builder(
              builder: (context) => TextButton(
                onPressed: () async {
                  selected = await pickParticipants(context, api, []);
                },
                child: const Text('添加'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('添加'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('同学'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('完成'));
      await tester.pumpAndSettle();
      expect(selected, ['2']);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      api.close();
    });
  }
  testWidgets('AI 只把证据内的好友和分享变成链接', (tester) async {
    const id = '11111111-1111-4111-8111-111111111111';
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: const Scaffold(
          body: AssistantMessageContent(
            text: '[Friend #$id] [Share #$id]',
            isUser: false,
            socialTitles: {'friend/$id': '小王'},
          ),
        ),
      ),
    );
    expect(find.textContaining('内容已不可查看', findRichText: true), findsWidgets);
    expect(find.textContaining('小王', findRichText: true), findsWidgets);
    expect(find.textContaining('[Friend #', findRichText: true), findsNothing);
  });
}
