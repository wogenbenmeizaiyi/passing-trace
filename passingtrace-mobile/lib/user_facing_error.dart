import 'dart:async';
import 'dart:io';

import 'package:http/http.dart' as http;

import 'auth_service.dart';
import 'events/events_api.dart';

String userFacingErrorMessage(
  Object error, {
  String fallback = '操作没有完成，请稍后重试。',
}) {
  final raw = switch (error) {
    EventApiException exception => exception.message.trim(),
    AuthException exception => exception.message.trim(),
    _ => '',
  };

  if (_containsUnknownFinishReason(raw) ||
      _containsUnknownFinishReason(error.toString())) {
    return 'AI 服务刚才返回了不完整结果，请重新发送一次。';
  }

  if (error is EventApiException) {
    if (error.status == 401) return '登录状态已过期，请重新登录。';
    if (error.status == 408 || error.status == 504) {
      return '等待响应超时，请检查网络后重试。';
    }
    if (error.status == 429) return '当前请求较多，请稍等片刻再试。';
    if (_isSafeMessage(raw)) return raw;
    if (error.status >= 500) return fallback;
    return fallback;
  }

  if (error is AuthException && _isSafeMessage(raw)) return raw;
  if (error is TimeoutException) return '等待响应超时，请检查网络后重试。';
  if (error is SocketException || error is http.ClientException) {
    return '网络连接不稳定，请检查网络后重试。';
  }
  if (error is FormatException) return '返回的数据暂时无法读取，请稍后重试。';
  return fallback;
}

bool _containsUnknownFinishReason(String value) =>
    value.toLowerCase().contains('unknown chatfinishreason');

bool _isSafeMessage(String value) {
  if (value.isEmpty || value.length > 180) return false;
  if (!RegExp(r'[\u3400-\u9fff]').hasMatch(value)) return false;
  return !RegExp(
    r'exception|stack\s*trace|bad\s*state|parameter|platformexception|'
    r'chatfinishreason|package:|\.dart\b|\.cs:\d+|/home/runner|'
    r'localhost:\d+|#sha1|invalid_[a-z_]+|null,\s*null|'
    r'\btoken\b|\bpkce\b|\boidc\b|\boauth\b|\bstate\b|回调|注册意图|'
    r'(?:ai\s*)?请求失败\s*[:：]\s*\d{3}|http\s*\d{3}|<!doctype\s+html',
    caseSensitive: false,
  ).hasMatch(value);
}
