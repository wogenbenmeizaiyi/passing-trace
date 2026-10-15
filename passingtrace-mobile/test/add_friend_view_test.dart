import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/events/events_api.dart';
import 'package:passingtrace_mobile/social/add_friend_view.dart';
import 'package:passingtrace_mobile/social/friends_view.dart';
import 'package:passingtrace_mobile/social/social_api.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

class _Api extends SocialApi {
  _Api()
    : super(
        AuthService(),
        const AuthSession(
          identityBaseUrl: 'https://identity.test',
          deviceId: 'test',
          deviceSecret: 'test',
          accessToken: 'test',
        ),
      );

  final calls = <(String, String, Object?)>[];
  Future<dynamic> Function()? respond;

  @override
  Future<dynamic> request(
    String method,
    String path, {
    Object? body,
    bool identity = false,
  }) async {
    calls.add((method, path, body));
    return respond == null ? {'id': 'request'} : await respond!();
  }
}

void main() {
  Future<void> open(
    WidgetTester tester,
    _Api api, {
    bool dark = false,
    double scale = 1,
    bool legacy = false,
  }) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: dark
            ? PassingTraceTheme.dark(PassingTracePalette.pine)
            : PassingTraceTheme.light(PassingTracePalette.pine),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: TextScaler.linear(scale)),
          child: child!,
        ),
        home: legacy
            ? FriendsView(api: api, onChat: (_) {}, initialPanel: 'add')
            : AddFriendView(api: api),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('添加页不加载联系人，兼容原添加入口', (tester) async {
    final api = _Api();
    addTearDown(api.close);
    await open(tester, api, legacy: true);
    expect(api.calls, isEmpty);
    expect(find.text('添加好友'), findsOneWidget);
    expect(find.text('发送申请'), findsOneWidget);
    expect(find.text('扫码添加'), findsOneWidget);
    expect(find.text('我的好友码'), findsOneWidget);
    expect(find.text('查找好友'), findsNothing);
    expect(
      tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
      isNull,
    );
    await tester.tap(find.byType(TextField));
    await tester.pump();
    await tester.testTextInput.receiveAction(TextInputAction.done);
    await tester.pumpAndSettle();
    expect(find.text('请先输入好友码。'), findsOneWidget);
    expect(api.calls, isEmpty);
  });

  testWidgets('发送去空格、防重复，成功后保留好友码并明确反馈', (tester) async {
    final api = _Api();
    addTearDown(api.close);
    final response = Completer<dynamic>();
    api.respond = () => response.future;
    await open(tester, api);
    await tester.enterText(find.byType(TextField), '  ABCDEF0123456789  ');
    await tester.pump();
    await tester.tap(find.text('发送申请'));
    await tester.pump();
    expect(find.text('正在发送…'), findsOneWidget);
    expect(tester.widget<TextField>(find.byType(TextField)).readOnly, isTrue);
    expect(
      tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
      isNull,
    );
    await tester.tap(find.text('正在发送…'));
    expect(api.calls.length, 1);
    expect(api.calls.single.$1, 'POST');
    expect(api.calls.single.$2, '/api/v1/friend-requests');
    expect(api.calls.single.$3, {'code': 'ABCDEF0123456789'});
    response.complete({'id': 'request'});
    await tester.pumpAndSettle();
    expect(find.text('申请已发送'), findsOneWidget);
    expect(find.text('好友申请已发送，等待对方确认。'), findsOneWidget);
    expect(
      tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
      isNull,
    );
    await tester.enterText(find.byType(TextField), 'OTHER');
    await tester.pump();
    expect(find.text('好友申请已发送，等待对方确认。'), findsNothing);
    expect(
      tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
      isNotNull,
    );
  });

  testWidgets('错误就近显示且保留输入，可以修改并用键盘重试', (tester) async {
    final api = _Api();
    addTearDown(api.close);
    api.respond = () async => throw const EventApiException(
      status: 400,
      message: '没有找到这个好友码，请检查后重试。',
    );
    await open(tester, api);
    await tester.enterText(find.byType(TextField), 'NOTFOUND');
    await tester.testTextInput.receiveAction(TextInputAction.done);
    await tester.pumpAndSettle();
    expect(find.text('没有找到这个好友码，请检查后重试。'), findsOneWidget);
    expect(
      tester.widget<TextField>(find.byType(TextField)).controller!.text,
      'NOTFOUND',
    );
    await tester.enterText(find.byType(TextField), 'CORRECTED');
    await tester.pumpAndSettle();
    expect(find.text('没有找到这个好友码，请检查后重试。'), findsNothing);
    api.respond = null;
    await tester.testTextInput.receiveAction(TextInputAction.done);
    await tester.pumpAndSettle();
    expect(api.calls.length, 2);
    expect(find.text('申请已发送'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('打开我的好友码后返回不丢失正在填写的内容', (tester) async {
    final api = _Api();
    addTearDown(api.close);
    api.respond = () async =>
        throw const EventApiException(status: 503, message: '暂时无法加载');
    await open(tester, api);
    await tester.enterText(find.byType(TextField), 'KEEP-DRAFT');
    await tester.tap(find.text('我的好友码'));
    await tester.pumpAndSettle();
    expect(api.calls.single.$2, '/api/v1/people/me');
    await tester.pageBack();
    await tester.pumpAndSettle();
    expect(
      tester.widget<TextField>(find.byType(TextField)).controller!.text,
      'KEEP-DRAFT',
    );
    expect(api.calls.length, 1);
    await tester.tap(find.byTooltip('清空好友码'));
    await tester.pumpAndSettle();
    expect(
      tester.widget<TextField>(find.byType(TextField)).controller!.text,
      isEmpty,
    );
  });

  for (final dark in [false, true]) {
    testWidgets('320dp 小屏、两倍字体、键盘弹出可滚动，无溢出 ${dark ? '深色' : '浅色'}', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(320, 640);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetViewInsets);
      final api = _Api();
      addTearDown(api.close);
      await open(tester, api, dark: dark, scale: 2);
      expect(tester.takeException(), isNull);
      await tester.enterText(find.byType(TextField), 'ABCDE');
      tester.view.viewInsets = const FakeViewPadding(bottom: 300);
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byType(FilledButton));
      await tester.pumpAndSettle();
      final button = tester.getRect(find.byType(FilledButton));
      expect(button.height, greaterThanOrEqualTo(48));
      expect(button.bottom, lessThanOrEqualTo(340));
      await tester.tap(find.text('发送申请'));
      await tester.pumpAndSettle();
      expect(api.calls.length, 1);
      tester.view.resetViewInsets();
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('我的好友码'));
      await tester.pumpAndSettle();
      expect(find.text('我的好友码').hitTestable(), findsOneWidget);
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('离开页面时尚未完成的申请不会引发 setState 异常', (tester) async {
    final api = _Api();
    addTearDown(api.close);
    final response = Completer<dynamic>();
    api.respond = () => response.future;
    await open(tester, api);
    await tester.enterText(find.byType(TextField), 'CODE');
    await tester.pump();
    await tester.tap(find.text('发送申请'));
    await tester.pumpWidget(const SizedBox());
    response.complete({'id': 'request'});
    await tester.pump();
    expect(tester.takeException(), isNull);
  });
}
