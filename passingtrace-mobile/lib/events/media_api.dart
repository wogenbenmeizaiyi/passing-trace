import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:crypto/crypto.dart';
import 'package:file_picker/file_picker.dart';
import 'package:http/http.dart' as http;

import '../auth_service.dart';
import 'event_model.dart';
import 'events_api.dart';

typedef UploadProgress = void Function(double value);

class PendingMediaUpload {
  const PendingMediaUpload({
    required this.file,
    required this.contentType,
    required this.id,
    required this.kind,
  });

  final PlatformFile file;
  final String contentType;
  final String id;
  final MediaKind kind;
}

/// 私有附件上传客户端。API 只负责签名；文件内容直接 PUT 到 MinIO/S3。
class MediaApiClient {
  MediaApiClient({
    required this.auth,
    required this.baseUrl,
    http.Client? httpClient,
  }) : _http = httpClient ?? http.Client();

  static const int _multipartThreshold = 100 * 1024 * 1024;

  final AuthService auth;
  final String baseUrl;
  final http.Client _http;

  Uri _resolve(String path) {
    final root = baseUrl.endsWith('/')
        ? baseUrl.substring(0, baseUrl.length - 1)
        : baseUrl;
    return Uri.parse('$root$path');
  }

  Future<Map<String, String>> _headers(AuthSession session) async {
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
    return {
      'Authorization': 'Bearer $token',
      'Accept': 'application/json',
      'Content-Type': 'application/json',
    };
  }

  Future<PendingMediaUpload> upload(
    AuthSession session,
    PlatformFile file, {
    UploadProgress? onProgress,
  }) async {
    final size = await file.length();
    final contentType = _mimeFor(file.name);
    final digest = await sha256.bind(file.readAsByteStream()).first;
    final create = _json(
      await _http.post(
        _resolve('/api/v1/media/uploads'),
        headers: await _headers(session),
        body: jsonEncode({
          'fileName': file.name,
          'contentType': contentType,
          'size': size,
          'sha256': digest.toString(),
        }),
      ),
      const {201},
    );

    final id = create['id'] as String;
    final mode = (create['mode'] as num).toInt();
    final kind = MediaKind.fromValue((create['kind'] as num).toInt());
    if (mode == 1 && size < _multipartThreshold) {
      await _putWithRetry(
        Uri.parse(create['uploadUrl'] as String),
        file.readAsByteStream(),
        size,
        contentType: contentType,
        onProgress: onProgress,
      );
      await _confirm(session, id, const []);
    } else {
      final partSize = (create['partSize'] as num).toInt();
      final chunks = _parts(file.readAsByteStream(), partSize);
      final parts = <Map<String, Object>>[];
      var partNumber = 0;
      var sent = 0;
      await for (final bytes in chunks) {
        partNumber++;
        final signed = _json(
          await _http.post(
            _resolve('/api/v1/media/$id/parts'),
            headers: await _headers(session),
            body: jsonEncode({'partNumber': partNumber}),
          ),
          const {200},
        );
        final response = await _putWithRetry(
          Uri.parse(signed['uploadUrl'] as String),
          Stream<Uint8List>.value(bytes),
          bytes.length,
          onProgress: (partProgress) {
            onProgress?.call((sent + bytes.length * partProgress) / size);
          },
        );
        final etag = response.headers['etag'];
        if (etag == null || etag.isEmpty) {
          throw const EventApiException(
            status: 502,
            message: '对象存储未返回分片 ETag。',
          );
        }
        parts.add({'partNumber': partNumber, 'eTag': etag});
        sent += bytes.length;
        onProgress?.call(sent / size);
      }
      await _confirm(session, id, parts);
    }

    onProgress?.call(1);
    return PendingMediaUpload(
      file: file,
      contentType: contentType,
      id: id,
      kind: kind,
    );
  }

  Future<void> _confirm(
    AuthSession session,
    String id,
    List<Map<String, Object>> parts,
  ) async {
    _json(
      await _http.post(
        _resolve('/api/v1/media/$id/confirm'),
        headers: await _headers(session),
        body: jsonEncode({'parts': parts.isEmpty ? null : parts}),
      ),
      const {200},
    );
  }

  Future<Uri> access(AuthSession session, String id) async {
    final raw = _json(
      await _http.get(
        _resolve('/api/v1/media/$id/access'),
        headers: await _headers(session),
      ),
      const {200},
    );
    return Uri.parse(raw['url'] as String);
  }

  Future<void> delete(AuthSession session, String id) async {
    final response = await _http.delete(
      _resolve('/api/v1/media/$id'),
      headers: await _headers(session),
    );
    if (response.statusCode != 204 && response.statusCode != 404) {
      _json(response, const {204, 404});
    }
  }

  Future<http.StreamedResponse> _putWithRetry(
    Uri uri,
    Stream<Uint8List> source,
    int length, {
    String? contentType,
    UploadProgress? onProgress,
  }) async {
    // 每个分片在内存中，单次上传的文件流不可重放，因此仅网络返回失败时
    // 由上层重新选择/重试；分片会在本方法中最多重试三次。
    final bytes = length <= 16 * 1024 * 1024
        ? await _collect(source, length)
        : null;
    for (var attempt = 1; attempt <= 3; attempt++) {
      var sent = 0;
      final stream = bytes == null ? source : Stream<Uint8List>.value(bytes);
      final request = http.StreamedRequest('PUT', uri)..contentLength = length;
      if (contentType != null) request.headers['Content-Type'] = contentType;
      final forwarding = stream.map((chunk) {
        sent += chunk.length;
        onProgress?.call(sent / length);
        return chunk;
      });
      unawaited(
        request.sink.addStream(forwarding).then((_) => request.sink.close()),
      );
      final response = await _http.send(request);
      if (response.statusCode >= 200 && response.statusCode < 300) {
        return response;
      }
      await response.stream.drain<void>();
      if (attempt == 3 || bytes == null) {
        throw EventApiException(
          status: response.statusCode,
          message: '上传对象失败：${response.statusCode}',
        );
      }
    }
    throw StateError('unreachable');
  }

  static Stream<Uint8List> _parts(
    Stream<Uint8List> source,
    int partSize,
  ) async* {
    var builder = BytesBuilder(copy: false);
    await for (final chunk in source) {
      var offset = 0;
      while (offset < chunk.length) {
        final take = (partSize - builder.length)
            .clamp(0, chunk.length - offset)
            .toInt();
        builder.add(Uint8List.sublistView(chunk, offset, offset + take));
        offset += take;
        if (builder.length == partSize) {
          yield builder.takeBytes();
          builder = BytesBuilder(copy: false);
        }
      }
    }
    if (builder.isNotEmpty) yield builder.takeBytes();
  }

  static Future<Uint8List> _collect(
    Stream<Uint8List> stream,
    int length,
  ) async {
    final builder = BytesBuilder(copy: false);
    await for (final chunk in stream) {
      builder.add(chunk);
    }
    final bytes = builder.takeBytes();
    if (bytes.length != length) throw StateError('文件读取长度发生变化。');
    return bytes;
  }

  static Map<String, dynamic> _json(http.Response response, Set<int> success) {
    if (success.contains(response.statusCode)) {
      return response.body.isEmpty
          ? <String, dynamic>{}
          : jsonDecode(response.body) as Map<String, dynamic>;
    }
    String message = '请求失败：${response.statusCode}';
    try {
      final raw = jsonDecode(response.body) as Map<String, dynamic>;
      message = (raw['detail'] ?? raw['title'] ?? message) as String;
    } catch (_) {}
    throw EventApiException(status: response.statusCode, message: message);
  }

  static String _mimeFor(String fileName) {
    final extension = fileName.split('.').last.toLowerCase();
    return switch (extension) {
      'jpg' || 'jpeg' => 'image/jpeg',
      'png' => 'image/png',
      'webp' => 'image/webp',
      'mp4' => 'video/mp4',
      'mov' => 'video/quicktime',
      'webm' => 'video/webm',
      'pdf' => 'application/pdf',
      'doc' => 'application/msword',
      'docx' => 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      'xls' => 'application/vnd.ms-excel',
      'xlsx' =>
        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      'ppt' => 'application/vnd.ms-powerpoint',
      'pptx' => 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
      'txt' || 'md' || 'csv' => 'text/plain',
      'json' => 'application/json',
      'zip' => 'application/zip',
      _ => 'application/octet-stream',
    };
  }

  void close() => _http.close();
}
