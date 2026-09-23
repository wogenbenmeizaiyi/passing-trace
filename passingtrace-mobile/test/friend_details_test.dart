import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/social/friends_view.dart';
import 'package:passingtrace_mobile/social/social_api.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

class _Api extends SocialApi {
  _Api()
    : super(
        AuthService(),
        const AuthSession(
          identityBaseUrl: 'https://test.invalid',
          deviceId: 'test',
          deviceSecret: 'test',
          accessToken: 'test',
        ),
      );
  final writes = <(String, String, Object?)>[];
  @override
  Future<dynamic> request(
    String method,
    String path, {
    Object? body,
    bool identity = false,
  }) async {
    if (method != 'GET') {
      writes.add((method, path, body));
      return {};
    }
    if (path == '/api/v1/people/me') {
      return {
        'profile': {'id': 'me'},
      };
    }
    if (path == '/api/v1/friends') {
      return [
        {
          'id': 'friendship',
          'person': {
            'id': 'friend',
            'nickname': '小林',
            'bio': '喜欢跑步和读书',
            'hasAvatar': false,
          },
          'remark': '一起跑步的小林',
          'label': '亲密朋友',
          'relationship': '亲密朋友',
          'version': 3,
        },
      ];
    }
    return [];
  }
}

void main() {
  for (final dark in [false, true]) {
    testWidgets('资料首页精简，编辑按需展开，危险操作需确认 $dark', (tester) async {
      FlutterSecureStorage.setMockInitialValues({});
      tester.view.physicalSize = const Size(360, 740);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final api = _Api();
      addTearDown(api.close);
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
          home: FriendsView(api: api, onChat: (_) {}, selectedId: 'friendship'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('好友资料'), findsOneWidget);
      expect(find.byType(TextField), findsNothing);
      expect(find.text('申请恋人'), findsNothing);
      expect(find.text('删除好友'), findsNothing);
      expect(find.text('一起跑步的小林'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.tap(find.text('备注与标签'));
      await tester.pumpAndSettle();
      expect(find.byType(TextField), findsNWidgets(2));
      await tester.enterText(find.byType(TextField).first, '新备注');
      await tester.ensureVisible(find.text('保存'));
      await tester.tap(find.text('保存'));
      await tester.pumpAndSettle();
      expect(api.writes.single.$1, 'PUT');
      expect(api.writes.single.$2, '/api/v1/friends/friendship/preference');
      expect(api.writes.single.$3, {'remark': '新备注', 'label': '亲密朋友'});
      expect(tester.takeException(), isNull);
      await tester.tap(find.text('一起跑步的小林'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('双方关系'));
      await tester.pumpAndSettle();
      expect(find.text('申请恋人'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.tap(find.byTooltip('更多好友操作'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('删除好友'));
      await tester.pumpAndSettle();
      expect(api.writes.length, 1);
      expect(find.textContaining('重新添加不会恢复旧授权'), findsOneWidget);
      await tester.tap(find.text('取消'));
      await tester.pumpAndSettle();
      expect(find.text('好友资料'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  }
}
