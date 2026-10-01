import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/events/ai_api.dart';
import 'package:passingtrace_mobile/events/events_api.dart';
import 'package:passingtrace_mobile/events/location_service.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';
import 'package:passingtrace_mobile/views/assistant_approval_panel.dart';
import 'package:passingtrace_mobile/views/assistant_view.dart';

final approval = AiApprovalModel(
  id: 'approval-1',
  conversationId: 'conversation-1',
  targetType: 'Plan',
  targetId: '42',
  title: '周末跑步',
  description: '删除这条计划。',
  expiresAt: DateTime.now().add(const Duration(minutes: 15)),
);
const session = AuthSession(
  identityBaseUrl: 'https://id.test',
  deviceId: 'test',
  deviceSecret: 'test',
  accessToken: 'test',
);
final receipt = {
  'operationId': 'operation-1',
  'state': 'Succeeded',
  'message': {
    'id': 99,
    'role': 'Assistant',
    'content': '已创建：[Event #42]',
    'createdAt': '2026-09-30T12:00:00Z',
    'evidence': {
      'records': [
        {'eventId': 42, 'title': '周末跑步'},
      ],
      'memories': [],
    },
  },
};

class FakeAuth extends AuthService {
  @override
  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async => current;
}

class FakeAi extends AiApiClient {
  FakeAi() : super(auth: FakeAuth(), baseUrl: 'https://events.test');
  final conversation = AiConversationModel(
    id: 'conversation-1',
    title: '写入测试',
    updatedAt: DateTime.now(),
  );
  final other = AiConversationModel(
    id: 'conversation-2',
    title: '另一段对话',
    updatedAt: DateTime.now(),
  );
  List<AiApprovalModel> approvals = [approval];
  final List<String> decisions = [];
  final List<DeviceLocation?> sentLocations = [];
  Completer<AiMutationResultModel>? pending;
  Completer<List<AiApprovalModel>>? delayedApprovals;
  Completer<AiConversationDetailModel>? delayedOther;
  @override
  Future<List<AiConversationModel>> listConversations(
    AuthSession session,
  ) async => [conversation, other];
  @override
  Future<AiConversationDetailModel> getConversation(
    AuthSession session,
    String id,
  ) async => id == other.id && delayedOther != null
      ? await delayedOther!.future
      : AiConversationDetailModel(
          conversation: id == conversation.id ? conversation : other,
          messages: [],
        );
  @override
  Future<List<AiApprovalModel>> listApprovals(
    AuthSession session,
    String id,
  ) async => id == conversation.id
      ? delayedApprovals == null
            ? approvals
            : await delayedApprovals!.future
      : [];
  @override
  Future<AiMutationResultModel> decideApproval(
    AuthSession session,
    String id,
    String approvalId,
    String decision,
  ) async {
    decisions.add(decision);
    return pending?.future ?? AiMutationResultModel.fromJson(receipt);
  }

  @override
  Stream<AssistantChunk> send(
    AuthSession session,
    String conversationId,
    String content, {
    DeviceLocation? location,
  }) async* {
    sentLocations.add(location);
    yield AssistantChunk('mutation-result', receipt);
    yield AssistantChunk('mutation-result', receipt);
    yield AssistantChunk('error', {'message': '回答中断'});
  }
}

void main() {
  const locationChannel = MethodChannel('passingtrace/amap_location');
  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(locationChannel, null);
  });

  void stubLocation({bool permission = true}) {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(locationChannel, (call) async {
          if (call.method == 'requestPermission') return permission;
          if (call.method == 'locateOnce') {
            return {
              'latitude': 30.12,
              'longitude': 120.13,
              'accuracyMeters': 25.0,
              'capturedAt': DateTime.now().millisecondsSinceEpoch,
              'coordinateSystem': 'GCJ02',
            };
          }
          return null;
        });
  }

  testWidgets('普通聊天不定位，明确授权后只将位置附加到下一次发送', (tester) async {
    stubLocation();
    final api = FakeAi()..approvals = [];
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: AssistantView(auth: FakeAuth(), session: session, apiClient: api),
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), '你好');
    await tester.tap(find.bySemanticsLabel('发送'));
    await tester.pumpAndSettle();
    expect(api.sentLocations, [null]);
    await tester.tap(find.text('使用当前位置'));
    await tester.pumpAndSettle();
    expect(find.textContaining('仅随下一条消息发送给 AI'), findsOneWidget);
    await tester.tap(find.text('同意并定位'));
    await tester.pumpAndSettle();
    expect(find.textContaining('精度约 25 米'), findsOneWidget);
    await tester.enterText(find.byType(TextField), '从这里出发');
    await tester.tap(find.bySemanticsLabel('发送'));
    await tester.pumpAndSettle();
    expect(api.sentLocations.last?.latitude, 30.12);
    expect(api.sentLocations.last?.coordinateSystem, 'GCJ02');
    expect(find.textContaining('已附加当前位置'), findsNothing);
    await tester.enterText(find.byType(TextField), '继续');
    await tester.tap(find.bySemanticsLabel('发送'));
    await tester.pumpAndSettle();
    expect(api.sentLocations.last, isNull);
  });

  testWidgets('取消隐私提示和拒绝系统权限都不附加位置，也保留输入', (tester) async {
    final calls = <String>[];
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(locationChannel, (call) async {
          calls.add(call.method);
          return false;
        });
    final api = FakeAi()..approvals = [];
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: AssistantView(auth: FakeAuth(), session: session, apiClient: api),
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), '从家里出发');
    await tester.tap(find.text('使用当前位置'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(calls, isEmpty);
    await tester.tap(find.text('使用当前位置'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('同意并定位'));
    await tester.pumpAndSettle();
    expect(calls, ['requestPermission']);
    expect(find.textContaining('未授予前台定位权限'), findsOneWidget);
    expect(
      tester.widget<TextField>(find.byType(TextField)).controller!.text,
      '从家里出发',
    );
    await tester.tap(find.bySemanticsLabel('发送'));
    await tester.pumpAndSettle();
    expect(api.sentLocations, [null]);
  });

  testWidgets('切换对话后丢弃迟到的定位结果', (tester) async {
    final pending = Completer<Map<String, dynamic>>();
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(locationChannel, (call) async {
          if (call.method == 'requestPermission') return true;
          if (call.method == 'locateOnce') return pending.future;
          return null;
        });
    final api = FakeAi()..approvals = [];
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: AssistantView(auth: FakeAuth(), session: session, apiClient: api),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('使用当前位置'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('同意并定位'));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('聊天记录'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('另一段对话'));
    await tester.pumpAndSettle();
    pending.complete({
      'latitude': 30.12,
      'longitude': 120.13,
      'accuracyMeters': 25.0,
      'capturedAt': DateTime.now().millisecondsSinceEpoch,
    });
    await tester.pumpAndSettle();
    expect(find.textContaining('已附加当前位置'), findsNothing);
    await tester.enterText(find.byType(TextField), '继续');
    await tester.tap(find.bySemanticsLabel('发送'));
    await tester.pumpAndSettle();
    expect(api.sentLocations, [null]);
  });

  test('定位 API 传递明确坐标系，普通消息不包含定位字段', () async {
    final bodies = <Map<String, dynamic>>[];
    final api = AiApiClient(
      auth: FakeAuth(),
      baseUrl: 'https://events.test',
      httpClient: MockClient((request) async {
        bodies.add(jsonDecode(request.body) as Map<String, dynamic>);
        return http.Response('event: done\ndata: {}\n\n', 200);
      }),
    );
    final location = DeviceLocation(
      latitude: 30,
      longitude: 120,
      accuracyMeters: 30,
      capturedAt: DateTime.now(),
      coordinateSystem: 'WGS84',
    );
    await api.send(session, 'chat', '从这里出发', location: location).toList();
    await api.send(session, 'chat', '继续').toList();
    expect(bodies.first['location'], location.toAssistantJson());
    expect(bodies.last.containsKey('location'), isFalse);
    api.close();
  });

  test('授权 API 仅提交决定，解析实际回执与目标链接', () async {
    final api = AiApiClient(
      auth: FakeAuth(),
      baseUrl: 'https://events.test',
      httpClient: MockClient((request) async {
        expect(request.headers['Authorization'], 'Bearer test');
        expect(
          request.url.path,
          '/api/v1/ai/conversations/conversation-1/approvals/approval-1/decision',
        );
        expect(jsonDecode(request.body), {'decision': 'cancel'});
        return http.Response(
          jsonEncode({
            'id': 'approval-1',
            'state': 'Cancelled',
            'result': receipt,
          }),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      }),
    );
    final result = await api.decideApproval(
      session,
      'conversation-1',
      'approval-1',
      'cancel',
    );
    expect(result.message.id, 99);
    expect(result.message.evidenceRecords.single.eventId, 42);
    expect(result.message.content, contains('[Event #42]'));
    api.close();
  });

  test('授权接口错误提取具体原因，不向用户展示整段响应', () async {
    final api = AiApiClient(
      auth: FakeAuth(),
      baseUrl: 'https://events.test',
      httpClient: MockClient(
        (request) async => http.Response(
          jsonEncode({'title': '资源不存在', 'detail': '授权已失效，请重新申请。'}),
          404,
          headers: {'content-type': 'application/json; charset=utf-8'},
        ),
      ),
    );
    await expectLater(
      api.decideApproval(session, 'conversation-1', 'approval-1', 'cancel'),
      throwsA(
        isA<EventApiException>()
            .having((e) => e.status, 'status', 404)
            .having((e) => e.message, 'message', '授权已失效，请重新申请。'),
      ),
    );
    api.close();
  });

  testWidgets('授权显示具体对象，取消和确定分别回调，加载状态禁止重复操作', (tester) async {
    final decisions = <String>[];
    Future<void> show(bool busy) => tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: Scaffold(
          body: AssistantApprovalPanel(
            approval: approval,
            remaining: 2,
            busy: busy,
            onDecision: decisions.add,
            onOpen: () {},
          ),
        ),
      ),
    );
    await show(false);
    expect(find.text('确认删除计划？'), findsOneWidget);
    expect(find.text('周末跑步'), findsOneWidget);
    await tester.tap(find.text('取消'));
    await tester.tap(find.text('确定'));
    expect(decisions, ['cancel', 'confirm']);
    await show(true);
    expect(find.text('正在处理…'), findsOneWidget);
    expect(
      tester
          .widgetList<TextButton>(find.byType(TextButton))
          .every((button) => button.onPressed == null),
      isTrue,
    );
  });

  testWidgets('重新打开聊天恢复授权，确认中只发送一次，结果写入聊天', (tester) async {
    final api = FakeAi();
    api.pending = Completer<AiMutationResultModel>();
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: AssistantView(auth: FakeAuth(), session: session, apiClient: api),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(AssistantApprovalPanel), findsOneWidget);
    expect(api.decisions, isEmpty);
    await tester.tap(find.text('确定'));
    await tester.pump();
    expect(api.decisions, ['confirm']);
    expect(find.text('正在处理…'), findsOneWidget);
    api.pending!.complete(AiMutationResultModel.fromJson(receipt));
    await tester.pumpAndSettle();
    expect(find.byType(AssistantApprovalPanel), findsNothing);
    expect(find.textContaining('周末跑步'), findsWidgets);
  });

  testWidgets('模型随后失败，创建回执与链接仍显示且重复事件去重', (tester) async {
    final api = FakeAi()..approvals = [];
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: AssistantView(auth: FakeAuth(), session: session, apiClient: api),
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), '创建跑步计划');
    await tester.tap(find.bySemanticsLabel('发送'));
    await tester.pumpAndSettle();
    final messages = tester.widgetList<AssistantMessageContent>(
      find.byType(AssistantMessageContent),
    );
    expect(
      messages.where((message) => message.text.startsWith('已创建')).length,
      1,
    );
    expect(
      messages
          .singleWhere((message) => message.text.startsWith('已创建'))
          .eventTitles[42],
      '周末跑步',
    );
    expect(find.text('回答中断'), findsOneWidget);
  });

  testWidgets('小屏、键盘和放大字体下授权与输入框没有溢出', (tester) async {
    tester.view.physicalSize = const Size(375, 750);
    tester.view.devicePixelRatio = 1;
    tester.view.viewInsets = const FakeViewPadding(bottom: 280);
    addTearDown(tester.view.reset);
    final api = FakeAi();
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(1.5)),
          child: child!,
        ),
        home: AssistantView(auth: FakeAuth(), session: session, apiClient: api),
      ),
    );
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.byType(AssistantApprovalPanel), findsOneWidget);
    expect(find.byType(TextField), findsOneWidget);
  });

  testWidgets('切换会话清除旧授权，重新打开原会话恢复且可取消', (tester) async {
    final api = FakeAi();
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: AssistantView(auth: FakeAuth(), session: session, apiClient: api),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('聊天记录'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('另一段对话'));
    await tester.pumpAndSettle();
    expect(find.byType(AssistantApprovalPanel), findsNothing);
    await tester.tap(find.byTooltip('聊天记录'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('写入测试'));
    await tester.pumpAndSettle();
    expect(find.byType(AssistantApprovalPanel), findsOneWidget);
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(api.decisions, ['cancel']);
    expect(find.byType(AssistantApprovalPanel), findsNothing);
  });

  testWidgets('切换会话期间晚到的旧授权不会显示或允许确认', (tester) async {
    final api = FakeAi();
    api.delayedApprovals = Completer<List<AiApprovalModel>>();
    api.delayedOther = Completer<AiConversationDetailModel>();
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: AssistantView(auth: FakeAuth(), session: session, apiClient: api),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('聊天记录'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('另一段对话'));
    await tester.pump();
    api.delayedApprovals!.complete([approval]);
    await tester.pump();
    expect(find.byType(AssistantApprovalPanel), findsNothing);
    api.delayedOther!.complete(
      AiConversationDetailModel(conversation: api.other, messages: []),
    );
    await tester.pumpAndSettle();
    expect(find.byType(AssistantApprovalPanel), findsNothing);
    expect(api.decisions, isEmpty);
  });
}
