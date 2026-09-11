import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/events/events_api.dart';
import 'package:passingtrace_mobile/user_facing_error.dart';

void main() {
  test('未知 AI 结束原因会转成可理解的重试提示', () {
    final message = userFacingErrorMessage(
      StateError("Unknown ChatFinishReason value. (Parameter 'value')"),
    );

    expect(message, 'AI 服务刚才返回了不完整结果，请重新发送一次。');
    expect(message, isNot(contains('ChatFinishReason')));
    expect(message, isNot(contains('Parameter')));
    expect(message, isNot(contains('Bad state')));
  });

  test('后端已转换的友好文字可以直接展示', () {
    final message = userFacingErrorMessage(
      const EventApiException(status: 503, message: 'AI 服务刚才返回了不完整结果，请重新发送一次。'),
    );

    expect(message, 'AI 服务刚才返回了不完整结果，请重新发送一次。');
  });

  test('技术异常细节不会出现在界面', () {
    const fallback = '暂时无法加载，请稍后重试。';
    final message = userFacingErrorMessage(
      const EventApiException(
        status: 500,
        message: 'System.Exception at localhost:5432 (Parameter value)',
      ),
      fallback: fallback,
    );

    expect(message, fallback);
  });

  test('HTTP 状态码不会作为用户提示', () {
    const fallback = '暂时没能完成这次回答，请稍后重试。';
    final message = userFacingErrorMessage(
      const EventApiException(status: 500, message: 'AI 请求失败：500'),
      fallback: fallback,
    );

    expect(message, fallback);
  });
}
