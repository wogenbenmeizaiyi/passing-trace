import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/main.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

void main() {
  testWidgets('登录与创建账号通过底部文字操作切换', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: RegistrationPage(auth: AuthService(), onRegistered: (_) {}),
      ),
    );

    expect(find.text('星期八'), findsOneWidget);
    expect(find.text('欢迎回来'), findsOneWidget);
    expect(find.text('第一次使用？'), findsOneWidget);
    expect(find.text('初始注册码'), findsNothing);

    final modeSwitch = find.byKey(const Key('auth-mode-switch'));
    await tester.ensureVisible(modeSwitch);
    await tester.tap(modeSwitch);
    await tester.pump();
    expect(find.text('创建账号'), findsNWidgets(2));
    expect(find.text('已经有账号？'), findsOneWidget);
    expect(find.text('返回登录'), findsOneWidget);
    expect(find.text('初始注册码'), findsOneWidget);

    await tester.ensureVisible(modeSwitch);
    await tester.tap(modeSwitch);
    await tester.pump();
    expect(find.text('欢迎回来'), findsOneWidget);
    expect(find.text('初始注册码'), findsNothing);
  });
}
