import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/events/ai_api.dart';

class _FakeAuthService extends AuthService {
  @override
  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async => current;
}

AuthSession _session() => AuthSession(
  identityBaseUrl: 'https://id.test',
  deviceId: 'device-1',
  deviceSecret: 'secret',
  accessToken: 'access-token',
  accessTokenExpiration: DateTime.now().add(const Duration(minutes: 10)),
);

void main() {
  test('列出会话并加载历史消息与证据', () async {
    final client = MockClient((request) async {
      expect(request.headers['Authorization'], 'Bearer access-token');
      if (request.url.path == '/api/v1/ai/conversations') {
        return http.Response(
          jsonEncode([
            {
              'id': 'conversation-1',
              'title': '七月回顾',
              'createdAt': '2026-08-31T08:00:00Z',
              'updatedAt': '2026-08-31T09:00:00Z',
            },
          ]),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      }
      if (request.url.path == '/api/v1/ai/conversations/conversation-1') {
        return http.Response(
          jsonEncode({
            'id': 'conversation-1',
            'title': '七月回顾',
            'createdAt': '2026-08-31T08:00:00Z',
            'updatedAt': '2026-08-31T09:00:00Z',
            'messages': [
              {
                'id': 1,
                'role': 'User',
                'content': '我去过哪里？',
                'createdAt': '2026-08-31T08:01:00Z',
                'evidence': null,
              },
              {
                'id': 2,
                'role': 'Assistant',
                'content': '你去过杭州。',
                'createdAt': '2026-08-31T08:01:01Z',
                'evidence': {
                  'records': [
                    {'eventId': 42, 'title': '西湖散步'},
                  ],
                },
              },
            ],
          }),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      }
      return http.Response('unexpected', 500);
    });
    final api = AiApiClient(
      auth: _FakeAuthService(),
      baseUrl: 'https://events.test',
      httpClient: client,
    );

    final conversations = await api.listConversations(_session());
    final detail = await api.getConversation(
      _session(),
      conversations.single.id,
    );

    expect(conversations.single.title, '七月回顾');
    expect(detail.messages.map((message) => message.role), [
      'user',
      'assistant',
    ]);
    expect(detail.messages.last.evidenceEventIds, [42]);
    expect(detail.messages.last.evidenceRecords.single.title, '西湖散步');
    api.close();
  });

  test('删除会话接受 204', () async {
    final client = MockClient((request) async {
      expect(request.method, 'DELETE');
      return http.Response('', 204);
    });
    final api = AiApiClient(
      auth: _FakeAuthService(),
      baseUrl: 'https://events.test',
      httpClient: client,
    );

    await api.deleteConversation(_session(), 'conversation-1');
    api.close();
  });

  test('实时证据兼容服务端 PascalCase 并使用记录标题', () async {
    final client = MockClient(
      (_) async => http.Response(
        'event: evidence\n'
        'data: {"Records":[{"EventId":13,"Title":"整理项目下一阶段计划","Snippet":"阶段计划"}]}\n\n'
        'event: done\n'
        'data: {}\n\n',
        200,
        headers: {'content-type': 'text/event-stream; charset=utf-8'},
      ),
    );
    final api = AiApiClient(
      auth: _FakeAuthService(),
      baseUrl: 'https://events.test',
      httpClient: client,
    );

    final chunks = await api
        .send(_session(), 'conversation-1', '总结这个月')
        .toList();
    final evidence = chunks.singleWhere((chunk) => chunk.type == 'evidence');
    final records = AiEvidenceRecord.fromEnvelope(
      evidence.data as Map<String, dynamic>,
    );

    expect(records.single.eventId, 13);
    expect(records.single.displayTitle, '整理项目下一阶段计划');
    api.close();
  });

  test('恢复会话时只接受可信的高德地点与动作', () async {
    final message = AiMessageModel.fromJson({
      'role': 'Assistant',
      'content': '为你找到一个地点。',
      'evidence': {
        'amapPlaces': [
          {
            'candidateId': 'p1',
            'poiId': 'poi-1',
            'name': '人民广场地铁站',
            'address': '上海市黄浦区',
            'latitude': 31.232,
            'longitude': 121.475,
          },
          {
            'candidateId': 'bad',
            'name': '无效地点',
            'latitude': 999,
            'longitude': 999,
          },
        ],
        'actions': [
          {
            'type': 'amap-navigation',
            'provider': 'amap',
            'label': '高德导航',
            'placeName': '人民广场地铁站',
            'latitude': 31.232,
            'longitude': 121.475,
            'coordinateSystem': 'GCJ02',
            'source': 'amap-live',
          },
          {
            'type': 'amap-trip-map',
            'provider': 'amap',
            'label': '打开地图',
            'placeName': '伪造地图',
            'latitude': 0,
            'longitude': 0,
            'coordinateSystem': 'GCJ02',
            'source': 'amap-live',
            'webUrl': 'https://evil.example/trip',
          },
        ],
      },
    });

    expect(message.amapPlaces, hasLength(1));
    expect(message.amapPlaces.single.name, '人民广场地铁站');
    expect(message.actions, hasLength(1));
    expect(message.actions.single.type, 'amap-navigation');
  });
}
