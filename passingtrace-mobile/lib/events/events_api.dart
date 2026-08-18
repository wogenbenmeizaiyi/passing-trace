// Events API 调用的统一入口。
//
// 责任：
//   1. 调用前从 `AuthService.ensureFreshToken(session)` 取最新 access_token。
//   2. 注入 `Authorization: Bearer` 头与必要 `Idempotency-Key` / `If-Match`。
//   3. 业务错误统一解析为 `EventApiException { status, problem }`，把 ProblemDetails
//      的 `detail` 透传给上层。
//   4. 401 时复用 `AuthService.ensureFreshToken` 重试一次。

import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../auth_service.dart';
import 'event_model.dart';

class EventApiException implements Exception {
  const EventApiException({
    required this.status,
    required this.message,
    this.problem,
  });

  final int status;
  final String message;
  final ProblemDetails? problem;

  @override
  String toString() => message;
}

class EventApiClient {
  EventApiClient({
    required this.auth,
    required this.baseUrl,
    http.Client? httpClient,
  }) : _http = httpClient ?? http.Client();

  final AuthService auth;
  final String baseUrl;
  final http.Client _http;

  Uri _resolve(String path, [Map<String, Object?>? query]) {
    final root = baseUrl.endsWith('/')
        ? baseUrl.substring(0, baseUrl.length - 1)
        : baseUrl;
    final uri = Uri.parse('$root$path');
    if (query == null || query.isEmpty) return uri;
    return uri.replace(
      queryParameters: {
        for (final entry in query.entries)
          if (entry.value != null) entry.key: '${entry.value}',
      },
    );
  }

  Future<Map<String, String>> _bearerHeaders(AuthSession session) async {
    final fresh = await auth.ensureFreshToken(session);
    final token = fresh.accessToken;
    if (token == null || token.isEmpty) {
      throw const EventApiException(
        status: 401,
        message: '登录状态已失效，请重新登录。',
      );
    }
    return {
      'Authorization': 'Bearer $token',
      'Accept': 'application/json',
    };
  }

  Future<http.Response> _send(
    AuthSession session,
    String method,
    Uri uri, {
    Object? body,
    Map<String, String>? extraHeaders,
    bool retryOn401 = true,
  }) async {
    Future<http.Response> doRequest(Map<String, String> headers) {
      final encoded = body == null ? null : jsonEncode(body);
      switch (method) {
        case 'GET':
          return _http.get(uri, headers: headers);
        case 'POST':
          return _http.post(uri, headers: headers, body: encoded);
        case 'PATCH':
          return _http.patch(uri, headers: headers, body: encoded);
        case 'DELETE':
          return _http.delete(uri, headers: headers, body: encoded);
      }
      throw StateError('Unsupported method: $method');
    }

    final headers = await _bearerHeaders(session);
    headers['Content-Type'] = 'application/json';
    if (extraHeaders != null) headers.addAll(extraHeaders);

    final response = await doRequest(headers);
    if (response.statusCode == 401 && retryOn401) {
      // 重试一次：`ensureFreshToken` 会判定过期并走 refresh token 续期。
      // 续期成功时拿到的就是新 session；重试仍 401 才视为真正过期。
      try {
        await auth.ensureFreshToken(session);
      } catch (_) {
        throw const EventApiException(
          status: 401,
          message: '登录状态已失效，请重新登录。',
        );
      }
      return _send(
        session,
        method,
        uri,
        body: body,
        extraHeaders: extraHeaders,
        retryOn401: false,
      );
    }
    return response;
  }

  T _decode<T>(http.Response response, Set<int> success, T Function(dynamic) parse) {
    if (success.contains(response.statusCode)) {
      if (response.body.isEmpty) {
        return parse(null);
      }
      return parse(jsonDecode(response.body));
    }
    ProblemDetails? problem;
    try {
      if (response.body.isNotEmpty) {
        final raw = jsonDecode(response.body);
        if (raw is Map<String, dynamic>) {
          problem = ProblemDetails.fromJson(raw);
        }
      }
    } catch (_) {
      problem = null;
    }
    final message = problem?.detail ?? problem?.title ?? '请求失败：${response.statusCode}';
    throw EventApiException(
      status: response.statusCode,
      message: message,
      problem: problem,
    );
  }

  Future<EventPage> list(
    AuthSession session, {
    int limit = 50,
    int? cursor,
    EventKind? kind,
    EventStatus? status,
    String? from,
    String? to,
  }) async {
    final uri = _resolve('/api/v1/events', {
      'limit': limit,
      'cursor': cursor,
      'kind': kind?.value,
      'status': status?.value,
      'from': from,
      'to': to,
    });
    final response = await _send(session, 'GET', uri);
    return _decode(
      response,
      const {200},
      (raw) => EventPage.fromJson(raw as Map<String, dynamic>),
    );
  }

  Future<EventModel> get(AuthSession session, int id) async {
    if (id <= 0) {
      throw ArgumentError.value(id, 'id', '事件 id 必须为正整数');
    }
    final uri = _resolve('/api/v1/events/$id');
    final response = await _send(session, 'GET', uri);
    return _decode(
      response,
      const {200},
      (raw) => EventModel.fromJson(raw as Map<String, dynamic>),
    );
  }

  Future<EventModel> create(
    AuthSession session, {
    required EventKind kind,
    String? title,
    String? rawContent,
    DateTime? happenedAt,
    DateTime? plannedAt,
    required String timezone,
    required String idempotencyKey,
  }) async {
    if (idempotencyKey.isEmpty) {
      throw ArgumentError.value(idempotencyKey, 'idempotencyKey', '幂等键不能为空');
    }
    final body = <String, Object?>{
      'kind': kind.value,
      'title': title,
      'rawContent': rawContent,
      'happenedAt': happenedAt?.toUtc().toIso8601String(),
      'plannedAt': plannedAt?.toUtc().toIso8601String(),
      'timezone': timezone,
    };
    final uri = _resolve('/api/v1/events');
    final response = await _send(
      session,
      'POST',
      uri,
      body: body,
      extraHeaders: {'Idempotency-Key': idempotencyKey},
    );
    return _decode(
      response,
      const {201},
      (raw) => EventModel.fromJson(raw as Map<String, dynamic>),
    );
  }

  Future<EventModel> update(
    AuthSession session,
    int id, {
    String? title,
    String? rawContent,
    DateTime? happenedAt,
    DateTime? plannedAt,
    required String timezone,
    required int version,
  }) async {
    if (version < 0) {
      throw ArgumentError.value(version, 'version', 'version 必须为非负整数');
    }
    final body = <String, Object?>{
      'title': title,
      'rawContent': rawContent,
      'happenedAt': happenedAt?.toUtc().toIso8601String(),
      'plannedAt': plannedAt?.toUtc().toIso8601String(),
      'timezone': timezone,
    };
    final uri = _resolve('/api/v1/events/$id');
    final response = await _send(
      session,
      'PATCH',
      uri,
      body: body,
      extraHeaders: {'If-Match': '$version'},
    );
    return _decode(
      response,
      const {200},
      (raw) => EventModel.fromJson(raw as Map<String, dynamic>),
    );
  }

  Future<void> remove(AuthSession session, int id, {required int version}) async {
    if (version < 0) {
      throw ArgumentError.value(version, 'version', 'version 必须为非负整数');
    }
    final uri = _resolve('/api/v1/events/$id');
    final response = await _send(
      session,
      'DELETE',
      uri,
      extraHeaders: {'If-Match': '$version'},
    );
    if (response.statusCode == 204) return;
    ProblemDetails? problem;
    try {
      if (response.body.isNotEmpty) {
        final raw = jsonDecode(response.body);
        if (raw is Map<String, dynamic>) {
          problem = ProblemDetails.fromJson(raw);
        }
      }
    } catch (_) {
      problem = null;
    }
    final message = problem?.detail ?? problem?.title ?? '删除失败：${response.statusCode}';
    throw EventApiException(
      status: response.statusCode,
      message: message,
      problem: problem,
    );
  }

  void close() => _http.close();
}
