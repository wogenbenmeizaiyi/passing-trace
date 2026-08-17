import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/auth_service.dart';

void main() {
  test('AuthException exposes a safe user-facing message', () {
    const exception = AuthException('登录已过期');

    expect(exception.toString(), '登录已过期');
  });
}
