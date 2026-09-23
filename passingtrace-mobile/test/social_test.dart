import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/social/social_api.dart';
import 'package:passingtrace_mobile/social/social_widgets.dart';
import 'package:passingtrace_mobile/social/messages_view.dart';
import 'package:passingtrace_mobile/social/friends_view.dart';
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

class _ConnectionApi extends SocialApi {
  _ConnectionApi(super.auth, super.session);
  ValueChanged<bool>? connection;
  final events = StreamController<int>();
  @override
  Stream<int> changes({int after = 0, ValueChanged<bool>? onConnection}) {
    connection = onConnection;
    return events.stream;
  }

  @override
  Future<dynamic> request(
    String method,
    String path, {
    Object? body,
    bool identity = false,
  }) async => {'count': 2};
  @override
  void close() {
    events.close();
    super.close();
  }
}

void main() {
  testWidgets('消息页只加载摘要，联系人独立进入，返回恢复消息列表', (tester) async {
    FlutterSecureStorage.setMockInitialValues({});
    final paths = <String>[];
    final auth = _Auth();
    final feed = _Feed(auth, session);
    final api = SocialApi(
      auth,
      session,
      client: MockClient((r) async {
        paths.add('${r.method} ${r.url.path}?${r.url.query}');
        if (r.url.path == '/api/v1/conversations') {
          return json({'items': [], 'nextCursor': null});
        }
        if (r.url.path == '/api/v1/people/me') {
          return json({
            'profile': {'id': '1', 'nickname': '我', 'hasAvatar': false},
          });
        }
        const notice = {
          'id': 9,
          'kind': 'friend-request',
          'text': '小王想添加你为好友',
          'createdAt': '2026-09-22T10:00:00',
        };
        if (r.url.path == '/api/v1/notifications/summary') {
          return json({'unreadCount': 1, 'latest': notice});
        }
        if (r.url.path == '/api/v1/notifications') return json([notice]);
        return json([]);
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: MessagesView(auth: auth, session: session, feed: feed, api: api),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('好友通知'), findsOneWidget);
    expect(find.byTooltip('提醒'), findsNothing);
    expect(paths.any((p) => p.contains('/messages')), isFalse);
    await tester.tap(find.text('好友通知'));
    await tester.pumpAndSettle();
    expect(paths, contains('GET /api/v1/notifications?visibleOnly=true'));
    expect(paths, contains('PUT /api/v1/notifications/read?visibleOnly=true'));
    await tester.pageBack();
    await tester.pumpAndSettle();
    expect(find.byType(SegmentedButton<int>), findsNothing);
    expect(find.byTooltip('搜索会话'), findsOneWidget);
    expect(find.byTooltip('添加'), findsOneWidget);
    await tester.tap(find.byTooltip('联系人'));
    await tester.pumpAndSettle();
    expect(find.text('好友'), findsOneWidget);
    expect(find.text('好友通知'), findsNothing);
    expect(find.text('还没有聊天，和好友聊聊共同的生活吧。'), findsNothing);
    await tester.pageBack();
    await tester.pumpAndSettle();
    expect(find.text('好友通知'), findsOneWidget);
    await tester.tap(find.byTooltip('添加'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('添加好友'));
    await tester.pumpAndSettle();
    expect(find.text('发送申请'), findsOneWidget);
    expect(find.text('好友通知'), findsNothing);
    await tester.pumpWidget(const SizedBox());
    feed.dispose();
  });
  for (final dark in [false, true]) {
    testWidgets('紧凑工具栏和按需搜索在小屏大字体${dark ? '深色' : '浅色'}可用', (tester) async {
      tester.view.physicalSize = const Size(640, 1400);
      tester.view.devicePixelRatio = 2;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final auth = _Auth();
      final feed = _Feed(auth, session);
      final paths = <String>[];
      final api = SocialApi(
        auth,
        session,
        client: MockClient((r) async {
          paths.add(r.url.path);
          if (r.url.path == '/api/v1/people/me') {
            return json({
              'profile': {'id': 'me', 'nickname': '我'},
            });
          }
          if (r.url.path == '/api/v1/notifications/summary') {
            return json({'latest': null, 'unreadCount': 0});
          }
          return json({
            'items': [
              {
                'id': '1',
                'person': {
                  'id': 'friend',
                  'nickname': '小王',
                  'hasAvatar': false,
                },
                'preview': '一起散步',
                'unreadCount': 0,
                'updatedAt': '2026-09-23T12:00:00Z',
              },
            ],
            'nextCursor': 'next',
          });
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          theme: dark
              ? PassingTraceTheme.dark(PassingTracePalette.pine)
              : PassingTraceTheme.light(PassingTracePalette.pine),
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context)
                .copyWith(textScaler: TextScaler.linear(1.6)),
            child: child!,
          ),
          home: MessagesView(
            auth: auth,
            session: session,
            feed: feed,
            api: api,
            drawer: const Drawer(),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byType(TextField), findsNothing);
      expect(find.text('小王'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.tap(find.byTooltip('搜索会话'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), '不存在');
      await tester.pumpAndSettle();
      expect(find.text('小王'), findsNothing);
      expect(find.text('已加载的会话中没有匹配结果。'), findsOneWidget);
      expect(find.text('加载更多'), findsOneWidget);
      await tester.tap(find.byTooltip('关闭搜索'));
      await tester.pumpAndSettle();
      expect(find.text('小王'), findsOneWidget);
      expect(find.byType(TextField), findsNothing);
      expect(
        paths.any((p) => p.contains('/messages') || p.contains('/friends')),
        isFalse,
      );
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      feed.dispose();
    });
  }
  testWidgets('断线十秒才显示同步提示，恢复不丢未读或要求退出', (tester) async {
    final auth = _Auth(), api = _ConnectionApi(_Auth(), session);
    final feed = SocialFeed(auth, session, apiFactory: () => api)..start();
    await tester.pump(const Duration(seconds: 9));
    expect(feed.reconnecting, isFalse);
    await tester.pump(const Duration(seconds: 1));
    expect(feed.reconnecting, isTrue);
    api.connection!(true);
    expect(feed.reconnecting, isFalse);
    expect(feed.unread, 2);
    feed.dispose();
    await tester.pump();
  });
  test('时间分隔只在首条、跨日或超过五分钟出现', () {
    SocialRow at(String date) => {'createdAt': date};
    expect(showSocialMessageTime(at('2026-09-22T10:00:00'), null), isTrue);
    expect(
      showSocialMessageTime(
        at('2026-09-22T10:05:00'),
        at('2026-09-22T10:00:00'),
      ),
      isFalse,
    );
    expect(
      showSocialMessageTime(
        at('2026-09-22T10:05:01'),
        at('2026-09-22T10:00:00'),
      ),
      isTrue,
    );
    expect(
      showSocialMessageTime(
        at('2026-09-23T00:01:00'),
        at('2026-09-22T23:59:00'),
      ),
      isTrue,
    );
  });
  for (final dark in [false, true]) {
    testWidgets('好友分组、搜索与独立添加页面在大字体${dark ? '深色' : '浅色'}下可用', (tester) async {
      FlutterSecureStorage.setMockInitialValues({});
      tester.view.resetPhysicalSize();
      tester.view.physicalSize = const Size(750, 1500);
      tester.view.devicePixelRatio = 2;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      var sent = 0;
      final api = SocialApi(
        _Auth(),
        session,
        client: MockClient((r) async {
          if (r.url.path == '/api/v1/people/me') {
            return json({
              'profile': {'id': '1'},
            });
          }
          if (r.url.path == '/api/v1/friends') {
            return json([
              {
                'id': 'friend',
                'person': {'id': '2', 'nickname': '王小明', 'hasAvatar': false},
                'remark': '小王',
                'label': '同事',
                'relationship': '恋人',
              },
            ]);
          }
          if (r.method == 'POST' && r.url.path == '/api/v1/friend-requests') {
            sent++;
            return json({'id': 'request'});
          }
          return json([]);
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          theme: ThemeData(
            brightness: dark ? Brightness.dark : Brightness.light,
          ),
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context)
                .copyWith(textScaler: const TextScaler.linear(1.6)),
            child: child!,
          ),
          home: FriendsView(api: api, onChat: (_) {}),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('小王'), findsOneWidget);
      expect(find.text('发送申请'), findsNothing);
      await tester.tap(find.byKey(const ValueKey('group:同事')));
      await tester.pumpAndSettle();
      expect(find.text('小王'), findsNothing);
      await tester.enterText(find.byType(TextField), '恋人');
      await tester.pumpAndSettle();
      expect(find.text('小王'), findsOneWidget);
      await tester.enterText(find.byType(TextField), '');
      await tester.pumpAndSettle();
      expect(find.text('小王'), findsNothing);
      await tester.tap(find.byTooltip('添加好友'));
      await tester.pumpAndSettle();
      expect(find.text('发送申请'), findsOneWidget);
      expect(find.text('扫码添加'), findsOneWidget);
      await tester.enterText(find.byType(TextField), 'FRIENDCODE');
      await tester.pump();
      await tester.tap(find.text('发送申请'));
      await tester.pumpAndSettle();
      expect(sent, 1);
      expect(find.textContaining('好友申请已发送'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pageBack();
      await tester.pumpAndSettle();
      expect(find.text('小王'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      api.close();
    });
  }
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
