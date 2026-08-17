import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:app_links/app_links.dart';
import 'package:crypto/crypto.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_appauth/flutter_appauth.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'package:url_launcher/url_launcher.dart';

const mobileClientId = 'passingtrace-mobile';
const mobileRedirectUri = 'com.passingtrace.mobile:/oauth2redirect';
const mobileScopes = <String>[
  'openid',
  'profile',
  'offline_access',
  'passingtrace.api',
  'passingtrace.identity.login-approve',
];

class AuthSession {
  const AuthSession({
    required this.identityBaseUrl,
    required this.deviceId,
    required this.deviceSecret,
    this.accessToken,
    this.refreshToken,
    this.idToken,
    this.accessTokenExpiration,
  });

  final String identityBaseUrl;
  final String deviceId;
  final String deviceSecret;
  final String? accessToken;
  final String? refreshToken;
  final String? idToken;
  final DateTime? accessTokenExpiration;

  bool get hasToken => accessToken != null && accessToken!.isNotEmpty;
}

class AuthService {
  AuthService({
    FlutterSecureStorage? storage,
    FlutterAppAuth? appAuth,
    http.Client? httpClient,
    AppLinks? appLinks,
  }) : _storage = storage ?? const FlutterSecureStorage(),
       _appAuth = appAuth ?? const FlutterAppAuth(),
       _http = httpClient ?? http.Client(),
       _appLinks = appLinks ?? AppLinks();

  static const defaultIdentityUrl = 'http://192.168.31.210:56229';
  static const _identityUrlKey = 'identity_url';
  static const _deviceIdKey = 'device_id';
  static const _deviceSecretKey = 'device_secret';
  static const _accessTokenKey = 'access_token';
  static const _refreshTokenKey = 'refresh_token';
  static const _idTokenKey = 'id_token';
  static const _expiresAtKey = 'access_token_expires_at';

  final FlutterSecureStorage _storage;
  final FlutterAppAuth _appAuth;
  final http.Client _http;
  final AppLinks _appLinks;

  Future<AuthSession?> restore() async {
    final values = await _storage.readAll();
    final deviceId = values[_deviceIdKey];
    final deviceSecret = values[_deviceSecretKey];
    if (deviceId == null || deviceSecret == null) return null;

    final expiresText = values[_expiresAtKey];
    return AuthSession(
      identityBaseUrl: values[_identityUrlKey] ?? defaultIdentityUrl,
      deviceId: deviceId,
      deviceSecret: deviceSecret,
      accessToken: values[_accessTokenKey],
      refreshToken: values[_refreshTokenKey],
      idToken: values[_idTokenKey],
      accessTokenExpiration: expiresText == null
          ? null
          : DateTime.tryParse(expiresText),
    );
  }

  Future<AuthSession> register({
    required String identityBaseUrl,
    required String username,
    required String password,
    required String bootstrapCode,
    required String deviceName,
  }) async {
    final baseUrl = _normalizeBaseUrl(identityBaseUrl);
    final pkce = _createPkce();
    final state = _randomSecret(32);
    final nonce = _randomSecret(32);

    final intent = await _postJson('$baseUrl/api/mobile/registration-intents', {
      'username': username,
      'clientId': mobileClientId,
      'redirectUri': mobileRedirectUri,
      'codeChallenge': pkce.challenge,
      'state': state,
      'nonce': nonce,
    });

    final registration = await _postJson(
      '$baseUrl/api/mobile/registrations',
      {
        'intentId': intent['intentId'],
        'username': username,
        'password': password,
        'bootstrapCode': bootstrapCode,
        'deviceName': deviceName,
      },
      expectedStatuses: const {201},
    );

    await _storage.write(key: _identityUrlKey, value: baseUrl);
    await _storage.write(
      key: _deviceIdKey,
      value: registration['deviceId'] as String,
    );
    await _storage.write(
      key: _deviceSecretKey,
      value: registration['deviceSecret'] as String,
    );

    final authorizeUrl = Uri.parse(registration['authorizeUrl'] as String);
    final session = await _authorize(
      baseUrl: baseUrl,
      authorizeUrl: authorizeUrl,
      verifier: pkce.verifier,
      expectedState: state,
      nonce: nonce,
      deviceId: registration['deviceId'] as String,
      deviceSecret: registration['deviceSecret'] as String,
    );
    return session;
  }

  Future<AuthSession> loginWithPassword({
    required String identityBaseUrl,
    required String username,
    required String password,
    required String deviceName,
  }) async {
    final baseUrl = _normalizeBaseUrl(identityBaseUrl);
    final pkce = _createPkce();
    final state = _randomSecret(32);
    final nonce = _randomSecret(32);
    final response = await _postJson('$baseUrl/api/mobile/logins', {
      'username': username,
      'password': password,
      'clientId': mobileClientId,
      'redirectUri': mobileRedirectUri,
      'codeChallenge': pkce.challenge,
      'state': state,
      'nonce': nonce,
      'deviceName': deviceName,
    });

    await _storage.write(key: _identityUrlKey, value: baseUrl);
    await _storage.write(
      key: _deviceIdKey,
      value: response['deviceId'] as String,
    );
    await _storage.write(
      key: _deviceSecretKey,
      value: response['deviceSecret'] as String,
    );

    return _authorize(
      baseUrl: baseUrl,
      authorizeUrl: Uri.parse(response['authorizeUrl'] as String),
      verifier: pkce.verifier,
      expectedState: state,
      nonce: nonce,
      deviceId: response['deviceId'] as String,
      deviceSecret: response['deviceSecret'] as String,
    );
  }

  Future<AuthSession> login(AuthSession current) async {
    final pkce = _createPkce();
    final state = _randomSecret(32);
    final nonce = _randomSecret(32);
    final response = await _postJson(
      '${current.identityBaseUrl}/api/mobile/authorization-launches',
      {
        'clientId': mobileClientId,
        'redirectUri': mobileRedirectUri,
        'codeChallenge': pkce.challenge,
        'state': state,
        'nonce': nonce,
        'deviceId': current.deviceId,
        'deviceSecret': current.deviceSecret,
      },
    );

    return _authorize(
      baseUrl: current.identityBaseUrl,
      authorizeUrl: Uri.parse(response['authorizeUrl'] as String),
      verifier: pkce.verifier,
      expectedState: state,
      nonce: nonce,
      deviceId: current.deviceId,
      deviceSecret: current.deviceSecret,
    );
  }

  Future<AuthSession> ensureFreshToken(AuthSession current) async {
    final expiry = current.accessTokenExpiration;
    if (current.hasToken &&
        expiry != null &&
        expiry.isAfter(
          DateTime.now().toUtc().add(const Duration(minutes: 1)),
        )) {
      return current;
    }
    if (current.refreshToken == null) return login(current);

    final result = await _appAuth.token(
      TokenRequest(
        mobileClientId,
        mobileRedirectUri,
        refreshToken: current.refreshToken,
        serviceConfiguration: _configuration(current.identityBaseUrl),
        scopes: mobileScopes,
        allowInsecureConnections: _allowsInsecure(current.identityBaseUrl),
      ),
    );
    if (result.accessToken == null) {
      throw const AuthException('刷新登录状态失败。');
    }
    return _saveTokens(current, result);
  }

  Future<QrLoginDetails> getQrDetails(
    AuthSession session,
    String rawValue,
  ) async {
    final fresh = await ensureFreshToken(session);
    final qrUri = _validateQrUri(fresh.identityBaseUrl, rawValue);
    final response = await _http.get(
      Uri.parse(
        '${fresh.identityBaseUrl}/api/qr-login/transactions/'
        '${Uri.encodeComponent(qrUri.queryParameters['code']!)}',
      ),
      headers: {'Authorization': 'Bearer ${fresh.accessToken}'},
    );
    final json = _decodeResponse(response, const {200});
    return QrLoginDetails(
      code: qrUri.queryParameters['code']!,
      clientId: json['clientId'] as String,
      clientDisplayName: json['clientDisplayName'] as String,
      browser: json['browser'] as String? ?? '未知浏览器',
      sourceIp: json['sourceIp'] as String? ?? '未知地址',
      expiresAt: DateTime.parse(json['expiresAt'] as String),
      session: fresh,
    );
  }

  Future<AuthSession> decideQr(QrLoginDetails details, bool approve) async {
    final response = await _http.post(
      Uri.parse(
        '${details.session.identityBaseUrl}/api/qr-login/transactions/'
        '${Uri.encodeComponent(details.code)}/${approve ? 'approve' : 'reject'}',
      ),
      headers: {'Authorization': 'Bearer ${details.session.accessToken}'},
    );
    _decodeResponse(response, const {200});
    return details.session;
  }

  Future<void> clearLocalAccount() => _storage.deleteAll();

  Future<AuthSession> _authorize({
    required String baseUrl,
    required Uri authorizeUrl,
    required String verifier,
    required String expectedState,
    required String nonce,
    required String deviceId,
    required String deviceSecret,
  }) async {
    final callbackFuture = _waitForCallback(expectedState);
    if (!await launchUrl(authorizeUrl, mode: LaunchMode.externalApplication)) {
      throw const AuthException('无法打开系统浏览器。');
    }

    final callback = await callbackFuture.timeout(const Duration(minutes: 3));
    final error = callback.queryParameters['error'];
    if (error != null) {
      throw AuthException(
        callback.queryParameters['error_description'] ?? error,
      );
    }
    if (callback.queryParameters['state'] != expectedState) {
      throw const AuthException('登录回调 state 不匹配，已拒绝。');
    }
    final code = callback.queryParameters['code'];
    if (code == null || code.isEmpty) {
      throw const AuthException('登录回调缺少授权码。');
    }

    final token = await _appAuth.token(
      TokenRequest(
        mobileClientId,
        mobileRedirectUri,
        authorizationCode: code,
        codeVerifier: verifier,
        nonce: nonce,
        serviceConfiguration: _configuration(baseUrl),
        allowInsecureConnections: _allowsInsecure(baseUrl),
      ),
    );
    if (token.accessToken == null) {
      throw const AuthException('授权码未能换取 Token。');
    }
    return _saveTokens(
      AuthSession(
        identityBaseUrl: baseUrl,
        deviceId: deviceId,
        deviceSecret: deviceSecret,
      ),
      token,
    );
  }

  Future<Uri> _waitForCallback(String expectedState) async {
    bool isExpectedCallback(Uri uri) =>
        uri.scheme == 'com.passingtrace.mobile' &&
        uri.path == '/oauth2redirect' &&
        uri.queryParameters['state'] == expectedState;

    final initial = await _appLinks.getInitialLink();
    if (initial != null && isExpectedCallback(initial)) {
      return initial;
    }
    return _appLinks.uriLinkStream.firstWhere(isExpectedCallback);
  }

  Future<AuthSession> _saveTokens(AuthSession base, TokenResponse token) async {
    final session = AuthSession(
      identityBaseUrl: base.identityBaseUrl,
      deviceId: base.deviceId,
      deviceSecret: base.deviceSecret,
      accessToken: token.accessToken,
      refreshToken: token.refreshToken ?? base.refreshToken,
      idToken: token.idToken ?? base.idToken,
      accessTokenExpiration: token.accessTokenExpirationDateTime,
    );
    await Future.wait([
      _storage.write(key: _accessTokenKey, value: session.accessToken),
      _storage.write(key: _refreshTokenKey, value: session.refreshToken),
      _storage.write(key: _idTokenKey, value: session.idToken),
      _storage.write(
        key: _expiresAtKey,
        value: session.accessTokenExpiration?.toUtc().toIso8601String(),
      ),
    ]);
    return session;
  }

  Future<Map<String, dynamic>> _postJson(
    String url,
    Map<String, Object?> body, {
    Set<int> expectedStatuses = const {200},
  }) async {
    final response = await _http.post(
      Uri.parse(url),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode(body),
    );
    return _decodeResponse(response, expectedStatuses);
  }

  Map<String, dynamic> _decodeResponse(
    http.Response response,
    Set<int> expectedStatuses,
  ) {
    if (!expectedStatuses.contains(response.statusCode)) {
      try {
        final problem = jsonDecode(response.body) as Map<String, dynamic>;
        throw AuthException(
          problem['detail'] as String? ??
              problem['title'] as String? ??
              '请求失败：${response.statusCode}',
        );
      } on FormatException {
        throw AuthException('请求失败：${response.statusCode}');
      }
    }
    return jsonDecode(response.body) as Map<String, dynamic>;
  }

  Uri _validateQrUri(String identityBaseUrl, String rawValue) {
    final uri = Uri.tryParse(rawValue.trim());
    final expected = Uri.parse(identityBaseUrl);
    final developmentHttp = kDebugMode && uri?.scheme == 'http';
    if (uri == null ||
        (uri.scheme != 'https' && !developmentHttp) ||
        uri.host != expected.host ||
        uri.port != expected.port ||
        uri.path != '/mobile/qr-login' ||
        uri.queryParameters['v'] != '1' ||
        uri.queryParameters.length != 2 ||
        uri.fragment.isNotEmpty ||
        !_isBase64Url256(uri.queryParameters['code'])) {
      throw const AuthException('这不是当前 Identity 服务生成的有效登录二维码。');
    }
    return uri;
  }

  AuthorizationServiceConfiguration _configuration(String baseUrl) =>
      AuthorizationServiceConfiguration(
        authorizationEndpoint: '$baseUrl/connect/authorize',
        tokenEndpoint: '$baseUrl/connect/token',
        endSessionEndpoint: '$baseUrl/connect/logout',
      );

  static String _normalizeBaseUrl(String value) {
    final uri = Uri.parse(value.trim());
    if (!uri.hasScheme || uri.host.isEmpty) {
      throw const AuthException('Identity 地址格式不正确。');
    }
    return uri
        .replace(path: '', query: null, fragment: null)
        .toString()
        .replaceAll(RegExp(r'/$'), '');
  }

  static bool _allowsInsecure(String baseUrl) =>
      Uri.parse(baseUrl).scheme == 'http';

  static bool _isBase64Url256(String? value) =>
      value != null && RegExp(r'^[A-Za-z0-9_-]{43}$').hasMatch(value);

  static _Pkce _createPkce() {
    final verifier = _randomSecret(64);
    final challenge = base64Url
        .encode(sha256.convert(ascii.encode(verifier)).bytes)
        .replaceAll('=', '');
    return _Pkce(verifier, challenge);
  }

  static String _randomSecret(int byteLength) {
    final random = Random.secure();
    return base64Url
        .encode(List<int>.generate(byteLength, (_) => random.nextInt(256)))
        .replaceAll('=', '');
  }
}

class QrLoginDetails {
  const QrLoginDetails({
    required this.code,
    required this.clientId,
    required this.clientDisplayName,
    required this.browser,
    required this.sourceIp,
    required this.expiresAt,
    required this.session,
  });

  final String code;
  final String clientId;
  final String clientDisplayName;
  final String browser;
  final String sourceIp;
  final DateTime expiresAt;
  final AuthSession session;
}

class AuthException implements Exception {
  const AuthException(this.message);
  final String message;
  @override
  String toString() => message;
}

class _Pkce {
  const _Pkce(this.verifier, this.challenge);
  final String verifier;
  final String challenge;
}
