import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../auth_service.dart';
import 'events_api.dart';

class AiConversationModel {
  const AiConversationModel({
    required this.id,
    required this.title,
    required this.updatedAt,
  });
  final String id;
  final String title;
  final DateTime updatedAt;

  factory AiConversationModel.fromJson(Map<String, dynamic> json) =>
      AiConversationModel(
        id: json['id'] as String,
        title: json['title'] as String,
        updatedAt: DateTime.parse(json['updatedAt'] as String),
      );
}

class AiMessageModel {
  const AiMessageModel({
    required this.role,
    required this.content,
    required this.evidenceRecords,
  });

  final String role;
  final String content;
  final List<AiEvidenceRecord> evidenceRecords;
  List<int> get evidenceEventIds =>
      evidenceRecords.map((record) => record.eventId).toList(growable: false);

  factory AiMessageModel.fromJson(Map<String, dynamic> json) {
    final evidence = json['evidence'];
    final records = evidence is Map<String, dynamic>
        ? evidence['records'] as List<dynamic>? ?? const []
        : const <dynamic>[];
    return AiMessageModel(
      role: (json['role'] as String).toLowerCase(),
      content: json['content'] as String,
      evidenceRecords: records
          .whereType<Map<String, dynamic>>()
          .map(AiEvidenceRecord.fromJson)
          .toList(growable: false),
    );
  }
}

class AiEvidenceRecord {
  const AiEvidenceRecord({required this.eventId, this.title});

  final int eventId;
  final String? title;

  factory AiEvidenceRecord.fromJson(Map<String, dynamic> json) =>
      AiEvidenceRecord(
        eventId: (json['eventId'] as num).toInt(),
        title: (json['title'] as String?)?.trim(),
      );
}

class AiConversationDetailModel {
  const AiConversationDetailModel({
    required this.conversation,
    required this.messages,
  });

  final AiConversationModel conversation;
  final List<AiMessageModel> messages;

  factory AiConversationDetailModel.fromJson(Map<String, dynamic> json) =>
      AiConversationDetailModel(
        conversation: AiConversationModel.fromJson(json),
        messages: (json['messages'] as List<dynamic>? ?? const [])
            .map(
              (message) =>
                  AiMessageModel.fromJson(message as Map<String, dynamic>),
            )
            .toList(growable: false),
      );
}

class UserMemoryModel {
  const UserMemoryModel({
    required this.id,
    required this.type,
    required this.content,
    required this.status,
    required this.evidenceEventIds,
  });

  final int id;
  final String type;
  final String content;
  final String status;
  final List<int> evidenceEventIds;

  factory UserMemoryModel.fromJson(Map<String, dynamic> json) =>
      UserMemoryModel(
        id: (json['id'] as num).toInt(),
        type: json['type'] as String,
        content: json['content'] as String,
        status: json['status'] as String,
        evidenceEventIds:
            (json['evidenceEventIds'] as List<dynamic>? ?? const [])
                .map((value) => (value as num).toInt())
                .toList(growable: false),
      );
}

class AssistantChunk {
  const AssistantChunk(this.type, this.data);
  final String type;
  final dynamic data;
}

class AiApiClient {
  AiApiClient({
    required this.auth,
    required this.baseUrl,
    http.Client? httpClient,
  }) : _http = httpClient ?? http.Client();

  final AuthService auth;
  final String baseUrl;
  final http.Client _http;

  Uri _uri(String path) => Uri.parse(
    '${baseUrl.endsWith('/') ? baseUrl.substring(0, baseUrl.length - 1) : baseUrl}$path',
  );

  Future<Map<String, String>> _headers(AuthSession session) async {
    late final AuthSession fresh;
    try {
      fresh = await auth.ensureFreshToken(session);
    } on AuthSessionExpiredException catch (error) {
      throw EventApiException(status: 401, message: error.message);
    }
    if (fresh.accessToken == null) {
      throw const EventApiException(status: 401, message: '登录状态已失效。');
    }
    return {
      'Authorization': 'Bearer ${fresh.accessToken}',
      'Accept': 'application/json',
      'Content-Type': 'application/json',
    };
  }

  Future<AiConversationModel> createConversation(AuthSession session) async {
    final response = await _http.post(
      _uri('/api/v1/ai/conversations'),
      headers: await _headers(session),
      body: jsonEncode({'title': null}),
    );
    return AiConversationModel.fromJson(_decode(response, const {201}));
  }

  Future<List<AiConversationModel>> listConversations(
    AuthSession session,
  ) async {
    final response = await _http.get(
      _uri('/api/v1/ai/conversations'),
      headers: await _headers(session),
    );
    return _decodeList(response, const {200})
        .map(
          (item) => AiConversationModel.fromJson(item as Map<String, dynamic>),
        )
        .toList(growable: false);
  }

  Future<AiConversationDetailModel> getConversation(
    AuthSession session,
    String id,
  ) async {
    final response = await _http.get(
      _uri('/api/v1/ai/conversations/$id'),
      headers: await _headers(session),
    );
    return AiConversationDetailModel.fromJson(_decode(response, const {200}));
  }

  Future<void> deleteConversation(AuthSession session, String id) async {
    final response = await _http.delete(
      _uri('/api/v1/ai/conversations/$id'),
      headers: await _headers(session),
    );
    if (response.statusCode != 204) _decode(response, const {204});
  }

  Stream<AssistantChunk> send(
    AuthSession session,
    String conversationId,
    String content,
  ) async* {
    final request =
        http.Request(
            'POST',
            _uri('/api/v1/ai/conversations/$conversationId/messages'),
          )
          ..headers.addAll(await _headers(session))
          ..body = jsonEncode({'content': content});
    final response = await _http.send(request);
    if (response.statusCode != 200) {
      final body = await response.stream.bytesToString();
      throw EventApiException(status: response.statusCode, message: body);
    }
    String? event;
    await for (final line
        in response.stream
            .transform(utf8.decoder)
            .transform(const LineSplitter())) {
      if (line.startsWith('event:')) {
        event = line.substring(6).trim();
      } else if (line.startsWith('data:') && event != null) {
        yield AssistantChunk(event, jsonDecode(line.substring(5).trim()));
        event = null;
      }
    }
  }

  Future<List<UserMemoryModel>> listMemories(AuthSession session) async {
    final response = await _http.get(
      _uri('/api/v1/ai/memories'),
      headers: await _headers(session),
    );
    final raw = _decodeList(response, const {200});
    return raw
        .map((item) => UserMemoryModel.fromJson(item as Map<String, dynamic>))
        .toList(growable: false);
  }

  Future<UserMemoryModel> confirmMemory(AuthSession session, int id) async {
    final response = await _http.patch(
      _uri('/api/v1/ai/memories/$id'),
      headers: await _headers(session),
      body: jsonEncode({'status': 'confirmed'}),
    );
    return UserMemoryModel.fromJson(_decode(response, const {200}));
  }

  Future<void> forgetMemory(AuthSession session, int id) async {
    final response = await _http.delete(
      _uri('/api/v1/ai/memories/$id'),
      headers: await _headers(session),
    );
    if (response.statusCode != 204) _decode(response, const {204});
  }

  static Map<String, dynamic> _decode(
    http.Response response,
    Set<int> success,
  ) {
    if (success.contains(response.statusCode)) {
      return response.body.isEmpty
          ? <String, dynamic>{}
          : jsonDecode(response.body) as Map<String, dynamic>;
    }
    throw EventApiException(
      status: response.statusCode,
      message: 'AI 请求失败：${response.statusCode} ${response.body}',
    );
  }

  static List<dynamic> _decodeList(http.Response response, Set<int> success) {
    if (success.contains(response.statusCode)) {
      return jsonDecode(response.body) as List<dynamic>;
    }
    throw EventApiException(
      status: response.statusCode,
      message: 'AI 请求失败：${response.statusCode}',
    );
  }

  void close() => _http.close();
}
