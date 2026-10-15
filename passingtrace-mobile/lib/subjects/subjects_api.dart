import '../auth_service.dart';
import '../events/events_api.dart';
import '../events/ai_api.dart';
import 'subject_model.dart';

class SubjectsApi {
  SubjectsApi({
    required AuthService auth,
    required String baseUrl,
    EventApiClient? client,
  }) : _client = client ?? EventApiClient(auth: auth, baseUrl: baseUrl);
  final EventApiClient _client;
  static const _root = '/api/v1/subjects';
  Future<Map<String, dynamic>> _map(
    AuthSession session,
    String method,
    String path, {
    Object? body,
    int? version,
    String? key,
    Map<String, Object?>? query,
  }) async => Map<String, dynamic>.from(
    await _client.requestJson(
      session,
      method,
      path,
      body: body,
      query: query,
      headers: {
        if (version != null) 'If-Match': '"$version"',
        'Idempotency-Key': ?key,
      },
    ) as Map,
  );
  Future<List<SubjectModel>> list(AuthSession s) async =>
      (await _client.requestJson(s, 'GET', _root) as List)
          .map((x) => SubjectModel(Map<String, dynamic>.from(x as Map)))
          .toList();
  Future<SubjectGraph> graph(AuthSession s) async =>
      SubjectGraph.fromJson(await _map(s, 'GET', '$_root/graph'));
  Future<SubjectModel> get(AuthSession s, String id) async =>
      SubjectModel(await _map(s, 'GET', '$_root/$id'));
  Future<List<SubjectField>> presets(
    AuthSession s,
    int kind,
    String? itemType,
  ) async =>
      (await _client.requestJson(
            s,
            'GET',
            '$_root/presets',
            query: {'kind': kind, 'itemType': itemType},
          ) as List)
          .map(
            (x) => SubjectField.fromJson(Map<String, dynamic>.from(x as Map)),
          )
          .toList();
  Future<SubjectModel> create(
    AuthSession s,
    Map<String, dynamic> body,
    String key,
  ) async => SubjectModel(await _map(s, 'POST', _root, body: body, key: key));
  Future<SubjectModel> update(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async => SubjectModel(
    await _map(s, 'PATCH', '$_root/$id', body: body, version: version),
  );
  Future<Map<String, dynamic>> timeline(
    AuthSession s,
    String id,
    Map<String, Object?> query,
  ) => _map(s, 'GET', '$_root/$id/timeline', query: query);
  Future<SubjectGraph> relate(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async => SubjectGraph.fromJson(
    await _map(s, 'POST', '$_root/$id/relations', body: body, version: version),
  );
  Future<void> updateRelation(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async {
    await _map(
      s,
      'PATCH',
      '$_root/relations/$id',
      body: body,
      version: version,
    );
  }

  Future<SubjectEntry> entry(AuthSession s, String id) async =>
      SubjectEntry(await _map(s, 'GET', '$_root/entries/$id'));
  Future<SubjectEntry> createEntry(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    String key,
  ) async => SubjectEntry(
    await _map(s, 'POST', '$_root/$id/entries', body: body, key: key),
  );
  Future<SubjectEntry> updateEntry(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async => SubjectEntry(
    await _map(s, 'PATCH', '$_root/entries/$id', body: body, version: version),
  );
  Future<SubjectEntry> decideEntry(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async => SubjectEntry(
    await _map(
      s,
      'POST',
      '$_root/entries/$id/decision',
      body: body,
      version: version,
    ),
  );
  Future<Map<String, dynamic>> previewLifecycle(AuthSession s, String id) =>
      _map(s, 'GET', '$_root/$id/lifecycle/preview');
  Future<SubjectModel> lifecycle(
    AuthSession s,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async => SubjectModel(
    await _map(s, 'POST', '$_root/$id/lifecycle', body: body, version: version),
  );
  Future<AiApprovalModel> requestDelete(
    AuthSession s,
    String type,
    String id,
    String requestId,
  ) async => AiApprovalModel.fromJson(
    await _map(
      s,
      'POST',
      '$_root/delete-requests',
      body: {'targetType': type, 'targetId': id, 'requestId': requestId},
    ),
  );
  Future<AiMutationResultModel> decideDelete(
    AuthSession s,
    AiApprovalModel approval,
    String decision,
  ) async {
    if (decision != 'confirm' && decision != 'cancel') {
      throw ArgumentError.value(decision, 'decision');
    }
    final response = await _map(
      s,
      'POST',
      '/api/v1/ai/conversations/${approval.conversationId}/approvals/${approval.id}/decision',
      body: {'decision': decision},
    );
    return AiMutationResultModel.fromJson(
      Map<String, dynamic>.from(response['result'] as Map),
    );
  }

  void close() => _client.close();
}
