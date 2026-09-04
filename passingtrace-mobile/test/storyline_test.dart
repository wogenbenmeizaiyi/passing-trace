import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/storylines/storyline_api.dart';
import 'package:passingtrace_mobile/storylines/storyline_model.dart';

class _StorylineAuth extends AuthService {
  @override
  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async => AuthSession(
    identityBaseUrl: current.identityBaseUrl,
    deviceId: current.deviceId,
    deviceSecret: current.deviceSecret,
    accessToken: 'fresh-token',
  );
}

AuthSession _session() => const AuthSession(
  identityBaseUrl: 'https://id.test',
  deviceId: 'device',
  deviceSecret: 'secret',
  accessToken: 'old-token',
);

Map<String, dynamic> _detailJson() => {
  'id': 'story-1',
  'title': '黄山旅行',
  'description': '从购票到登顶',
  'categoryKey': 'trip',
  'categoryLabel': '行程旅行',
  'status': 1,
  'revision': 3,
  'version': 42,
  'rangeStart': '2026-08-01T08:00:00Z',
  'rangeEnd': '2026-08-03T18:00:00Z',
  'layoutState': 2,
  'tags': ['登山'],
  'stages': [
    {'key': 'stage-1', 'title': '出发', 'semanticOrder': 0},
  ],
  'nodes': [
    {
      'key': 'node-1',
      'eventId': 7,
      'sourceRevision': 1,
      'currentSourceRevision': 2,
      'revisionState': 'updated',
      'kind': 0,
      'status': 1,
      'title': '购买车票',
      'rawContent': '买好了去黄山的票',
      'occurredAt': '2026-08-01T08:00:00Z',
      'stageKey': 'stage-1',
      'semanticOrder': 0,
      'emphasis': 1,
      'place': '黄山北站',
      'tags': ['交通'],
      'imageMediaAssetId': 'media-1',
    },
  ],
  'outline': [
    {
      'nodeKey': 'node-1',
      'stageKey': 'stage-1',
      'topologicalOrder': 0,
      'depth': 1,
      'incomingCount': 2,
      'outgoingCount': 1,
      'startsBranch': true,
      'isMerge': true,
    },
  ],
  'updatedAt': '2026-08-04T00:00:00Z',
};

void main() {
  test('故事线详情保留固定修订、分支汇合与图片信息', () {
    final value = StorylineDetailModel.fromJson(_detailJson());

    expect(value.title, '黄山旅行');
    expect(value.nodes.single.sourceRevision, 1);
    expect(value.nodes.single.currentSourceRevision, 2);
    expect(value.nodes.single.revisionState, 'updated');
    expect(value.nodes.single.imageMediaAssetId, 'media-1');
    expect(value.outline.single.startsBranch, isTrue);
    expect(value.outline.single.isMerge, isTrue);
  });

  test('手机增量操作发送并发令牌与幂等键', () async {
    late http.Request captured;
    final client = StorylineApiClient(
      auth: _StorylineAuth(),
      baseUrl: 'https://events.test',
      httpClient: MockClient((request) async {
        captured = request;
        return http.Response(
          jsonEncode({'storyline': _detailJson(), 'undoRevision': 2}),
          200,
          headers: {'content-type': 'application/json'},
        );
      }),
    );

    final result = await client.change(_session(), 'story-1', 42, {
      'operation': 'sync-node',
      'nodeKey': 'node-1',
    }, 'change-key');

    expect(captured.url.path, '/api/v1/storylines/story-1/changes');
    expect(captured.headers['if-match'], '42');
    expect(captured.headers['idempotency-key'], 'change-key');
    expect(captured.headers['authorization'], 'Bearer fresh-token');
    expect(result.undoRevision, 2);
    client.close();
  });
}
