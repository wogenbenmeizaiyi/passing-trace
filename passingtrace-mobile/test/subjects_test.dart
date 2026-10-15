import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/profile/account_avatar.dart';
import 'package:passingtrace_mobile/events/events_api.dart';
import 'package:passingtrace_mobile/events/ai_api.dart';
import 'package:passingtrace_mobile/subjects/subject_model.dart';
import 'package:passingtrace_mobile/subjects/subjects_api.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_layout.dart';
import 'package:passingtrace_mobile/subjects/subjects_view.dart';
import 'package:passingtrace_mobile/subjects/subject_detail_view.dart';
import 'package:passingtrace_mobile/subjects/subject_entry_view.dart';
import 'package:passingtrace_mobile/subjects/subject_fields_editor.dart';
import 'package:passingtrace_mobile/subjects/subject_filter_sheet.dart';
import 'package:passingtrace_mobile/subjects/subject_form_view.dart';
import 'package:passingtrace_mobile/subjects/subject_picker.dart';
import 'package:passingtrace_mobile/subjects/subject_delete_dialog.dart';
import 'package:passingtrace_mobile/subjects/subject_relation_form_view.dart';
import 'package:passingtrace_mobile/subjects/subject_relations_section.dart';
import 'package:passingtrace_mobile/subjects/subject_timeline_filter_sheet.dart';
import 'package:passingtrace_mobile/subjects/subject_timeline_section.dart';
import 'package:passingtrace_mobile/subjects/subject_avatar.dart';
import 'package:passingtrace_mobile/subjects/subject_list_view.dart';
import 'package:passingtrace_mobile/subjects/subject_actions_menu.dart';
import 'package:passingtrace_mobile/events/media_api.dart';
import 'package:passingtrace_mobile/theme/quiet_trace_components.dart';
import 'package:passingtrace_mobile/theme/quiet_trace_icons.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';
import 'package:passingtrace_mobile/views/assistant_view.dart';
import 'package:passingtrace_mobile/views/assistant_approval_panel.dart';

const session = AuthSession(
  identityBaseUrl: 'https://identity.test',
  deviceId: 'test',
  deviceSecret: 'test',
  accessToken: 'test',
);
const selfId = '11111111-1111-4111-8111-111111111111',
    catId = '22222222-2222-4222-8222-222222222222',
    entryId = '33333333-3333-4333-8333-333333333333';

class FakeAuth extends AuthService {
  @override
  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async => current;
}

Map<String, dynamic> subject(String id, String name, {bool self = false}) => {
  'id': id,
  'name': name,
  'kind': self ? 0 : 1,
  'isSelf': self,
  'state': 0,
  'version': 3,
  'timezone': 'Asia/Shanghai',
  'fields': [
    {
      'id': 'weight',
      'name': '体重',
      'type': 'number',
      'unit': 'kg',
      'removed': false,
    },
  ],
  'values': {},
  'mediaIds': [],
};
Map<String, dynamic> relation(String from, String to) => {
  'id': '$from-$to',
  'fromSubjectId': from,
  'toSubjectId': to,
  'label': '饲养',
  'directed': false,
  'endedAt': '2026-01-01T00:00:00Z',
  'revision': 1,
};
final graphJson = {
  'rootId': selfId,
  'nodes': [subject(selfId, '自己', self: true), subject(catId, '小猫')],
  'relations': [relation(catId, selfId)],
};
final planJson = <String, dynamic>{
  'id': entryId,
  'subjectId': catId,
  'subjectName': '小猫',
  'sourceSubjectDeleted': false,
  'kind': 1,
  'state': 0,
  'title': '下月疫苗',
  'content': '去医院',
  'version': 2,
  'timezone': 'Asia/Shanghai',
  'fieldChanges': {'weight': 5},
  'actualFieldChanges': {},
  'fieldDefinitions': subject(catId, '小猫')['fields'],
  'markedSubjectIds': [],
  'mediaIds': [],
};

class FakeSubjects extends SubjectsApi {
  FakeSubjects({this.graphFixture, this.entryFixture})
    : super(auth: FakeAuth(), baseUrl: 'https://events.test');
  final Map<String, dynamic>? graphFixture;
  final Map<String, dynamic>? entryFixture;
  Map<String, dynamic>? decision, life, created;
  Map<String, dynamic>? savedEntry;
  String? entryOwner;
  int? entryVersion;
  final timelineQueries = <Map<String, Object?>>[];
  @override
  Future<List<SubjectField>> presets(
    AuthSession s,
    int kind,
    String? itemType,
  ) async => const [SubjectField(id: 'phone', name: '电话号码', type: 'phone')];
  @override
  Future<SubjectModel> create(
    AuthSession s,
    Map<String, dynamic> body,
    String key,
  ) async {
    created = body;
    return SubjectModel(subject('new', body['name'] as String));
  }

  @override
  Future<SubjectGraph> graph(AuthSession s) async =>
      SubjectGraph.fromJson(graphFixture ?? graphJson);
  @override
  Future<SubjectModel> get(AuthSession s, String id) async =>
      SubjectModel(subject(id, id == selfId ? '自己' : '小猫', self: id == selfId));
  @override
  Future<List<SubjectModel>> list(AuthSession s) async => [
    await get(s, selfId),
    await get(s, catId),
  ];
  @override
  Future<SubjectEntry> entry(AuthSession s, String id) async =>
      SubjectEntry({...planJson, 'timezone': 'UTC', ...?entryFixture});
  @override
  Future<SubjectEntry> createEntry(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    String key,
  ) async {
    savedEntry = body;
    entryOwner = id;
    return SubjectEntry({
      ...planJson,
      ...body,
      'subjectId': id,
      'state': body['kind'] == 0 ? 1 : 0,
    });
  }

  @override
  Future<SubjectEntry> updateEntry(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async {
    savedEntry = body;
    entryVersion = version;
    return SubjectEntry({...planJson, ...?entryFixture, ...body});
  }

  @override
  Future<Map<String, dynamic>> timeline(
    AuthSession s,
    String id,
    Map<String, Object?> query,
  ) async {
    timelineQueries.add({...query});
    return {
      'groups': [
        {
          'key': '2026-09',
          'items': [
            {
              'sourceType': 'Event',
              'sourceId': '42',
              'title': '原记录修车',
              'kind': 'Record',
              'state': 'Completed',
            },
            {
              'sourceType': 'SubjectEntry',
              'sourceId': entryId,
              'title': '专属疫苗',
              'kind': 'Plan',
              'state': 'Planned',
            },
          ],
        },
      ],
      'nextCursor': null,
      'timezone': 'Asia/Shanghai',
    };
  }

  @override
  Future<SubjectEntry> decideEntry(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async {
    decision = body;
    return SubjectEntry({
      ...planJson,
      'state': 1,
      'actualFieldChanges': body['actualFieldChanges'],
      'happenedAt': body['happenedAt'],
    });
  }

  @override
  Future<Map<String, dynamic>> previewLifecycle(
    AuthSession s,
    String id,
  ) async => {
    'subject': subject(catId, '小猫'),
    'plans': [planJson],
    'milestones': [],
  };
  @override
  Future<SubjectModel> lifecycle(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async {
    life = body;
    return get(s, id);
  }
}

class FakeSubjectList extends FakeSubjects {
  FakeSubjectList(this.subjects);
  final List<SubjectModel> subjects;
  @override
  Future<List<SubjectModel>> list(AuthSession s) async => subjects;
}

Map<String, dynamic> timelineItem(String id, String title) => {
  'sourceType': 'SubjectEntry',
  'sourceId': id,
  'title': title,
  'kind': 'Plan',
  'state': 'Planned',
  'occurredAt': '2026-10-01T00:00:00Z',
};

class FakeLongTimelineSubjects extends FakeSubjects {
  @override
  Future<Map<String, dynamic>> timeline(
    AuthSession s,
    String id,
    Map<String, Object?> query,
  ) async {
    timelineQueries.add({...query});
    return {
      'groups': [
        {
          'key': '2026-10',
          'items': [
            for (var i = 0; i < 40; i++) timelineItem('oct-$i', '十月计划 $i'),
          ],
        },
        {
          'key': '2026-09',
          'items': [timelineItem(entryId, '九月计划')],
        },
      ],
      'nextCursor': null,
    };
  }
}

class FakeDeleteSubjects extends FakeSubjects {
  FakeDeleteSubjects({this.resultState = 'Succeeded'});
  final String resultState;
  final decisions = <String>[];
  final requests = <String>[];
  final requestKeys = <String>[];
  Completer<void>? decisionGate;
  bool failOnce = false, removed = false;
  @override
  Future<AiApprovalModel> requestDelete(
    AuthSession s,
    String type,
    String id,
    String requestId,
  ) async {
    requests.add(type);
    requestKeys.add(requestId);
    return AiApprovalModel(
      id: 'approval',
      conversationId: 'delete-conversation',
      targetType: type,
      targetId: id,
      title: type == 'SubjectRelation' ? '饲养' : '下月疫苗',
      description: '移除关系保留审计历史。若造成档案断连，将拒绝移除。',
      expiresAt: DateTime.now().add(const Duration(minutes: 15)),
    );
  }

  @override
  Future<AiMutationResultModel> decideDelete(
    AuthSession s,
    AiApprovalModel approval,
    String decision,
  ) async {
    decisions.add(decision);
    await decisionGate?.future;
    if (failOnce) {
      failOnce = false;
      throw const EventApiException(status: 503, message: 'internal');
    }
    final state = decision == 'cancel' ? 'Cancelled' : resultState;
    removed = state == 'Succeeded';
    return AiMutationResultModel.fromJson({
      'operationId': approval.id,
      'state': state,
      'message': {
        'id': 99,
        'role': 'Assistant',
        'content': state == 'Conflict' ? '移除会导致小猫断连，请先添加其他关系。' : state,
      },
    });
  }

  @override
  Future<SubjectGraph> graph(AuthSession s) async => SubjectGraph.fromJson({
    ...graphJson,
    if (removed) 'relations': <Map<String, dynamic>>[],
  });
}

class FakeRelationSubjects extends FakeSubjects {
  FakeRelationSubjects({super.graphFixture});
  Map<String, dynamic>? relationBody;
  String? relationSource, relationId;
  int? relationVersion;
  int writes = 0;
  Completer<void>? gate;
  Object? failure;

  @override
  Future<void> updateRelation(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async {
    writes++;
    relationId = id;
    relationVersion = version;
    relationBody = body;
    await gate?.future;
    if (failure != null) throw failure!;
  }

  @override
  Future<SubjectGraph> relate(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async {
    writes++;
    relationSource = id;
    relationVersion = version;
    relationBody = body;
    await gate?.future;
    if (failure != null) throw failure!;
    return graph(s);
  }
}

Widget app(Widget child) => MaterialApp(
  theme: PassingTraceTheme.light(PassingTracePalette.pine),
  home: child,
);
Future<void> expandRelations(WidgetTester tester) async {
  final header = find.text('人物关系');
  await tester.scrollUntilVisible(
    header,
    240,
    scrollable: find.byType(Scrollable).first,
  );
  await tester.tap(header);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('结束和删除只在更多菜单显示为危险色，取消不提交且保留原档案', (tester) async {
    final api = FakeDeleteSubjects();
    await tester.pumpWidget(
      app(
        SubjectDetailView(
          auth: FakeAuth(),
          session: session,
          subjectId: catId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('结束档案'), findsNothing);
    expect(find.text('申请删除档案'), findsNothing);
    expect(find.byTooltip('编辑资料'), findsOneWidget);
    final more = find.byKey(const Key('subject-detail-more'));
    await tester.tap(more);
    await tester.pumpAndSettle();
    final colors = tester.element(more).traceColors;
    expect(tester.widget<Text>(find.text('结束档案')).style!.color, colors.danger);
    expect(
      tester.widget<Text>(find.text('申请删除档案')).style!.color,
      colors.danger,
    );
    expect(
      tester.widget<Icon>(find.byIcon(Icons.delete_outline)).color,
      colors.danger,
    );
    expect(api.requests, isEmpty);
    await tester.tapAt(const Offset(10, 400));
    await tester.pumpAndSettle();
    expect(find.text('申请删除档案'), findsNothing);
    expect(api.requests, isEmpty);
    await tester.tap(more);
    await tester.pumpAndSettle();
    await tester.tap(find.text('结束档案'));
    await tester.pumpAndSettle();
    final end = tester.widget<FilledButton>(
      find.widgetWithText(FilledButton, '确认结束'),
    );
    expect(end.style!.backgroundColor!.resolve({}), colors.danger);
    expect(
      tester.widget<PopupMenuButton<SubjectAction>>(more).enabled,
      isFalse,
    );
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(api.life, isNull);
    await tester.tap(more);
    await tester.pumpAndSettle();
    await tester.tap(find.text('申请删除档案'));
    await tester.pumpAndSettle();
    expect(find.byType(AlertDialog), findsOneWidget);
    expect(api.requests, ['Subject']);
    expect(api.decisions, isEmpty);
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(api.decisions, ['cancel']);
    expect(api.removed, isFalse);
    expect(find.byType(SubjectDetailView), findsOneWidget);
    expect(find.byType(AssistantView), findsNothing);
    expect(tester.widget<PopupMenuButton<SubjectAction>>(more).enabled, isTrue);
    expect(tester.takeException(), isNull);
    api.close();
  });

  testWidgets('更多菜单按结束原因提供恢复或纠正，自身不显示，执行中禁止打开', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final selected = <SubjectAction>[];
    Future<void> render(SubjectModel current, {bool busy = false}) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: PassingTraceTheme.light(PassingTracePalette.pine),
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context)
                .copyWith(textScaler: const TextScaler.linear(1.6)),
            child: child!,
          ),
          home: Scaffold(
            appBar: AppBar(
              actions: [
                SubjectActionsMenu(
                  subject: current,
                  busy: busy,
                  onSelected: selected.add,
                ),
              ],
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    final ended = SubjectModel({
      ...subject(catId, '小猫'),
      'state': 1,
      'endReason': 'relationship-ended',
    });
    await render(ended);
    await tester.tap(find.byTooltip('更多操作'));
    await tester.pumpAndSettle();
    expect(find.text('结束档案'), findsNothing);
    expect(find.text('恢复档案'), findsOneWidget);
    expect(find.text('纠正结束信息'), findsOneWidget);
    expect(find.text('申请删除档案'), findsOneWidget);
    await tester.tap(find.text('恢复档案'));
    await tester.pumpAndSettle();
    expect(selected, [SubjectAction.resume]);
    await render(SubjectModel({...ended.json, 'endReason': 'deceased'}));
    await tester.tap(find.byTooltip('更多操作'));
    await tester.pumpAndSettle();
    expect(find.text('恢复档案'), findsNothing);
    await tester.tap(find.text('纠正结束信息'));
    await tester.pumpAndSettle();
    expect(selected.last, SubjectAction.correct);
    await render(ended, busy: true);
    await tester.tap(find.byTooltip('更多操作'));
    await tester.pumpAndSettle();
    expect(find.text('申请删除档案'), findsNothing);
    await render(SubjectModel(subject(selfId, '自己', self: true)));
    expect(find.byTooltip('更多操作'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('档案列表区分人宠物和物品分类，自己置顶，点击仍打开真实档案', (tester) async {
    final profiles = [
      subject(catId, '小猫'),
      subject(selfId, '自己', self: true),
      {...subject('friend', '朋友名字比较长时也需要保持卡片可读'), 'kind': 0},
      {...subject('car', '小汽车'), 'kind': 2, 'itemType': 'vehicle'},
      {...subject('house', '小房屋'), 'kind': 2, 'itemType': 'property'},
      {...subject('bike', '自行车'), 'kind': 2, 'itemType': 'bicycle', 'state': 1},
      {...subject('collection', '收藏品'), 'kind': 2, 'itemType': 'collectible'},
    ];
    final graph = SubjectGraph.fromJson({
      'rootId': selfId,
      'nodes': profiles,
      'relations': [],
    });
    final ids = graph.nodes.map((s) => s.id).toSet();
    final opened = <String>[];
    await tester.pumpWidget(
      app(
        Scaffold(
          body: SubjectListView(
            graph: graph,
            matches: ids,
            kept: ids,
            nickname: '我的昵称',
            onOpen: opened.add,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(
      tester.getTopLeft(find.text('我的昵称（自己）')).dy,
      lessThan(tester.getTopLeft(find.text('小猫')).dy),
    );
    expect(find.text('7 份档案'), findsOneWidget);
    const icons = {
      catId: Icons.pets_outlined,
      'friend': Icons.person_outline,
      'car': Icons.directions_car_outlined,
      'house': Icons.home_outlined,
      'bike': Icons.pedal_bike_outlined,
      'collection': Icons.diamond_outlined,
    };
    for (final entry in icons.entries) {
      final card = find.byKey(ValueKey('subject-list-card-${entry.key}'));
      await tester.scrollUntilVisible(card, 150);
      await tester.pumpAndSettle();
      expect(
        find.descendant(of: card, matching: find.byIcon(entry.value)),
        findsOneWidget,
      );
      await tester.tap(card);
      expect(opened.last, entry.key);
    }
    expect(tester.takeException(), isNull);
  });

  testWidgets('人物列表关系清单默认收起，按名称关系搜索及历史筛选，两端可进入档案', (tester) async {
    final graph = SubjectGraph.fromJson({
      'rootId': selfId,
      'nodes': [
        subject(selfId, '自己', self: true),
        for (var i = 0; i < 40; i++) subject('friend-$i', '朋友 $i'),
      ],
      'relations': [
        for (var i = 0; i < 40; i++)
          {
            ...relation(selfId, 'friend-$i'),
            'label': '关系$i',
            'endedAt': i.isEven ? '2025-01-01' : null,
          },
      ],
    });
    final opened = <String>[];
    await tester.pumpWidget(
      app(
        Scaffold(
          body: SubjectListView(
            graph: graph,
            matches: {selfId},
            kept: graph.nodes.map((s) => s.id).toSet(),
            nickname: '我的昵称',
            onOpen: opened.add,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('40 条关系 · 20 条历史关系'), findsOneWidget);
    expect(find.byKey(const Key('subjects-relationships-list')), findsNothing);
    await tester.tap(find.text('人物关系'));
    await tester.pumpAndSettle();
    expect(find.byType(TextButton).evaluate().length, lessThan(24));
    final search = find.byKey(
      const PageStorageKey('subjects-relationships-search-$selfId'),
    );
    await tester.enterText(search, '关系25');
    await tester.pumpAndSettle();
    expect(
      find.text('关系25', findRichText: false, skipOffstage: true).last,
      findsOneWidget,
    );
    await tester.tap(find.widgetWithText(TextButton, '朋友 25'));
    expect(opened.last, 'friend-25');
    await tester.tap(find.byTooltip('筛选关系清单'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(CheckedPopupMenuItem<String>, '历史关系'));
    await tester.pumpAndSettle();
    expect(find.text('暂无匹配的关系'), findsOneWidget);
    await tester.enterText(search, '朋友 26');
    await tester.pumpAndSettle();
    expect(find.text('关系26'), findsOneWidget);
    await tester.tap(find.widgetWithText(TextButton, '我的昵称（自己）'));
    expect(opened.last, selfId);
    FocusManager.instance.primaryFocus?.unfocus();
    await tester.tap(find.text('人物关系'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('subjects-relationships-list')), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('人物列表在小屏大字体下可看类型状态，人物导航使用双人图标且仍选择人物页', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final graph = SubjectGraph.fromJson({
      'rootId': selfId,
      'nodes': [
        subject(selfId, '自己', self: true),
        {...subject(catId, '一只名字很长很长的小猫，卡片应该保留可点击区域'), 'state': 1},
      ],
      'relations': [relation(selfId, catId)],
    });
    int? selected;
    final opened = <String>[];
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(1.6)),
          child: child!,
        ),
        home: Scaffold(
          body: SubjectListView(
            graph: graph,
            matches: {selfId, catId},
            kept: {selfId, catId},
            nickname: '自己',
            onOpen: opened.add,
          ),
          bottomNavigationBar: TraceBottomNavigation(
            selectedIndex: 4,
            onSelected: (index) => selected = index,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('已结束'), findsOneWidget);
    await tester.tap(find.byKey(const ValueKey('subject-list-card-$catId')));
    expect(opened, [catId]);
    expect(
      find.byWidgetPredicate(
        (w) => w is TraceIcon && w.glyph == TraceGlyph.people,
      ),
      findsOneWidget,
    );
    expect(
      find.byWidgetPredicate(
        (w) => w is TraceIcon && w.glyph == TraceGlyph.target,
      ),
      findsNothing,
    );
    await tester.tap(find.text('人物'));
    expect(selected, 4);
    expect(tester.takeException(), isNull);
  });

  testWidgets('档案封面加载失败仍显示类型头像，账号头像继续独立显示', (tester) async {
    final cover = Completer<MediaAccessTarget>();
    await tester.pumpWidget(
      app(
        Scaffold(
          body: Row(
            children: [
              SubjectAvatar(
                subject: SubjectModel(subject(selfId, '自己', self: true)),
              ),
              SubjectAvatar(
                subject: SubjectModel({
                  ...subject('car', '车'),
                  'kind': 2,
                  'itemType': 'vehicle',
                }),
                cover: cover.future,
              ),
            ],
          ),
        ),
      ),
    );
    cover.completeError(StateError('封面读取失败'));
    await tester.pumpAndSettle();
    expect(find.byType(AccountAvatar), findsOneWidget);
    expect(find.byIcon(Icons.directions_car_outlined), findsOneWidget);
    expect(find.textContaining('封面读取失败'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('人物关系默认收起，展开可搜索及筛选，列表限高且可再次收起', (tester) async {
    final api = FakeSubjects(
      graphFixture: {
        'rootId': selfId,
        'nodes': [
          subject(selfId, '自己', self: true),
          for (var i = 0; i < 40; i++) subject('friend-$i', '好友 $i'),
        ],
        'relations': [
          for (var i = 0; i < 40; i++)
            {
              ...relation('friend-$i', selfId),
              'label': '关系$i',
              'endedAt': i.isEven ? '2026-01-01T00:00:00Z' : null,
            },
        ],
      },
    );
    await tester.pumpWidget(
      app(
        SubjectDetailView(
          auth: FakeAuth(),
          session: session,
          subjectId: selfId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(SubjectRelationsSection), findsOneWidget);
    expect(find.text('40 条关系 · 20 条历史关系'), findsOneWidget);
    expect(find.byTooltip('申请移除误关联'), findsNothing);
    expect(find.byKey(const Key('subject-relations-list')), findsNothing);
    expect(find.byType(DropdownButton<String>), findsNothing);
    await expandRelations(tester);
    expect(
      tester.getSize(find.byKey(const Key('subject-relations-list'))).height,
      lessThanOrEqualTo(340),
    );
    expect(find.byTooltip('申请移除误关联').evaluate().length, lessThan(15));
    final search = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.hintText == '搜索名称或关系',
    );
    await tester.enterText(search, '好友 18');
    await tester.pumpAndSettle();
    expect(find.widgetWithText(ListTile, '关系18 — 好友 18'), findsOneWidget);
    expect(find.byTooltip('申请移除误关联'), findsOneWidget);
    await tester.tap(find.byTooltip('筛选关系'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('当前关系'));
    await tester.pumpAndSettle();
    expect(find.text('暂无匹配的关系'), findsOneWidget);
    await tester.tap(find.byTooltip('筛选关系'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('历史关系'));
    await tester.pumpAndSettle();
    expect(find.widgetWithText(ListTile, '关系18 — 好友 18'), findsOneWidget);
    await tester.enterText(search, '关系19');
    await tester.pumpAndSettle();
    expect(find.text('暂无匹配的关系'), findsOneWidget);
    await tester.tap(find.byTooltip('筛选关系'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('全部关系'));
    await tester.pumpAndSettle();
    expect(find.widgetWithText(ListTile, '关系19 — 好友 19'), findsOneWidget);
    FocusManager.instance.primaryFocus?.unfocus();
    await tester.tap(find.text('人物关系'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('subject-relations-list')), findsNothing);
    expect(api.timelineQueries.length, 1);
    expect(tester.takeException(), isNull);
    api.close();
  });
  testWidgets('时间轴筛选集中维护，取消和重置草稿不查询，应用才更新查询', (tester) async {
    final api = FakeSubjects();
    await tester.pumpWidget(
      app(
        SubjectDetailView(
          auth: FakeAuth(),
          session: session,
          subjectId: catId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    final filter = find.byKey(const Key('subject-timeline-filter-button'));
    await tester.scrollUntilVisible(
      filter,
      240,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(filter);
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(ChoiceChip, '计划'));
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(api.timelineQueries.length, 1);
    expect(find.text('按月 · 全部内容 · 全部状态'), findsOneWidget);
    await tester.tap(filter);
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<ChoiceChip>(find.widgetWithText(ChoiceChip, '全部内容'))
          .selected,
      isTrue,
    );
    await tester.tap(find.widgetWithText(ChoiceChip, '计划'));
    await tester.tap(find.widgetWithText(ChoiceChip, '待执行'));
    await tester.tap(find.widgetWithText(ChoiceChip, '按日'));
    final from = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '起始时间',
    );
    final to = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '截止时间',
    );
    await tester.ensureVisible(from);
    await tester.enterText(from, '2026-10-01 00:00');
    await tester.ensureVisible(to);
    await tester.enterText(to, '2026-09-01 00:00');
    await tester.tap(find.text('应用筛选'));
    await tester.pumpAndSettle();
    expect(find.text('截止时间不能早于起始时间'), findsOneWidget);
    expect(api.timelineQueries.length, 1);
    await tester.enterText(to, '2026-10-31 23:59');
    await tester.tap(find.text('应用筛选'));
    await tester.pumpAndSettle();
    expect(api.timelineQueries.length, 2);
    expect(api.timelineQueries.last['groupBy'], 'day');
    expect(api.timelineQueries.last['kind'], 'Plan');
    expect(api.timelineQueries.last['state'], 'Planned');
    expect(api.timelineQueries.last['from'], startsWith('2026-10-01T00:00'));
    expect(find.text('筛选 (3)'), findsOneWidget);
    await tester.tap(filter);
    await tester.pumpAndSettle();
    await tester.tap(find.text('重置'));
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(api.timelineQueries.length, 2);
    expect(find.text('筛选 (3)'), findsOneWidget);
    await tester.tap(filter);
    await tester.pumpAndSettle();
    await tester.tap(find.text('重置'));
    await tester.tap(find.text('应用筛选'));
    await tester.pumpAndSettle();
    expect(api.timelineQueries.length, 3);
    expect(api.timelineQueries.last['groupBy'], 'month');
    expect(api.timelineQueries.last['kind'], isEmpty);
    expect(api.timelineQueries.last['from'], isNull);
    expect(find.text('按月 · 全部内容 · 全部状态'), findsOneWidget);
    expect(
      find.byKey(const Key('subject-timeline-filter-sheet')),
      findsNothing,
    );
    api.close();
  });
  testWidgets('关系收起与时间轴筛选适配小屏大字体和键盘，取消不应用', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    SubjectTimelineFilter? selected;
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(1.4)),
          child: child!,
        ),
        home: Builder(
          builder: (context) => Scaffold(
            body: SingleChildScrollView(
              child: Column(
                children: [
                  SubjectRelationsSection(
                    subjectId: selfId,
                    graph: SubjectGraph.fromJson(graphJson),
                    busy: false,
                    onAdd: () {},
                    onEdit: (_) {},
                    onRemove: (_) {},
                  ),
                  TextButton(
                    onPressed: () async {
                      selected = await showSubjectTimelineFilterSheet(
                        context: context,
                        selection: const SubjectTimelineFilter(),
                      );
                    },
                    child: const Text('打开筛选'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('人物关系'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('打开筛选'));
    await tester.pumpAndSettle();
    tester.view.viewInsets = const FakeViewPadding(bottom: 260);
    await tester.pumpAndSettle();
    final to = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '截止时间',
    );
    await tester.ensureVisible(to);
    await tester.enterText(to, '2026-10-31 23:59');
    await tester.pumpAndSettle();
    expect(
      tester.getBottomLeft(find.widgetWithText(FilledButton, '应用筛选')).dy,
      lessThanOrEqualTo(380),
    );
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(selected, isNull);
    expect(
      find.byKey(const Key('subject-timeline-filter-sheet')),
      findsNothing,
    );
  });
  testWidgets('时间轴按日期收起，刷新及追加分页保留状态，日月分组互不影响', (tester) async {
    var filter = const SubjectTimelineFilter();
    var groups = <Map<String, dynamic>>[
      {
        'key': '2026-10',
        'items': [timelineItem('new', '最近计划')],
      },
      {
        'key': '2026-09',
        'items': [timelineItem('old', '较早记录')],
      },
    ];
    final opened = <Map>[];
    Future<void> render() async {
      await tester.pumpWidget(
        app(
          Scaffold(
            body: CustomScrollView(
              slivers: [
                SubjectTimelineSection(
                  groups: groups,
                  filter: filter,
                  busy: false,
                  loading: false,
                  hasMore: false,
                  onFilter: () {},
                  onMore: () {},
                  onOpen: opened.add,
                ),
              ],
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    await render();
    expect(find.text('最近计划'), findsOneWidget);
    expect(find.text('较早记录'), findsNothing);
    final october = find.byKey(
      const ValueKey('subject-timeline-group-month/2026-10'),
    );
    final september = find.byKey(
      const ValueKey('subject-timeline-group-month/2026-09'),
    );
    await tester.tap(october);
    await tester.pumpAndSettle();
    expect(find.text('最近计划'), findsNothing);
    await tester.tap(september);
    await tester.pumpAndSettle();
    await tester.tap(find.text('较早记录'));
    expect(opened.single['sourceId'], 'old');
    await tester.tap(find.text('全部收起'));
    await tester.pumpAndSettle();
    groups = [
      {
        'key': '2026-10',
        'items': [timelineItem('new', '更新后的计划'), timelineItem('next', '同月下一页')],
      },
      groups.last,
      {
        'key': '2026-08',
        'items': [timelineItem('older', '更早一页')],
      },
    ];
    await render();
    expect(find.text('更新后的计划'), findsNothing);
    expect(find.text('同月下一页'), findsNothing);
    expect(find.text('更早一页'), findsNothing);
    expect(find.text('2 条'), findsOneWidget);
    filter = const SubjectTimelineFilter(groupBy: 'day');
    final monthly = groups;
    groups = [
      {
        'key': '2026-10-01',
        'items': [timelineItem('day', '当天计划')],
      },
    ];
    await render();
    await tester.tap(
      find.byKey(const ValueKey('subject-timeline-group-day/2026-10-01')),
    );
    await tester.pumpAndSettle();
    expect(find.text('当天计划'), findsOneWidget);
    filter = const SubjectTimelineFilter();
    groups = monthly;
    await render();
    expect(find.text('更新后的计划'), findsNothing);
    await tester.tap(find.text('全部展开'));
    await tester.pumpAndSettle();
    expect(find.text('更新后的计划'), findsOneWidget);
    await tester.scrollUntilVisible(find.text('更早一页'), 150);
    await tester.pumpAndSettle();
    expect(find.text('更早一页'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('长时间轴按需构建，工具栏常驻，回到顶部和详情返回保留日期状态', (tester) async {
    final api = FakeLongTimelineSubjects();
    await tester.pumpWidget(
      app(
        SubjectDetailView(
          auth: FakeAuth(),
          session: session,
          subjectId: catId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    final position = tester
        .state<ScrollableState>(find.byType(Scrollable).first)
        .position;
    position.jumpTo(1700);
    await tester.pumpAndSettle();
    expect(tester.getTopLeft(find.text('时间轴')).dy, inInclusiveRange(50, 140));
    expect(find.byTooltip('回到顶部'), findsOneWidget);
    expect(
      find
          .byWidgetPredicate(
            (w) => w is Text && (w.data?.startsWith('十月计划 ') ?? false),
          )
          .evaluate()
          .length,
      lessThan(15),
    );
    await tester.tap(find.byTooltip('回到顶部'));
    await tester.pumpAndSettle();
    expect(position.pixels, 0);
    expect(find.byTooltip('回到顶部'), findsNothing);
    position.jumpTo(1700);
    await tester.pumpAndSettle();
    await tester.tap(find.text('全部收起'));
    await tester.pumpAndSettle();
    expect(find.text('全部展开'), findsOneWidget);
    expect(find.text('十月计划 0'), findsNothing);
    final september = find.byKey(
      const ValueKey('subject-timeline-group-month/2026-09'),
    );
    await tester.scrollUntilVisible(
      september,
      150,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(september);
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('九月计划'),
      100,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.text('九月计划'));
    await tester.pumpAndSettle();
    expect(find.byType(SubjectEntryView), findsOneWidget);
    await tester.tap(find.byTooltip('返回'));
    await tester.pumpAndSettle();
    expect(find.byType(SubjectDetailView), findsOneWidget);
    expect(find.text('十月计划 0'), findsNothing);
    await tester.scrollUntilVisible(
      find.text('九月计划'),
      100,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('九月计划'), findsOneWidget);
    expect(api.timelineQueries.length, 2);
    expect(tester.takeException(), isNull);
    api.close();
  });

  testWidgets('时间轴收起工具栏适配小屏大字体和键盘', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    tester.view.viewInsets = const FakeViewPadding(bottom: 260);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    final scroll = ScrollController();
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(1.8)),
          child: child!,
        ),
        home: Scaffold(
          body: CustomScrollView(
            controller: scroll,
            slivers: [
              SubjectTimelineSection(
                groups: [
                  {
                    'key': '2026-10-01',
                    'items': [
                      for (var i = 0; i < 40; i++)
                        timelineItem('$i', '一条比较长的记录标题 $i'),
                    ],
                  },
                ],
                filter: const SubjectTimelineFilter(
                  groupBy: 'day',
                  kind: 'Plan',
                  state: 'Planned',
                ),
                busy: false,
                loading: false,
                hasMore: true,
                onFilter: () {},
                onMore: () {},
                onOpen: (_) {},
              ),
            ],
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    scroll.jumpTo(1000);
    await tester.pumpAndSettle();
    expect(tester.getBottomLeft(find.text('全部收起')).dy, lessThan(380));
    await tester.tap(find.text('全部收起'));
    await tester.pumpAndSettle();
    expect(find.text('全部展开'), findsOneWidget);
    expect(find.text('一条比较长的记录标题 0'), findsNothing);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox.shrink());
    scroll.dispose();
  });

  testWidgets('关系编辑使用分区页面，取消保留原关系，保存方向与关系版本', (tester) async {
    final api = FakeRelationSubjects();
    await tester.pumpWidget(
      app(
        SubjectDetailView(
          auth: FakeAuth(),
          session: session,
          subjectId: catId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await expandRelations(tester);
    final edit = find.widgetWithText(ListTile, '饲养 — 自己');
    await tester.scrollUntilVisible(
      edit,
      240,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(edit);
    await tester.pumpAndSettle();
    expect(find.byType(SubjectRelationFormView), findsOneWidget);
    expect(find.byType(AlertDialog), findsNothing);
    expect(find.text('关联信息'), findsOneWidget);
    expect(tester.getTopLeft(find.text('关联信息')).dy, inInclusiveRange(80, 250));
    expect(find.text('关系方向'), findsOneWidget);
    expect(find.text('关系时间'), findsOneWidget);
    final name = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '关系名称',
    );
    await tester.enterText(name, '未保存');
    await tester.tap(find.byTooltip('取消编辑'));
    await tester.pumpAndSettle();
    expect(api.writes, 0);
    expect(find.byType(SubjectDetailView), findsOneWidget);
    await tester.tap(edit);
    await tester.pumpAndSettle();
    expect(tester.widget<TextField>(name).controller!.text, '饲养');
    await tester.enterText(name, '陪伴');
    final direction = find.byKey(
      const ValueKey('subject-relation-direction-1'),
    );
    await tester.ensureVisible(direction);
    await tester.tap(direction);
    await tester.pumpAndSettle();
    await tester.tap(find.text('保存关系'));
    await tester.pumpAndSettle();
    expect(find.byType(SubjectDetailView), findsOneWidget);
    expect(api.writes, 1);
    expect(api.relationId, '$catId-$selfId');
    expect(api.relationVersion, 1);
    expect(api.relationBody!['label'], '陪伴');
    expect(api.relationBody!['directed'], isTrue);
    expect(api.relationBody!['fromSubjectId'], catId);
    expect(api.relationBody!['toSubjectId'], selfId);
    expect(api.relationBody!['endedAt'], isNotNull);
    api.close();
  });
  testWidgets('小屏大字体和键盘下可编辑关系及时间，以关联档案版本创建反向关系', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    final other = SubjectModel({
      ...subject('friend', '名字很长的朋友档案用来检查小屏排版和连接方向'),
      'version': 7,
    });
    final current = SubjectModel(subject(catId, '名字很长的小猫档案也需要显示完整名字'));
    final api = FakeRelationSubjects();
    bool? saved;
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(1.4)),
          child: child!,
        ),
        home: Builder(
          builder: (context) => Scaffold(
            body: TextButton(
              onPressed: () async {
                saved = await Navigator.of(context).push<bool>(
                  MaterialPageRoute(
                    builder: (_) => SubjectRelationFormView(
                      api: api,
                      session: session,
                      subject: current,
                      subjects: [other, current],
                    ),
                  ),
                );
              },
              child: const Text('新增关系'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('新增关系'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    final name = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '关系名称',
    );
    await tester.enterText(name, '饲养');
    tester.view.viewInsets = const FakeViewPadding(bottom: 260);
    await tester.pumpAndSettle();
    expect(
      tester.getBottomLeft(find.widgetWithText(FilledButton, '保存关系')).dy,
      lessThanOrEqualTo(380),
    );
    final direction = find.byKey(
      const ValueKey('subject-relation-direction-2'),
    );
    await tester.ensureVisible(direction);
    await tester.pumpAndSettle();
    await tester.tap(direction);
    await tester.pumpAndSettle();
    final start = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '开始时间',
    );
    await tester.ensureVisible(start);
    await tester.enterText(start, '2026-09-20 01:00');
    final end = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '结束时间',
    );
    await tester.ensureVisible(end);
    await tester.enterText(end, '2026-09-25 12:30');
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('保存关系'));
    await tester.pumpAndSettle();
    expect(saved, isTrue);
    expect(api.relationSource, other.id);
    expect(api.relationVersion, 7);
    expect(api.relationBody!['directed'], isTrue);
    expect(api.relationBody!['fromSubjectId'], other.id);
    expect(api.relationBody!['toSubjectId'], catId);
    expect(api.relationBody!['startedAt'], startsWith('2026-09-20T01:00:00'));
    expect(api.relationBody!['endedAt'], startsWith('2026-09-25T12:30:00'));
    expect(api.relationBody!['resume'], isFalse);
    expect(tester.takeException(), isNull);
    api.close();
  });
  testWidgets('关系表单验证空名称和屏幕外日期，提交失败保留草稿且禁止重复提交', (tester) async {
    final api = FakeRelationSubjects()
      ..gate = Completer<void>()
      ..failure = const EventApiException(
        status: 409,
        message: '关系已更新，请刷新后再试。',
      );
    await tester.pumpWidget(
      app(
        SubjectRelationFormView(
          api: api,
          session: session,
          subject: SubjectModel(subject(catId, '小猫')),
          subjects: SubjectGraph.fromJson(graphJson).nodes,
          relation: SubjectRelation(relation(catId, selfId)),
        ),
      ),
    );
    await tester.pumpAndSettle();
    final name = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '关系名称',
    );
    await tester.enterText(name, '');
    await tester.tap(find.text('保存关系'));
    await tester.pumpAndSettle();
    expect(find.text('请填写关系名称'), findsOneWidget);
    expect(api.writes, 0);
    await tester.enterText(name, '新的关系');
    final end = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '结束时间',
    );
    await tester.ensureVisible(end);
    await tester.enterText(end, '无效日期');
    await tester.ensureVisible(name);
    await tester.tap(find.text('保存关系'));
    await tester.pumpAndSettle();
    expect(find.text('日期格式应为 YYYY-MM-DD HH:mm'), findsOneWidget);
    expect(api.writes, 0);
    await tester.ensureVisible(end);
    await tester.enterText(end, '');
    await tester.tap(find.text('保存关系'));
    await tester.pump();
    await tester.tap(find.byType(FilledButton));
    await tester.pump();
    expect(api.writes, 1);
    expect(
      tester
          .widget<RadioListTile<int>>(
            find.byKey(const ValueKey('subject-relation-direction-0')),
          )
          .enabled,
      isFalse,
    );
    await tester.binding.handlePopRoute();
    await tester.pump();
    expect(find.byType(SubjectRelationFormView), findsOneWidget);
    api.gate!.complete();
    await tester.pumpAndSettle();
    expect(find.text('关系已更新，请刷新后再试。'), findsOneWidget);
    expect(tester.widget<TextField>(name).controller!.text, '新的关系');
    expect(api.relationBody!['directed'], isFalse);
    expect(api.relationBody!['endedAt'], isNull);
    expect(api.relationBody!['resume'], isTrue);
    expect(find.byType(SubjectRelationFormView), findsOneWidget);
    api.close();
  });
  test('手动授权决定仅提交确定或取消，共用会话授权接口且不能替换目标', () async {
    final requests = <http.Request>[];
    final api = SubjectsApi(
      auth: FakeAuth(),
      baseUrl: 'https://events.test',
      client: EventApiClient(
        auth: FakeAuth(),
        baseUrl: 'https://events.test',
        httpClient: MockClient((request) async {
          requests.add(request);
          return http.Response(
            jsonEncode({
              'result': {
                'operationId': 'approval',
                'state': 'Cancelled',
                'message': {'role': 'Assistant', 'content': '已取消'},
              },
            }),
            200,
            headers: {'content-type': 'application/json; charset=utf-8'},
          );
        }),
      ),
    );
    final grant = await FakeDeleteSubjects().requestDelete(
      session,
      'SubjectRelation',
      'relation',
      'key',
    );
    final result = await api.decideDelete(session, grant, 'cancel');
    expect(result.state, 'Cancelled');
    expect(
      requests.single.url.path,
      '/api/v1/ai/conversations/delete-conversation/approvals/approval/decision',
    );
    expect(jsonDecode(requests.single.body), {'decision': 'cancel'});
    expect(() => api.decideDelete(session, grant, 'yes'), throwsArgumentError);
    api.close();
  });
  testWidgets('档案内移除关系在原页授权，取消不跳转且系统返回也取消', (tester) async {
    final api = FakeDeleteSubjects();
    await tester.pumpWidget(
      app(
        SubjectDetailView(
          auth: FakeAuth(),
          session: session,
          subjectId: catId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await expandRelations(tester);
    final remove = find.byTooltip('申请移除误关联');
    await tester.scrollUntilVisible(
      remove,
      240,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(remove);
    await tester.pumpAndSettle();
    expect(find.text('移除这条关系？'), findsOneWidget);
    expect(find.text('小猫 — 自己'), findsOneWidget);
    expect(find.byType(AssistantView), findsNothing);
    expect(api.decisions, isEmpty);
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(api.decisions, ['cancel']);
    expect(api.removed, isFalse);
    expect(find.byType(SubjectDetailView), findsOneWidget);
    expect(find.byType(AlertDialog), findsNothing);
    await tester.tap(remove);
    await tester.pumpAndSettle();
    await tester.binding.handlePopRoute();
    await tester.pumpAndSettle();
    expect(api.decisions, ['cancel', 'cancel']);
    expect(find.byType(AlertDialog), findsNothing);
    expect(find.byType(SubjectDetailView), findsOneWidget);
    api.close();
  });
  testWidgets('移除成功只刷新关系，执行中重复点击不会提交第二次', (tester) async {
    final api = FakeDeleteSubjects()..decisionGate = Completer<void>();
    await tester.pumpWidget(
      app(
        SubjectDetailView(
          auth: FakeAuth(),
          session: session,
          subjectId: catId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await expandRelations(tester);
    final remove = find.byTooltip('申请移除误关联');
    await tester.scrollUntilVisible(
      remove,
      240,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(remove);
    await tester.pumpAndSettle();
    await tester.tap(find.text('确定'));
    await tester.pump();
    await tester.tap(find.text('确定'));
    await tester.pump();
    expect(api.decisions, ['confirm']);
    expect(
      tester
          .widget<TextButton>(find.widgetWithText(TextButton, '取消'))
          .onPressed,
      isNull,
    );
    await tester.binding.handlePopRoute();
    await tester.pump();
    expect(api.decisions, ['confirm']);
    api.decisionGate!.complete();
    await tester.pumpAndSettle();
    expect(find.byType(SubjectDetailView), findsOneWidget);
    expect(remove, findsNothing);
    expect(find.text('关系已移除'), findsOneWidget);
    api.close();
  });
  testWidgets('断连结果在原页显示具体原因并关闭，网络失败可使用同一授权重试', (tester) async {
    final api = FakeDeleteSubjects(resultState: 'Conflict')..failOnce = true;
    await tester.pumpWidget(
      app(
        SubjectDetailView(
          auth: FakeAuth(),
          session: session,
          subjectId: catId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await expandRelations(tester);
    final remove = find.byTooltip('申请移除误关联');
    await tester.scrollUntilVisible(
      remove,
      240,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(remove);
    await tester.pumpAndSettle();
    await tester.tap(find.text('确定'));
    await tester.pumpAndSettle();
    expect(find.textContaining('暂时无法处理'), findsOneWidget);
    await tester.tap(find.text('确定'));
    await tester.pumpAndSettle();
    expect(find.text('移除会导致小猫断连，请先添加其他关系。'), findsOneWidget);
    expect(find.text('确定'), findsNothing);
    expect(api.requests, ['SubjectRelation']);
    expect(api.decisions, ['confirm', 'confirm']);
    expect(api.removed, isFalse);
    await tester.tap(find.text('关闭'));
    await tester.pumpAndSettle();
    expect(find.byType(SubjectDetailView), findsOneWidget);
    expect(remove, findsOneWidget);
    api.close();
  });
  testWidgets('记录删除取消保留页面和未保存内容，成功后才返回', (tester) async {
    final api = FakeDeleteSubjects();
    await tester.pumpWidget(
      app(
        Builder(
          builder: (context) => Scaffold(
            body: TextButton(
              onPressed: () => Navigator.of(context).push<void>(
                MaterialPageRoute(
                  builder: (_) => SubjectEntryView(
                    auth: FakeAuth(),
                    session: session,
                    entryId: entryId,
                    apiClient: api,
                  ),
                ),
              ),
              child: const Text('打开记录'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('打开记录'));
    await tester.pumpAndSettle();
    final title = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '标题',
    );
    await tester.enterText(title, '还未保存的标题');
    final remove = find.text('申请删除计划');
    await tester.scrollUntilVisible(
      remove,
      300,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.drag(find.byType(Scrollable).first, const Offset(0, -200));
    await tester.pumpAndSettle();
    await tester.tap(remove);
    await tester.pump(const Duration(milliseconds: 300));
    await tester.pump();
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(find.byType(SubjectEntryView), findsOneWidget);
    expect(tester.widget<TextField>(title).controller!.text, '还未保存的标题');
    expect(find.byType(AssistantView), findsNothing);
    await tester.scrollUntilVisible(
      remove,
      300,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.drag(find.byType(Scrollable).first, const Offset(0, -200));
    await tester.pumpAndSettle();
    await tester.tap(remove);
    await tester.pump(const Duration(milliseconds: 300));
    await tester.pump();
    await tester.tap(find.text('确定'));
    await tester.pumpAndSettle();
    expect(api.decisions, ['cancel', 'confirm']);
    expect(find.byType(SubjectEntryView), findsNothing);
    expect(find.text('打开记录'), findsOneWidget);
    api.close();
  });
  testWidgets('原页授权在小屏、大字体和键盘下可读并取消', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    tester.view.viewInsets = const FakeViewPadding(bottom: 260);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    final api = FakeDeleteSubjects();
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(1.4)),
          child: child!,
        ),
        home: Builder(
          builder: (context) => Scaffold(
            body: TextButton(
              onPressed: () => showSubjectDeleteDialog(
                context: context,
                api: api,
                session: session,
                targetType: 'SubjectRelation',
                targetId: 'relation',
              ),
              child: const Text('移除'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('移除'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(tester.getBottomLeft(find.text('取消')).dy, lessThan(380));
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(api.decisions, ['cancel']);
    expect(tester.takeException(), isNull);
    api.close();
  });
  testWidgets('建档分区在小屏和键盘下可以选择关系并通过固定操作栏保存', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    final api = FakeSubjects();
    SubjectModel? saved;
    await tester.pumpWidget(
      app(
        Builder(
          builder: (context) => Scaffold(
            body: TextButton(
              onPressed: () async {
                saved = await Navigator.of(context).push<SubjectModel>(
                  MaterialPageRoute(
                    builder: (_) => SubjectFormView(
                      auth: FakeAuth(),
                      session: session,
                      apiClient: api,
                    ),
                  ),
                );
              },
              child: const Text('建档'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('建档'));
    await tester.pumpAndSettle();
    final name = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '名称',
    );
    await tester.enterText(name, '新朋友');
    tester.view.viewInsets = const FakeViewPadding(bottom: 260);
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(
      tester.getBottomLeft(find.widgetWithText(FilledButton, '保存档案')).dy,
      lessThanOrEqualTo(380),
    );
    await tester.scrollUntilVisible(
      find.text('新档案指向所选档案'),
      200,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('新档案指向所选档案'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('保存档案'));
    await tester.pumpAndSettle();
    expect(saved?.name, '新朋友');
    expect(api.created!['relations'], [
      {'toSubjectId': selfId, 'label': '关联', 'directed': true},
    ]);
    expect(api.created!['mediaIds'], isEmpty);
    expect(tester.takeException(), isNull);
    api.close();
  });
  test('长链、环和历史关系只布局人物，筛选保留到自己的路径', () {
    final graph = SubjectGraph.fromJson({
      'rootId': selfId,
      'nodes': [
        subject(selfId, '自己', self: true),
        subject('friend', '朋友'),
        subject('mother', '母猫'),
        subject('kitten', '幼猫'),
      ],
      'relations': [
        relation('friend', selfId),
        relation('mother', 'friend'),
        relation('kitten', 'mother'),
        relation('friend', 'mother'),
      ],
    });
    final layout = SubjectLayout(graph);
    expect(layout.positions.length, 4);
    expect(layout.positions[selfId], Offset.zero);
    expect(layout.pathsToRoot({'kitten'}, selfId), {
      'kitten',
      'mother',
      'friend',
      selfId,
    });
    final siblings = List.generate(30, (i) => subject('sibling-$i', '物品 $i'));
    final dense = SubjectLayout(
      SubjectGraph.fromJson({
        'rootId': selfId,
        'nodes': [
          subject(selfId, '自己', self: true),
          ...siblings,
          subject('child', '幼猫'),
        ],
        'relations': [
          for (final sibling in siblings)
            relation(selfId, sibling['id'] as String),
          relation('sibling-0', 'child'),
        ],
      }),
    );
    expect(
      dense.positions['child']!.distance,
      greaterThan(dense.positions['sibling-0']!.distance),
    );
  });
  test('专属 API 保持独立路径、真实 ID、版本与幂等键，不向 Event 写入', () async {
    final requests = <http.Request>[];
    final client = EventApiClient(
      auth: FakeAuth(),
      baseUrl: 'https://events.test',
      httpClient: MockClient((r) async {
        requests.add(r);
        return http.Response(
          jsonEncode(planJson),
          201,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      }),
    );
    final api = SubjectsApi(
      auth: FakeAuth(),
      baseUrl: 'https://events.test',
      client: client,
    );
    final result = await api.createEntry(session, catId, {
      'title': '下月疫苗',
      'markedSubjectIds': [selfId],
    }, 'key');
    expect(result.id, entryId);
    expect(requests.single.url.path, '/api/v1/subjects/$catId/entries');
    expect(requests.single.headers['idempotency-key'], 'key');
    expect(jsonDecode(requests.single.body)['markedSubjectIds'], [selfId]);
    api.close();
  });
  testWidgets('关系图切换列表、打开档案后返回保留人物筛选', (tester) async {
    final api = FakeSubjects();
    await tester.pumpWidget(
      app(
        SubjectsView(
          auth: FakeAuth(),
          session: session,
          apiClient: api,
          nickname: '我的昵称',
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(InteractiveViewer), findsOneWidget);
    await tester.tap(find.byTooltip('列表与关系清单'));
    await tester.pumpAndSettle();
    expect(find.byType(TextFormField), findsNothing);
    expect(find.byType(DropdownButtonFormField<String>), findsNothing);
    await tester.tap(find.byKey(const Key('subjects-filter-button')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField).first, '小猫');
    await tester.tap(find.text('应用筛选'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(ListTile, '小猫').first);
    await tester.pumpAndSettle();
    expect(find.byType(SubjectDetailView), findsOneWidget);
    await tester.scrollUntilVisible(
      find.text('原记录修车'),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.pumpAndSettle();
    expect(find.textContaining('原记录 · 记录'), findsOneWidget);
    await tester.scrollUntilVisible(
      find.text('专属疫苗'),
      150,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.pumpAndSettle();
    expect(find.textContaining('来自人物 · 计划'), findsOneWidget);
    await tester.tap(find.byType(BackButton));
    await tester.pumpAndSettle();
    expect(find.byType(InteractiveViewer), findsNothing);
    expect(find.text('小猫'), findsWidgets);
    expect(find.text('我的昵称（自己）'), findsNothing);
    await tester.tap(find.byKey(const Key('subjects-filter-button')));
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<TextFormField>(find.byType(TextFormField).first)
          .controller!
          .text,
      '小猫',
    );
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    api.close();
  });
  testWidgets('人物筛选仅在应用后生效，取消保留原条件，重置恢复全部', (tester) async {
    const rootId = 'filter-self';
    final api = FakeSubjects(
      graphFixture: {
        'rootId': rootId,
        'nodes': [
          subject(rootId, '自己', self: true),
          subject(catId, '小猫'),
          {...subject('car', '小汽车'), 'kind': 2},
        ],
        'relations': [relation(catId, rootId), relation('car', rootId)],
      },
    );
    await tester.pumpWidget(
      app(SubjectsView(auth: FakeAuth(), session: session, apiClient: api)),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('列表与关系清单'));
    await tester.pumpAndSettle();
    final filterButton = find.byKey(const Key('subjects-filter-button'));
    final cat = find.widgetWithText(ListTile, '小猫');
    final car = find.widgetWithText(ListTile, '小汽车');
    await tester.tap(filterButton);
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField), '小');
    await tester.tap(find.widgetWithText(ChoiceChip, '宠物'));
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(cat, findsOneWidget);
    expect(car, findsOneWidget);
    await tester.tap(filterButton);
    await tester.pumpAndSettle();
    expect(
      tester.widget<TextFormField>(find.byType(TextFormField)).controller!.text,
      '',
    );
    await tester.enterText(find.byType(TextFormField), '  小  ');
    await tester.tap(find.widgetWithText(ChoiceChip, '宠物'));
    await tester.tap(find.text('应用筛选'));
    await tester.pumpAndSettle();
    expect(cat, findsOneWidget);
    expect(car, findsNothing);
    expect(find.byTooltip('筛选人物，已应用 2 项'), findsOneWidget);
    await tester.tap(filterButton);
    await tester.pumpAndSettle();
    await tester.tap(find.text('重置'));
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(car, findsNothing);
    await tester.tap(filterButton);
    await tester.pumpAndSettle();
    await tester.tap(find.text('重置'));
    await tester.tap(find.text('应用筛选'));
    await tester.pumpAndSettle();
    expect(cat, findsOneWidget);
    expect(car, findsOneWidget);
    expect(find.byTooltip('筛选人物'), findsOneWidget);
    api.close();
  });
  testWidgets('人物筛选在小屏、键盘和大字体下可应用并关闭', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    tester.view.viewInsets = const FakeViewPadding(bottom: 260);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    SubjectFilterSelection? selected;
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(1.4)),
          child: child!,
        ),
        home: Builder(
          builder: (context) => Scaffold(
            body: TextButton(
              onPressed: () async {
                selected = await showSubjectFilterSheet(
                  context: context,
                  selection: const SubjectFilterSelection(
                    query: '小猫',
                    kind: '1',
                  ),
                );
              },
              child: const Text('打开筛选'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('打开筛选'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('应用筛选'));
    await tester.pumpAndSettle();
    expect(selected?.query, '小猫');
    expect(selected?.kind, '1');
    expect(find.byKey(const Key('subject-filter-sheet')), findsNothing);
    expect(tester.takeException(), isNull);
  });
  testWidgets('自身详情使用账号昵称和头像，不显示生命周期或删除操作', (tester) async {
    final api = FakeSubjects();
    await tester.pumpWidget(
      app(
        SubjectDetailView(
          auth: FakeAuth(),
          session: session,
          subjectId: selfId,
          apiClient: api,
          nickname: '账号昵称',
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('账号昵称（自己）'), findsOneWidget);
    expect(find.byType(AccountAvatar), findsOneWidget);
    expect(find.text('结束档案'), findsNothing);
    expect(find.text('申请删除档案'), findsNothing);
    expect(find.byKey(const Key('subject-detail-more')), findsNothing);
    api.close();
  });
  testWidgets('专属计划确认实际值，预期值不自动代入', (tester) async {
    final api = FakeSubjects();
    await tester.pumpWidget(
      app(
        SubjectEntryView(
          auth: FakeAuth(),
          session: session,
          entryId: entryId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('确认实际情况并完成计划'),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('确认实际情况并完成计划'));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('确认完成'),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.pumpAndSettle();
    final actualForm = find.byType(Form).last;
    final fields = find.descendant(
      of: actualForm,
      matching: find.byType(TextFormField),
    );
    expect(tester.widget<TextFormField>(fields.last).controller!.text, '');
    await tester.enterText(fields.first, '2026-09-01 12:00');
    await tester.tap(find.text('确认完成'));
    await tester.pumpAndSettle();
    expect(api.decision!['actualFieldChanges'], isEmpty);
    expect(api.decision!['happenedAt'], '2026-09-01T12:00:00+08:00');
    api.close();
  });
  testWidgets('记录在小屏键盘下可修改字段、标记人物并保存当前版本', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    final api = FakeSubjects(
      entryFixture: {
        'kind': 0,
        'state': 1,
        'title': '建档',
        'content': '现有正文',
        'happenedAt': '2026-09-01T04:00:00Z',
      },
    );
    await tester.pumpWidget(
      app(
        SubjectEntryView(
          auth: FakeAuth(),
          session: session,
          entryId: entryId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('记录'), findsOneWidget);
    expect(find.textContaining('人物专属'), findsNothing);
    expect(find.text('生活字段'), findsNothing);
    final title = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '标题',
    );
    await tester.enterText(title, '体重变化');
    tester.view.viewInsets = const FakeViewPadding(bottom: 260);
    await tester.pumpAndSettle();
    expect(
      tester.getBottomLeft(find.widgetWithText(FilledButton, '保存记录')).dy,
      lessThanOrEqualTo(380),
    );
    final weight = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '体重 (kg)',
    );
    await tester.scrollUntilVisible(
      weight,
      200,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.pumpAndSettle();
    await tester.enterText(weight, '6');
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.byKey(const Key('subject-picker-open')),
      200,
      scrollable: find.byType(Scrollable).first,
    );
    await Scrollable.ensureVisible(
      tester.element(find.byKey(const Key('subject-picker-open'))),
      alignment: 0.3,
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('subject-picker-open')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('subject-option-$selfId')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('subject-picker-confirm')));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('保存记录'));
    await tester.pumpAndSettle();
    expect(api.entryVersion, 2);
    expect(api.savedEntry!['title'], '体重变化');
    expect(api.savedEntry!['content'], '现有正文');
    expect(api.savedEntry!['happenedAt'], '2026-09-01T12:00:00+08:00');
    expect(api.savedEntry!['fieldChanges'], {'weight': 6});
    expect(api.savedEntry!['markedSubjectIds'], [selfId]);
    expect(find.text('记录已保存'), findsOneWidget);
    expect(tester.takeException(), isNull);
    api.close();
  });
  testWidgets('新建记录切换为计划后保存预期值，实际值保持独立', (tester) async {
    final api = FakeSubjects();
    await tester.pumpWidget(
      app(
        SubjectEntryView(
          auth: FakeAuth(),
          session: session,
          subjectId: catId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('计划'));
    await tester.pumpAndSettle();
    expect(find.text('新建计划'), findsOneWidget);
    final title = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '标题',
    );
    await tester.enterText(title, '下月检查');
    final weight = find.byWidgetPredicate(
      (w) => w is TextField && w.decoration?.labelText == '体重 (kg)',
    );
    await tester.scrollUntilVisible(
      weight,
      200,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.pumpAndSettle();
    await tester.enterText(weight, '5.5');
    await tester.tap(find.text('保存计划'));
    await tester.pumpAndSettle();
    expect(api.entryOwner, catId);
    expect(api.savedEntry!['kind'], 1);
    expect(api.savedEntry!['fieldChanges'], {'weight': 5.5});
    expect(api.savedEntry!.containsKey('actualFieldChanges'), isFalse);
    expect(api.savedEntry!.containsKey('happenedAt'), isFalse);
    expect(find.text('计划已保存'), findsOneWidget);
    expect(tester.takeException(), isNull);
    api.close();
  });
  testWidgets('人物选择取消或关闭保留原标记，确定后显示标签且可直接移除', (tester) async {
    final api = FakeSubjects();
    var ids = [catId];
    var changes = 0;
    await tester.pumpWidget(
      app(
        Scaffold(
          body: StatefulBuilder(
            builder: (_, setState) => Padding(
              padding: const EdgeInsets.all(20),
              child: SubjectPicker(
                auth: FakeAuth(),
                session: session,
                apiClient: api,
                ids: ids,
                onChanged: (selected) => setState(() {
                  ids = selected;
                  changes++;
                }),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(CheckboxListTile), findsNothing);
    expect(find.byType(InputChip), findsOneWidget);
    await tester.tap(find.byKey(const Key('subject-picker-open')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('subject-picker-search')),
      '自己',
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('subject-option-$selfId')));
    await tester.pumpAndSettle();
    expect(changes, 0);
    await tester.tap(find.text('取消'));
    await tester.pumpAndSettle();
    expect(ids, [catId]);
    await tester.tap(find.byKey(const Key('subject-picker-open')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('subject-option-$selfId')));
    await tester.pumpAndSettle();
    await tester.tapAt(const Offset(10, 10));
    await tester.pumpAndSettle();
    expect(ids, [catId]);
    expect(changes, 0);
    await tester.tap(find.byKey(const Key('subject-picker-open')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('subject-option-$selfId')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('subject-picker-confirm')));
    await tester.pumpAndSettle();
    expect(ids, [catId, selfId]);
    expect(find.byType(CheckboxListTile), findsNothing);
    await tester.tap(find.byTooltip('移除小猫'));
    await tester.pumpAndSettle();
    expect(ids, [selfId]);
    expect(tester.takeException(), isNull);
    api.close();
  });
  testWidgets('大量人物搜索和类型筛选保留其他选择，排除所属人物且不展开整个列表', (tester) async {
    final api = FakeSubjectList([
      for (var i = 0; i < 200; i++)
        SubjectModel({...subject('profile-$i', '档案$i'), 'kind': i % 3}),
    ]);
    var ids = ['profile-0', 'unavailable'];
    await tester.pumpWidget(
      app(
        Scaffold(
          body: StatefulBuilder(
            builder: (_, setState) => SubjectPicker(
              auth: FakeAuth(),
              session: session,
              apiClient: api,
              ids: ids,
              exclude: 'profile-5',
              onChanged: (selected) => setState(() => ids = selected),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(CheckboxListTile), findsNothing);
    await tester.tap(find.byKey(const Key('subject-picker-open')));
    await tester.pumpAndSettle();
    expect(find.byType(CheckboxListTile).evaluate().length, lessThan(30));
    final search = find.byKey(const Key('subject-picker-search'));
    await tester.enterText(search, '档案199');
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('subject-option-profile-199')));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('按类型筛选'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(CheckedPopupMenuItem<String>, '物品'));
    await tester.pumpAndSettle();
    expect(find.text('没有找到匹配的人物'), findsOneWidget);
    expect(find.text('确定（3）'), findsOneWidget);
    await tester.enterText(search, '档案5');
    await tester.pumpAndSettle();
    expect(
      find.byKey(const ValueKey('subject-option-profile-5')),
      findsNothing,
    );
    await tester.enterText(search, '档案2');
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('subject-option-profile-2')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('subject-picker-confirm')));
    await tester.pumpAndSettle();
    expect(ids, ['profile-0', 'unavailable', 'profile-199', 'profile-2']);
    expect(find.byType(InputChip), findsNWidgets(3));
    expect(find.text('另有 1 个，可在选择列表中管理。'), findsOneWidget);
    expect(find.byType(CheckboxListTile), findsNothing);
    expect(tester.takeException(), isNull);
    api.close();
  });
  testWidgets('人物多选在小屏大字体和键盘下可搜索、选择并确认', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    final api = FakeSubjects();
    var ids = <String>[];
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(1.4)),
          child: child!,
        ),
        home: Scaffold(
          body: StatefulBuilder(
            builder: (_, setState) => SubjectPicker(
              auth: FakeAuth(),
              session: session,
              apiClient: api,
              ids: ids,
              onChanged: (selected) => setState(() => ids = selected),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('subject-picker-open')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('subject-picker-search')),
      '小猫',
    );
    tester.view.viewInsets = const FakeViewPadding(bottom: 260);
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.tap(find.byKey(const ValueKey('subject-option-$catId')));
    await tester.pumpAndSettle();
    expect(
      tester.getBottomLeft(find.byKey(const Key('subject-picker-confirm'))).dy,
      lessThanOrEqualTo(380),
    );
    await tester.tap(find.byKey(const Key('subject-picker-confirm')));
    await tester.pumpAndSettle();
    expect(ids, [catId]);
    expect(find.byKey(const Key('subject-picker-sheet')), findsNothing);
    expect(tester.takeException(), isNull);
    api.close();
  });
  testWidgets('结束档案的所属计划默认保留', (tester) async {
    final api = FakeSubjects();
    await tester.pumpWidget(
      app(
        SubjectDetailView(
          auth: FakeAuth(),
          session: session,
          subjectId: catId,
          apiClient: api,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('subject-detail-more')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('结束档案'));
    await tester.pumpAndSettle();
    expect(find.textContaining('默认保留'), findsOneWidget);
    await tester.tap(find.text('确认结束'));
    await tester.pumpAndSettle();
    expect(api.life!['cancelPlans'], isEmpty);
    expect(api.life!['operation'], 'end');
    api.close();
  });
  testWidgets('字段保留电话号码前导零，服务端回算后刷新当前值', (tester) async {
    const fields = [SubjectField(id: 'phone', name: '电话', type: 'phone')];
    var values = <String, dynamic>{};
    await tester.pumpWidget(
      app(
        Scaffold(
          body: StatefulBuilder(
            builder: (_, update) => SubjectFieldsEditor(
              fields: fields,
              values: values,
              onChanged: (v) => update(() => values = v),
            ),
          ),
        ),
      ),
    );
    await tester.enterText(find.byType(TextFormField), '012345');
    expect(values['phone'], '012345');
    FocusManager.instance.primaryFocus?.unfocus();
    await tester.pump();
    values = {'phone': '09876'};
    await tester.pumpWidget(
      app(
        Scaffold(
          body: StatefulBuilder(
            builder: (_, update) => SubjectFieldsEditor(
              fields: fields,
              values: values,
              onChanged: (v) => update(() => values = v),
            ),
          ),
        ),
      ),
    );
    expect(
      tester.widget<TextFormField>(find.byType(TextFormField)).controller!.text,
      '09876',
    );
  });
  testWidgets('AI 人物与专属引用使用证据链接，未知 ID 不可点击', (tester) async {
    final opened = <String>[];
    await tester.pumpWidget(
      app(
        Scaffold(
          body: AssistantMessageContent(
            text:
                '[Subject #$catId] [SubjectEntry #$entryId] [Subject #$selfId]',
            isUser: false,
            socialTitles: {
              'subject/$catId': '小猫',
              'subjectentry/$entryId': '下月疫苗',
            },
            onOpenSocial: opened.add,
          ),
        ),
      ),
    );
    final markdown = tester.widget<MarkdownBody>(find.byType(MarkdownBody));
    expect(markdown.data, contains('passingtrace://subject/$catId'));
    expect(markdown.data, contains('passingtrace://subjectentry/$entryId'));
    expect(markdown.data, contains('不可查看'));
    expect(markdown.data, isNot(contains('passingtrace://subject/$selfId')));
    markdown.onTapLink!('小猫', 'passingtrace://subject/$catId', '');
    markdown.onTapLink!('下月疫苗', 'passingtrace://subjectentry/$entryId', '');
    expect(opened, ['subject/$catId', 'subjectentry/$entryId']);
  });
  testWidgets('人物删除授权在小屏、键盘和大字体下可取消且禁止重复点击', (tester) async {
    tester.view.physicalSize = const Size(360, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final decisions = <String>[];
    final approval = AiApprovalModel(
      id: 'a',
      conversationId: 'c',
      targetType: 'Subject',
      targetId: catId,
      title: '小猫',
      description: '保留所有原记录和专属内容，删除前重新检查图连接。',
      expiresAt: DateTime.now().add(const Duration(minutes: 15)),
    );
    await tester.pumpWidget(
      app(
        MediaQuery(
          data: const MediaQueryData(
            size: Size(360, 640),
            textScaler: TextScaler.linear(1.4),
            viewInsets: EdgeInsets.only(bottom: 260),
          ),
          child: Scaffold(
            body: Column(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                AssistantApprovalPanel(
                  approval: approval,
                  remaining: 1,
                  busy: false,
                  onDecision: decisions.add,
                  onOpen: () {},
                ),
              ],
            ),
          ),
        ),
      ),
    );
    expect(tester.takeException(), isNull);
    await tester.tap(find.text('取消'));
    expect(decisions, ['cancel']);
  });
}
