import 'dart:convert';
import 'dart:typed_data';

import 'package:crypto/crypto.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/events/media_api.dart';

base class _MemoryPlatformFile extends PlatformFile {
  _MemoryPlatformFile(this.name, List<int> bytes)
    : _bytes = Uint8List.fromList(bytes),
      uri = Uri.parse('memory:///$name');

  @override
  final String name;
  final Uint8List _bytes;

  @override
  final Uri uri;

  @override
  Never get xFile => throw UnimplementedError();

  @override
  Future<int> length() async => _bytes.length;

  @override
  Future<Uint8List> readAsBytes() async => Uint8List.fromList(_bytes);

  @override
  Stream<Uint8List> readAsByteStream() =>
      Stream<Uint8List>.value(Uint8List.fromList(_bytes));
}

class _FakeAuthService extends AuthService {
  @override
  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async => current;
}

AuthSession _session() => AuthSession(
  identityBaseUrl: 'https://id.test',
  deviceId: 'dev-1',
  deviceSecret: 'secret',
  accessToken: 'access-token',
  accessTokenExpiration: DateTime.now().add(const Duration(minutes: 10)),
);

void main() {
  test('single upload 计算 SHA-256、直传对象并确认', () async {
    final source = _MemoryPlatformFile('photo.png', [1, 2, 3, 4]);
    final requests = <http.Request>[];
    final client = MockClient((request) async {
      requests.add(request);
      if (request.url.path == '/api/v1/media/uploads') {
        return http.Response(
          jsonEncode({
            'id': 'media-1',
            'kind': 1,
            'mode': 1,
            'uploadUrl': 'https://storage.test/object',
            'partSize': null,
            'partCount': null,
            'expiresAt': '2026-08-30T12:00:00Z',
          }),
          201,
        );
      }
      if (request.method == 'PUT') {
        expect(request.bodyBytes, [1, 2, 3, 4]);
        expect(request.headers['Content-Type'], 'image/png');
        return http.Response('', 200, headers: {'etag': 'single-etag'});
      }
      if (request.url.path == '/api/v1/media/media-1/confirm') {
        expect(jsonDecode(request.body), {'parts': null});
        return http.Response('{}', 200);
      }
      return http.Response('unexpected', 500);
    });
    final api = MediaApiClient(
      auth: _FakeAuthService(),
      baseUrl: 'https://events.test',
      httpClient: client,
    );

    final result = await api.upload(_session(), source);

    expect(result.id, 'media-1');
    final create = jsonDecode(requests.first.body) as Map<String, dynamic>;
    expect(create['sha256'], sha256.convert([1, 2, 3, 4]).toString());
    expect(requests.first.headers['Authorization'], 'Bearer access-token');
    api.close();
  });

  test('multipart upload 按顺序上传并确认所有 ETag', () async {
    final source = _MemoryPlatformFile('note.txt', [1, 2, 3, 4, 5]);
    var nextPart = 0;
    Map<String, dynamic>? confirmBody;
    final client = MockClient((request) async {
      if (request.url.path == '/api/v1/media/uploads') {
        return http.Response(
          jsonEncode({
            'id': 'media-2',
            'kind': 3,
            'mode': 2,
            'uploadUrl': null,
            'partSize': 3,
            'partCount': 2,
            'expiresAt': '2026-08-30T12:00:00Z',
          }),
          201,
        );
      }
      if (request.url.path == '/api/v1/media/media-2/parts') {
        nextPart =
            (jsonDecode(request.body) as Map<String, dynamic>)['partNumber']
                as int;
        return http.Response(
          jsonEncode({
            'partNumber': nextPart,
            'uploadUrl': 'https://storage.test/part-$nextPart',
          }),
          200,
        );
      }
      if (request.method == 'PUT') {
        final part = int.parse(request.url.pathSegments.last.split('-').last);
        expect(request.bodyBytes.length, part == 1 ? 3 : 2);
        return http.Response('', 200, headers: {'etag': 'etag-$part'});
      }
      if (request.url.path == '/api/v1/media/media-2/confirm') {
        confirmBody = jsonDecode(request.body) as Map<String, dynamic>;
        return http.Response('{}', 200);
      }
      return http.Response('unexpected', 500);
    });
    final api = MediaApiClient(
      auth: _FakeAuthService(),
      baseUrl: 'https://events.test',
      httpClient: client,
    );

    await api.upload(_session(), source);

    expect(confirmBody, {
      'parts': [
        {'partNumber': 1, 'eTag': 'etag-1'},
        {'partNumber': 2, 'eTag': 'etag-2'},
      ],
    });
    api.close();
  });
}
