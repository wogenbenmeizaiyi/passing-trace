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
    late final AuthSession fresh;
    try {
      fresh = await auth.ensureFreshToken(session);
    } on AuthSessionExpiredException catch (error) {
      throw EventApiException(status: 401, message: error.message);
    }
    final token = fresh.accessToken;
    if (token == null || token.isEmpty) {
      throw const EventApiException(status: 401, message: '登录状态已失效，请重新登录。');
    }
    return {'Authorization': 'Bearer $token', 'Accept': 'application/json'};
  }

  Future<http.Response> _send(
    AuthSession session,
    String method,
    Uri uri, {
    Object? body,
    Map<String, String>? extraHeaders,
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
    if (response.statusCode == 401) {
      // API 拒绝了当前 access token 时强制刷新一次，并用返回的
      // 新 token 直接重试。不再递归传入旧 session，避免重复使用已轮换的
      // refresh token。
      late final AuthSession refreshed;
      try {
        refreshed = await auth.ensureFreshToken(session, forceRefresh: true);
      } catch (_) {
        throw const EventApiException(status: 401, message: '登录状态已失效，请重新登录。');
      }
      final retryHeaders = Map<String, String>.from(headers)
        ..['Authorization'] = 'Bearer ${refreshed.accessToken}';
      return doRequest(retryHeaders);
    }
    return response;
  }

  T _decode<T>(
    http.Response response,
    Set<int> success,
    T Function(dynamic) parse,
  ) {
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
    final message =
        problem?.detail ?? problem?.title ?? '请求失败：${response.statusCode}';
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
    return _decode(response, const {
      200,
    }, (raw) => EventPage.fromJson(raw as Map<String, dynamic>));
  }

  Future<EventModel> get(AuthSession session, int id) async {
    if (id <= 0) {
      throw ArgumentError.value(id, 'id', '事件 id 必须为正整数');
    }
    final uri = _resolve('/api/v1/events/$id');
    final response = await _send(session, 'GET', uri);
    return _decode(response, const {
      200,
    }, (raw) => EventModel.fromJson(raw as Map<String, dynamic>));
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
    List<String> mediaIds = const [],
    ManualClassification? classification,
    List<EventLocationModel>? locations,
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
      'mediaIds': mediaIds,
      if (classification != null) 'classification': classification.toJson(),
      if (locations != null)
        'locations': locations.map((x) => x.toJson()).toList(),
    };
    final uri = _resolve('/api/v1/events');
    final response = await _send(
      session,
      'POST',
      uri,
      body: body,
      extraHeaders: {'Idempotency-Key': idempotencyKey},
    );
    return _decode(response, const {
      201,
    }, (raw) => EventModel.fromJson(raw as Map<String, dynamic>));
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
    List<String> mediaIds = const [],
    ManualClassification? classification,
    List<EventLocationModel>? locations,
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
      'mediaIds': mediaIds,
      if (classification != null) 'classification': classification.toJson(),
      if (locations != null)
        'locations': locations.map((x) => x.toJson()).toList(),
    };
    final uri = _resolve('/api/v1/events/$id');
    final response = await _send(
      session,
      'PATCH',
      uri,
      body: body,
      extraHeaders: {'If-Match': '$version'},
    );
    return _decode(response, const {
      200,
    }, (raw) => EventModel.fromJson(raw as Map<String, dynamic>));
  }

  Future<void> remove(
    AuthSession session,
    int id, {
    required int version,
  }) async {
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
    final message =
        problem?.detail ?? problem?.title ?? '删除失败：${response.statusCode}';
    throw EventApiException(
      status: response.statusCode,
      message: message,
      problem: problem,
    );
  }

  Future<EventTaxonomyModel> taxonomy(AuthSession session) async {
    final response = await _send(
      session,
      'GET',
      _resolve('/api/v1/event-taxonomy'),
    );
    return _decode(response, const {
      200,
    }, (raw) => EventTaxonomyModel.fromJson(raw as Map<String, dynamic>));
  }

  Future<List<PlaceCandidateModel>> searchPlaces(
    AuthSession session, {
    required String mode,
    String? query,
    double? latitude,
    double? longitude,
    int radiusMeters = 1000,
    String? cityAdCode,
  }) async {
    final response = await _send(
      session,
      'POST',
      _resolve('/api/v1/places/search'),
      body: {
        'mode': mode,
        'query': query,
        'latitude': latitude,
        'longitude': longitude,
        'radiusMeters': radiusMeters,
        'cityAdCode': cityAdCode,
      },
    );
    return _decode(
      response,
      const {200},
      (raw) => (raw as List<dynamic>)
          .map((x) => PlaceCandidateModel.fromJson(x as Map<String, dynamic>))
          .toList(),
    );
  }

  Future<Map<String, dynamic>> navigationTarget(
    AuthSession session,
    int eventId,
    int locationId,
  ) async {
    final response = await _send(
      session,
      'GET',
      _resolve(
        '/api/v1/events/$eventId/locations/$locationId/navigation-target',
      ),
    );
    return _decode(response, const {200}, (raw) => raw as Map<String, dynamic>);
  }

  void close() => _http.close();
}
