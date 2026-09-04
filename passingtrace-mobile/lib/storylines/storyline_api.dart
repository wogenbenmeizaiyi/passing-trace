import 'dart:convert';

import 'package:http/http.dart' as http;

import '../auth_service.dart';
import '../events/events_api.dart';
import 'storyline_model.dart';

class StorylineApiClient {
  StorylineApiClient({
    required this.auth,
    required this.baseUrl,
    http.Client? httpClient,
  }) : _http = httpClient ?? http.Client();
  final AuthService auth;
  final String baseUrl;
  final http.Client _http;
  Uri _uri(String path, [Map<String, Object?>? query]) {
    final root = baseUrl.endsWith('/')
        ? baseUrl.substring(0, baseUrl.length - 1)
        : baseUrl;
    final uri = Uri.parse('$root$path');
    if (query == null) return uri;
    return uri.replace(
      queryParameters: {
        for (final e in query.entries)
          if (e.value != null) e.key: '${e.value}',
      },
    );
  }

  Future<http.Response> _send(
    AuthSession session,
    String method,
    Uri uri, {
    Object? body,
    Map<String, String>? headers,
  }) async {
    final fresh = await auth.ensureFreshToken(session);
    final token = fresh.accessToken;
    if (token == null || token.isEmpty) {
      throw const EventApiException(status: 401, message: '登录状态已失效，请重新登录。');
    }
    final all = {
      'Authorization': 'Bearer $token',
      'Accept': 'application/json',
      'Content-Type': 'application/json',
      ...?headers,
    };
    Future<http.Response> call(String access) => switch (method) {
      'GET' => _http.get(
        uri,
        headers: {...all, 'Authorization': 'Bearer $access'},
      ),
      'POST' => _http.post(
        uri,
        headers: {...all, 'Authorization': 'Bearer $access'},
        body: body == null ? null : jsonEncode(body),
      ),
      'DELETE' => _http.delete(
        uri,
        headers: {...all, 'Authorization': 'Bearer $access'},
      ),
      _ => throw StateError('Unsupported method'),
    };
    var response = await call(token);
    if (response.statusCode == 401) {
      final renewed = await auth.ensureFreshToken(session, forceRefresh: true);
      response = await call(renewed.accessToken ?? '');
    }
    return response;
  }

  T _decode<T>(http.Response response, T Function(dynamic) parse) {
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return parse(response.body.isEmpty ? null : jsonDecode(response.body));
    }
    String message = '请求失败：${response.statusCode}';
    try {
      final value = jsonDecode(response.body) as Map<String, dynamic>;
      message = (value['detail'] ?? value['title'] ?? message) as String;
    } catch (_) {}
    throw EventApiException(status: response.statusCode, message: message);
  }

  Future<List<StorylineSummary>> list(
    AuthSession session, {
    int? status,
    String? categoryKey,
    String? from,
    String? to,
  }) async {
    final response = await _send(
      session,
      'GET',
      _uri('/api/v1/storylines', {
        'limit': 60,
        'status': status,
        'categoryKey': categoryKey,
        'from': from,
        'to': to,
      }),
    );
    return _decode(
      response,
      (raw) => (raw['items'] as List<dynamic>)
          .map((x) => StorylineSummary.fromJson(x as Map<String, dynamic>))
          .toList(),
    );
  }

  Future<StorylineDetailModel> get(AuthSession session, String id) async {
    final response = await _send(
      session,
      'GET',
      _uri('/api/v1/storylines/$id'),
    );
    return _decode(
      response,
      (raw) => StorylineDetailModel.fromJson(raw as Map<String, dynamic>),
    );
  }

  Future<StorylineSaveResult> create(
    AuthSession session,
    Map<String, dynamic> body,
    String idempotencyKey,
  ) async {
    final response = await _send(
      session,
      'POST',
      _uri('/api/v1/storylines'),
      body: body,
      headers: {'Idempotency-Key': idempotencyKey},
    );
    return _decode(
      response,
      (raw) => StorylineSaveResult.fromJson(raw as Map<String, dynamic>),
    );
  }

  Future<StorylineSaveResult> change(
    AuthSession session,
    String id,
    int version,
    Map<String, dynamic> body,
    String idempotencyKey,
  ) async {
    final response = await _send(
      session,
      'POST',
      _uri('/api/v1/storylines/$id/changes'),
      body: body,
      headers: {'If-Match': '$version', 'Idempotency-Key': idempotencyKey},
    );
    return _decode(
      response,
      (raw) => StorylineSaveResult.fromJson(raw as Map<String, dynamic>),
    );
  }

  Future<StorylineSaveResult> restore(
    AuthSession session,
    String id,
    int revision,
    int version,
    String idempotencyKey,
  ) async {
    final response = await _send(
      session,
      'POST',
      _uri('/api/v1/storylines/$id/revisions/$revision/restore'),
      headers: {'If-Match': '$version', 'Idempotency-Key': idempotencyKey},
    );
    return _decode(
      response,
      (raw) => StorylineSaveResult.fromJson(raw as Map<String, dynamic>),
    );
  }

  void close() => _http.close();
}
