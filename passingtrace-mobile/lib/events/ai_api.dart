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
    required this.amapPlaces,
    required this.actions,
  });

  final String role;
  final String content;
  final List<AiEvidenceRecord> evidenceRecords;
  final List<AmapPlaceModel> amapPlaces;
  final List<AssistantActionModel> actions;
  List<int> get evidenceEventIds =>
      evidenceRecords.map((record) => record.eventId).toList(growable: false);

  factory AiMessageModel.fromJson(Map<String, dynamic> json) {
    final evidence = json['evidence'];
    final records = evidence is Map<String, dynamic>
        ? evidence['records'] as List<dynamic>? ?? const []
        : const <dynamic>[];
    final amapPlaces = evidence is Map<String, dynamic>
        ? evidence['amapPlaces'] as List<dynamic>? ?? const []
        : const <dynamic>[];
    final actions = evidence is Map<String, dynamic>
        ? evidence['actions'] as List<dynamic>? ?? const []
        : const <dynamic>[];
    return AiMessageModel(
      role: (json['role'] as String).toLowerCase(),
      content: json['content'] as String,
      evidenceRecords: records
          .whereType<Map<String, dynamic>>()
          .map(AiEvidenceRecord.fromJson)
          .toList(growable: false),
      amapPlaces: amapPlaces
          .whereType<Map<String, dynamic>>()
          .map(AmapPlaceModel.fromJson)
          .where((place) => place.isValid)
          .toList(growable: false),
      actions: actions
          .whereType<Map<String, dynamic>>()
          .map(AssistantActionModel.fromJson)
          .where((action) => action.isSafe)
          .toList(growable: false),
    );
  }
}

class AmapPlaceModel {
  const AmapPlaceModel({
    required this.candidateId,
    required this.name,
    required this.latitude,
    required this.longitude,
    this.poiId,
    this.address,
  });

  final String candidateId;
  final String? poiId;
  final String name;
  final String? address;
  final double latitude;
  final double longitude;

  bool get isValid =>
      candidateId.isNotEmpty &&
      name.isNotEmpty &&
      latitude >= -90 &&
      latitude <= 90 &&
      longitude >= -180 &&
      longitude <= 180;

  factory AmapPlaceModel.fromJson(Map<String, dynamic> json) => AmapPlaceModel(
    candidateId: json['candidateId'] as String? ?? '',
    poiId: json['poiId'] as String?,
    name: json['name'] as String? ?? '',
    address: json['address'] as String?,
    latitude: (json['latitude'] as num?)?.toDouble() ?? double.nan,
    longitude: (json['longitude'] as num?)?.toDouble() ?? double.nan,
  );

  static List<AmapPlaceModel> fromEnvelope(Map<String, dynamic> json) {
    final raw = json['amapPlaces'] ?? json['AmapPlaces'];
    if (raw is! List<dynamic>) return const [];
    return raw
        .whereType<Map<String, dynamic>>()
        .map(AmapPlaceModel.fromJson)
        .where((place) => place.isValid)
        .toList(growable: false);
  }
}

class AssistantActionModel {
  const AssistantActionModel({
    required this.type,
    required this.provider,
    required this.label,
    required this.placeName,
    required this.latitude,
    required this.longitude,
    required this.coordinateSystem,
    required this.source,
    this.address,
    this.poiId,
    this.webUrl,
  });

  final String type;
  final String provider;
  final String label;
  final String placeName;
  final String? address;
  final double latitude;
  final double longitude;
  final String coordinateSystem;
  final String? poiId;
  final String source;
  final String? webUrl;

  bool get isSafe {
    if (provider != 'amap') return false;
    if (type == 'amap-trip-map') return _isTrustedAmapUrl(webUrl);
    return type == 'amap-navigation' &&
        coordinateSystem == 'GCJ02' &&
        latitude >= -90 &&
        latitude <= 90 &&
        longitude >= -180 &&
        longitude <= 180;
  }

  factory AssistantActionModel.fromJson(Map<String, dynamic> json) =>
      AssistantActionModel(
        type: json['type'] as String? ?? '',
        provider: json['provider'] as String? ?? '',
        label: json['label'] as String? ?? '',
        placeName: json['placeName'] as String? ?? '',
        address: json['address'] as String?,
        latitude: (json['latitude'] as num?)?.toDouble() ?? double.nan,
        longitude: (json['longitude'] as num?)?.toDouble() ?? double.nan,
        coordinateSystem: json['coordinateSystem'] as String? ?? '',
        poiId: json['poiId'] as String?,
        source: json['source'] as String? ?? '',
        webUrl: json['webUrl'] as String?,
      );

  static bool _isTrustedAmapUrl(String? value) {
    final uri = Uri.tryParse(value ?? '');
    if (uri == null || uri.scheme != 'https') return false;
    return uri.host == 'uri.amap.com' ||
        uri.host == 'm.amap.com' ||
        uri.host.endsWith('.amap.com');
  }

  static List<AssistantActionModel> fromEnvelope(Map<String, dynamic> json) {
    final raw = json['actions'] ?? json['Actions'];
    if (raw is! List<dynamic>) return const [];
    return raw
        .whereType<Map<String, dynamic>>()
        .map(AssistantActionModel.fromJson)
        .where((action) => action.isSafe)
        .toList(growable: false);
  }
}

class AiEvidenceRecord {
  const AiEvidenceRecord({required this.eventId, this.title, this.snippet});

  final int eventId;
  final String? title;
  final String? snippet;

  String get displayTitle {
    final explicitTitle = title?.trim();
    if (explicitTitle != null && explicitTitle.isNotEmpty) return explicitTitle;

    final source = snippet?.trim().replaceAll(RegExp(r'\s+'), ' ');
    if (source == null || source.isEmpty) return '查看记录';
    final firstSentence = source.split(RegExp(r'[。！？\n]')).first.trim();
    final codePoints = firstSentence.runes.toList(growable: false);
    if (codePoints.length <= 28) return firstSentence;
    return '${String.fromCharCodes(codePoints.take(28))}…';
  }

  factory AiEvidenceRecord.fromJson(Map<String, dynamic> json) =>
      AiEvidenceRecord(
        eventId: ((json['eventId'] ?? json['EventId']) as num).toInt(),
        title: ((json['title'] ?? json['Title']) as String?)?.trim(),
        snippet: ((json['snippet'] ?? json['Snippet']) as String?)?.trim(),
      );

  static List<AiEvidenceRecord> fromEnvelope(Map<String, dynamic> json) {
    final raw = json['records'] ?? json['Records'];
    if (raw is! List<dynamic>) return const [];
    return raw
        .whereType<Map<String, dynamic>>()
        .map(AiEvidenceRecord.fromJson)
        .toList(growable: false);
  }
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
