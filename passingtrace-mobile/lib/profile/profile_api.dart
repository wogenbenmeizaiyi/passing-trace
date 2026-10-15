import 'dart:convert';
import 'dart:typed_data';

import 'package:http/http.dart' as http;

import '../auth_service.dart';

class AccountProfile {
  const AccountProfile({
    required this.username,
    required this.nickname,
    required this.bio,
    required this.createdAt,
    required this.version,
    required this.hasAvatar,
  });
  final String username, nickname, bio, version;
  final DateTime createdAt;
  final bool hasAvatar;
  factory AccountProfile.fromJson(Map<String, dynamic> json) => AccountProfile(
    username: json['username'] as String,
    nickname: json['nickname'] as String,
    bio: json['bio'] as String? ?? '',
    createdAt: DateTime.parse(json['createdAt'] as String),
    version: json['version'] as String,
    hasAvatar: json['hasAvatar'] as bool? ?? false,
  );
}

class ProfileException implements Exception {
  const ProfileException(this.status);
  final int status;
  String get message => switch (status) {
    409 => '资料已在另一处更新。请重新加载后再修改，本次修改尚未保存。',
    401 || 403 => '登录状态已失效，请重新登录。',
    413 => '头像不能超过 5MB，请选择较小的图片。',
    429 => '保存过于频繁，请稍后再试。',
    400 => '请检查昵称、简介和头像格式后重试。',
    _ => '暂时无法连接账号服务，请稍后重试。已填写的内容会保留。',
  };
  @override
  String toString() => message;
}

class ProfileApi {
  ProfileApi(this.auth, {http.Client? client})
    : _client = client ?? http.Client();
  final AuthService auth;
  final http.Client _client;
  void dispose() => _client.close();

  Future<http.Response> _send(
    AuthSession session,
    http.BaseRequest Function(Uri) request,
    String path,
  ) async {
    try {
      var fresh = await auth.ensureFreshToken(session);
      for (var attempt = 0; attempt < 2; attempt++) {
        final uri = Uri.parse(
          '${session.identityBaseUrl.replaceFirst(RegExp(r'/$'), '')}/api/v1/account/$path',
        );
        final outgoing = request(uri)
          ..headers['Authorization'] = 'Bearer ${fresh.accessToken}';
        final response = await _client
            .send(outgoing)
            .then(http.Response.fromStream)
            .timeout(const Duration(seconds: 45));
        if (response.statusCode == 401 && attempt == 0) {
          fresh = await auth.ensureFreshToken(session, forceRefresh: true);
          continue;
        }
        if (response.statusCode != 200) {
          throw ProfileException(response.statusCode);
        }
        return response;
      }
      throw const ProfileException(401);
    } on ProfileException {
      rethrow;
    } on AuthSessionExpiredException {
      throw const ProfileException(401);
    } catch (_) {
      throw const ProfileException(0);
    }
  }

  Future<AccountProfile> get(AuthSession session) async =>
      AccountProfile.fromJson(
        jsonDecode(
          (await _send(
            session,
            (uri) => http.Request('GET', uri),
            'profile',
          )).body,
        ) as Map<String, dynamic>,
      );

  Future<Uint8List> avatar(AuthSession session) async => (await _send(
    session,
    (uri) => http.Request('GET', uri),
    'avatar',
  )).bodyBytes;

  Future<AccountProfile> save(
    AuthSession session, {
    required String nickname,
    required String bio,
    required String version,
    Uint8List? avatar,
    bool removeAvatar = false,
  }) async {
    final response = await _send(session, (uri) {
      final request = http.MultipartRequest('PUT', uri)
        ..fields.addAll({
          'nickname': nickname.trim(),
          'bio': bio.trim(),
          'version': version,
          'removeAvatar': '$removeAvatar',
        });
      if (avatar != null) {
        request.files.add(
          http.MultipartFile.fromBytes(
            'avatar',
            avatar,
            filename: 'avatar.png',
          ),
        );
      }
      return request;
    }, 'profile');
    return AccountProfile.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
  }
}
