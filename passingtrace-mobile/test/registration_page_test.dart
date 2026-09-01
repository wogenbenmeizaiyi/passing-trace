import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/main.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

void main() {
  testWidgets('初始注册码只在创建账号模式显示', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: PassingTraceTheme.light(PassingTracePalette.pine),
        home: RegistrationPage(auth: AuthService(), onRegistered: (_) {}),
      ),
    );

    expect(find.text('初始注册码'), findsNothing);

    await tester.tap(find.text('创建账号'));
    await tester.pump();
    expect(find.text('初始注册码'), findsOneWidget);

    await tester.tap(find.text('登录'));
    await tester.pump();
    expect(find.text('初始注册码'), findsNothing);
  });
}
