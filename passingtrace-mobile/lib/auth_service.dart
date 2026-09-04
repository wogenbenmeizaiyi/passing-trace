import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:app_links/app_links.dart';
import 'package:crypto/crypto.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:flutter_appauth/flutter_appauth.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'package:url_launcher/url_launcher.dart';

import 'build_environment.dart';

const mobileClientId = 'passingtrace-mobile';
const mobileRedirectUri = 'com.passingtrace.mobile:/oauth2redirect';
const minimumPasswordLength = 8;
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
    this.environment = BuildEnvironment.current,
  }) : _storage = storage ?? const FlutterSecureStorage(),
       _appAuth = appAuth ?? const FlutterAppAuth(),
       _http = httpClient ?? http.Client(),
       _appLinks = appLinks ?? AppLinks();

  static String get defaultIdentityUrl => BuildEnvironment.current.identityUrl;
  static String get defaultEventsApiUrl =>
      BuildEnvironment.current.eventsApiUrl;
  static const _buildChannelKey = 'build_channel';
  static const _identityUrlKey = 'identity_url';
  static const _eventsApiUrlKey = 'events_api_url';
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
  final BuildEnvironment environment;
  AuthSession? _latestSession;
  Future<AuthSession>? _refreshInFlight;

  Future<AuthSession?> restore() async {
    final values = await _storage.readAll();
    final storedChannel = values[_buildChannelKey];
    if (storedChannel != environment.channel) {
      // Legacy builds did not persist a channel. Preserve their local session
      // only for the internal channel; production must never inherit localhost
      // endpoints or credentials issued by a development Identity server.
      if (environment.isProduction || storedChannel != null) {
        await _storage.deleteAll();
        await _storage.write(key: _buildChannelKey, value: environment.channel);
        return null;
      }
      await _storage.write(key: _buildChannelKey, value: environment.channel);
    }
    final deviceId = values[_deviceIdKey];
    final deviceSecret = values[_deviceSecretKey];
    if (deviceId == null || deviceSecret == null) return null;

    final expiresText = values[_expiresAtKey];
    final session = AuthSession(
      identityBaseUrl: environment.allowEndpointOverrides
          ? values[_identityUrlKey] ?? environment.identityUrl
          : environment.identityUrl,
      deviceId: deviceId,
      deviceSecret: deviceSecret,
      accessToken: values[_accessTokenKey],
      refreshToken: values[_refreshTokenKey],
      idToken: values[_idTokenKey],
      accessTokenExpiration: expiresText == null
          ? null
          : DateTime.tryParse(expiresText),
    );
    _latestSession = session;
    return session;
  }

  Future<AuthSession> register({
    required String identityBaseUrl,
    required String username,
    required String password,
    required String bootstrapCode,
    required String deviceName,
  }) async {
    final baseUrl = _resolveIdentityUrl(identityBaseUrl);
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
    await _storage.write(key: _buildChannelKey, value: environment.channel);
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
    final baseUrl = _resolveIdentityUrl(identityBaseUrl);
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
    await _storage.write(key: _buildChannelKey, value: environment.channel);
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

  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async {
    final effective = _latestFor(current);
    final expiry = effective.accessTokenExpiration;
    if (!forceRefresh &&
        effective.hasToken &&
        expiry != null &&
        expiry.isAfter(
          DateTime.now().toUtc().add(const Duration(minutes: 1)),
        )) {
      return effective;
    }
    if (effective.refreshToken == null) return login(effective);

    // 多个页面可能同时发现令牌即将过期。Refresh Token 启用轮换后，同一
    // refresh token 只能可靠使用一次，因此所有调用必须共享同一个刷新任务。
    final inFlight = _refreshInFlight;
    if (inFlight != null) return inFlight;

    final refresh = _refresh(effective);
    _refreshInFlight = refresh;
    try {
      return await refresh;
    } finally {
      if (identical(_refreshInFlight, refresh)) {
        _refreshInFlight = null;
      }
    }
  }

  Future<AuthSession> _refresh(AuthSession current) async {
    late final TokenResponse result;
    try {
      result = await _appAuth.token(
        TokenRequest(
          mobileClientId,
          mobileRedirectUri,
          refreshToken: current.refreshToken,
          serviceConfiguration: _configuration(current.identityBaseUrl),
          scopes: mobileScopes,
          allowInsecureConnections: _allowsInsecure(current.identityBaseUrl),
        ),
      );
    } on PlatformException catch (error) {
      if (!_isInvalidGrant(error)) rethrow;
      await _clearTokens();
      throw const AuthSessionExpiredException();
    }
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

  Future<void> clearLocalAccount() async {
    _latestSession = null;
    _refreshInFlight = null;
    await _storage.deleteAll();
  }

  Future<void> _clearTokens() async {
    final latest = _latestSession;
    if (latest != null) {
      _latestSession = AuthSession(
        identityBaseUrl: latest.identityBaseUrl,
        deviceId: latest.deviceId,
        deviceSecret: latest.deviceSecret,
      );
    }
    await Future.wait([
      _storage.delete(key: _accessTokenKey),
      _storage.delete(key: _refreshTokenKey),
      _storage.delete(key: _idTokenKey),
      _storage.delete(key: _expiresAtKey),
    ]);
  }

  /// 读取已保存的 Events API 地址；若未保存则回落到默认值。
  Future<String> getEventsApiBaseUrl() async {
    if (!environment.allowEndpointOverrides) return environment.eventsApiUrl;
    final stored = await _storage.read(key: _eventsApiUrlKey);
    if (stored == null || stored.isEmpty) return environment.eventsApiUrl;
    return _normalizeBaseUrl(stored);
  }

  /// 持久化一个新的 Events API 地址。空字符串视作"重置为默认值"。
  Future<void> setEventsApiBaseUrl(String? value) async {
    if (!environment.allowEndpointOverrides) {
      await _storage.delete(key: _eventsApiUrlKey);
      return;
    }
    if (value == null || value.trim().isEmpty) {
      await _storage.delete(key: _eventsApiUrlKey);
      return;
    }
    final normalized = _normalizeBaseUrl(value);
    await _storage.write(key: _eventsApiUrlKey, value: normalized);
  }

  String _resolveIdentityUrl(String requestedUrl) =>
      environment.allowEndpointOverrides
      ? _normalizeBaseUrl(requestedUrl)
      : _normalizeBaseUrl(environment.identityUrl);

  Future<AuthSession> _authorize({
    required String baseUrl,
    required Uri authorizeUrl,
    required String verifier,
    required String expectedState,
    required String nonce,
    required String deviceId,
    required String deviceSecret,
  }) async {
    // 注册和账号密码登录已经由后端完成身份校验，handoff URL 只负责签发
    // 一次性授权码。直接读取它的 302 回调即可，避免 Android 再弹浏览器选择器。
    // 真正需要用户交互的设备重新认证仍使用系统 Custom Tab。
    final directCallback = await _tryResolveHandoff(authorizeUrl);
    final callback =
        directCallback ??
        await _launchAndWaitForCallback(authorizeUrl, expectedState);
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

  Future<Uri?> _tryResolveHandoff(Uri authorizeUrl) async {
    if (!authorizeUrl.queryParameters.containsKey('handoff_code')) return null;

    final request = http.Request('GET', authorizeUrl)..followRedirects = false;
    final response = await _http.send(request);
    if (response.statusCode < 300 || response.statusCode >= 400) {
      throw AuthException('登录授权失败：${response.statusCode}');
    }
    final location = response.headers['location'];
    if (location == null || location.isEmpty) {
      throw const AuthException('登录授权回调缺失。');
    }
    return authorizeUrl.resolve(location);
  }

  Future<Uri> _launchAndWaitForCallback(
    Uri authorizeUrl,
    String expectedState,
  ) async {
    final callbackFuture = _waitForCallback(expectedState);
    if (!await launchUrl(
      authorizeUrl,
      mode: LaunchMode.inAppBrowserView,
      browserConfiguration: const BrowserConfiguration(showTitle: false),
    )) {
      throw const AuthException('无法打开安全登录页。');
    }
    return callbackFuture.timeout(const Duration(minutes: 3));
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
    _latestSession = session;
    return session;
  }

  AuthSession _latestFor(AuthSession fallback) {
    final latest = _latestSession;
    if (latest == null ||
        latest.identityBaseUrl != fallback.identityBaseUrl ||
        latest.deviceId != fallback.deviceId) {
      return fallback;
    }
    return latest;
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
        if (problem['title'] == 'invalid_device') {
          throw const DeviceCredentialsInvalidException();
        }
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

  static bool _isInvalidGrant(PlatformException error) {
    final details =
        '${error.code} ${error.message ?? ''} ${error.details ?? ''}'
            .toLowerCase();
    return details.contains('invalid_grant');
  }

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

class AuthSessionExpiredException extends AuthException {
  const AuthSessionExpiredException() : super('登录状态已过期，请重新登录。');
}

class DeviceCredentialsInvalidException extends AuthException {
  const DeviceCredentialsInvalidException() : super('此手机的设备凭据已失效，请重新登录绑定。');
}

class _Pkce {
  const _Pkce(this.verifier, this.challenge);
  final String verifier;
  final String challenge;
}
