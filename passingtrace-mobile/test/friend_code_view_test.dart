import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/social/social_api.dart';
import 'package:passingtrace_mobile/social/social_widgets.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

const _png =
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII=';

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
  bool fail = false;
  int avatars = 0;
  @override
  Future<dynamic> request(
    String method,
    String path, {
    Object? body,
    bool identity = false,
  }) async {
    if (fail) throw Exception('offline');
    return {
      'profile': {
        'id': '1',
        'nickname': '不要显示我的昵称',
        'friendCode': '172BD1EB56614E4F',
        'hasAvatar': true,
      },
      'qrDataUrl': 'data:image/png;base64,$_png',
    };
  }

  @override
  Future<Uint8List> bytes(String path, {bool identity = false}) async {
    avatars++;
    return base64Decode(_png);
  }
}

void main() {
  for (final dark in [false, true]) {
    for (final size in [const Size(320, 640), const Size(740, 360)]) {
      testWidgets('二维码头像居中、隐藏昵称、复制反馈 $dark $size', (tester) async {
        tester.view.physicalSize = size;
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final api = _Api();
        addTearDown(api.close);
        String? copied;
        tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
          SystemChannels.platform,
          (call) async {
            if (call.method == 'Clipboard.setData') {
              copied = (call.arguments as Map)['text'] as String;
            }
            return null;
          },
        );
        addTearDown(
          () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
            SystemChannels.platform,
            null,
          ),
        );
        await tester.pumpWidget(
          MaterialApp(
            theme: dark
                ? PassingTraceTheme.dark(PassingTracePalette.pine)
                : PassingTraceTheme.light(PassingTracePalette.pine),
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: TextScaler.linear(2)),
              child: child!,
            ),
            home: FriendCodeView(api: api),
          ),
        );
        await tester.pumpAndSettle();
        expect(find.text('不要显示我的昵称'), findsNothing);
        expect(find.text('172BD1EB56614E4F'), findsNothing);
        expect(api.avatars, 1);
        expect(
          tester.getCenter(find.byKey(const ValueKey('friend-code-avatar'))),
          tester.getCenter(find.byType(Image).first),
        );
        await tester.ensureVisible(find.text('复制好友码'));
        await tester.tap(find.text('复制好友码'));
        await tester.pumpAndSettle();
        expect(copied, '172BD1EB56614E4F');
        expect(find.text('好友码已复制'), findsOneWidget);
        expect(tester.takeException(), isNull);
      });
    }
  }
  testWidgets('加载失败可重试', (tester) async {
    final api = _Api()..fail = true;
    addTearDown(api.close);
    await tester.pumpWidget(MaterialApp(home: FriendCodeView(api: api)));
    await tester.pumpAndSettle();
    expect(find.text('重新加载'), findsOneWidget);
    api.fail = false;
    await tester.tap(find.text('重新加载'));
    await tester.pumpAndSettle();
    expect(find.text('扫一扫，加我为好友'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
