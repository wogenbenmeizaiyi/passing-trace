import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;

import '../auth_service.dart';
import '../events/events_api.dart';

typedef SocialRow = Map<String, dynamic>;
bool showSocialMessageTime(SocialRow current, SocialRow? previous) {
  if (previous == null) return true;
  final now = DateTime.parse(current['createdAt'] as String).toLocal();
  final before = DateTime.parse(previous['createdAt'] as String).toLocal();
  return now.year != before.year ||
      now.month != before.month ||
      now.day != before.day ||
      now.difference(before) > const Duration(minutes: 5);
}

String socialMessageTime(String value, {bool full = false}) {
  final date = DateTime.tryParse(value)?.toLocal();
  if (date == null) return '';
  final now = DateTime.now();
  final day =
      date.year == now.year && date.month == now.month && date.day == now.day;
  final clock =
      '${date.hour.toString().padLeft(2, '0')}:${date.minute.toString().padLeft(2, '0')}';
  return day && !full
      ? clock
      : '${date.month}月${date.day}日${full ? ' $clock' : ''}';
}

List<SocialRow> socialRows(dynamic value) => (value as List? ?? [])
    .map((x) => Map<String, dynamic>.from(x as Map))
    .toList();
String socialName(SocialRow friend) =>
    (friend['remark'] as String?)?.isNotEmpty == true
    ? friend['remark'] as String
    : (friend['person'] as Map?)?['nickname'] as String? ?? '好友';
String messageKey() {
  final r = Random.secure();
  final bytes = List.generate(16, (_) => r.nextInt(256));
  bytes[6] = (bytes[6] & 15) | 64;
  bytes[8] = (bytes[8] & 63) | 128;
  final h = bytes.map((x) => x.toRadixString(16).padLeft(2, '0')).join();
  return '${h.substring(0, 8)}-${h.substring(8, 12)}-${h.substring(12, 16)}-${h.substring(16, 20)}-${h.substring(20)}';
}

class SocialApi {
  SocialApi(this.auth, this.session, {http.Client? client})
    : _http = client ?? http.Client();
  final AuthService auth;
  final AuthSession session;
  final http.Client _http;
  bool _closed = false;
  Future<Uri> _uri(String path, bool identity) async {
    final base = identity
        ? session.identityBaseUrl
        : await auth.getEventsApiBaseUrl();
    return Uri.parse('${base.replaceAll(RegExp(r'/$'), '')}$path');
  }

  Future<http.StreamedResponse> _send(
    String method,
    String path, {
    Object? body,
    bool identity = false,
    bool refresh = false,
  }) async {
    final fresh = await auth.ensureFreshToken(session, forceRefresh: refresh);
    final request = http.Request(method, await _uri(path, identity));
    request.headers.addAll({
      'Authorization': 'Bearer ${fresh.accessToken}',
      'Content-Type': 'application/json',
    });
    if (body != null) request.body = jsonEncode(body);
    final response = await _http
        .send(request)
        .timeout(const Duration(seconds: 20));
    if (response.statusCode == 401 && !refresh) {
      await response.stream.drain<void>();
      return _send(method, path, body: body, identity: identity, refresh: true);
    }
    return response;
  }

  Future<dynamic> request(
    String method,
    String path, {
    Object? body,
    bool identity = false,
  }) async {
    final response = await http.Response.fromStream(
      await _send(method, path, body: body, identity: identity),
    ).timeout(const Duration(seconds: 25));
    if (response.statusCode < 200 || response.statusCode >= 300) {
      String? detail;
      try {
        detail = (jsonDecode(response.body) as Map)['detail'] as String?;
      } catch (_) {}
      throw EventApiException(
        status: response.statusCode,
        message: detail ?? '暂时无法完成，请稍后重试。',
      );
    }
    return response.body.isEmpty ? null : jsonDecode(response.body);
  }

  Future<Uint8List> bytes(String path, {bool identity = false}) async {
    final response = await http.Response.fromStream(
      await _send('GET', path, identity: identity),
    ).timeout(const Duration(seconds: 30));
    if (response.statusCode != 200) {
      throw const EventApiException(status: 404, message: '附件已不可查看。');
    }
    return response.bodyBytes;
  }

  Future<(Uri, Map<String, String>)> mediaAccess(String path) async {
    final fresh = await auth.ensureFreshToken(session);
    return (
      await _uri(path, false),
      {'Authorization': 'Bearer ${fresh.accessToken}'},
    );
  }

  Stream<int> changes({
    int after = 0,
    ValueChanged<bool>? onConnection,
  }) async* {
    var cursor = after;
    while (!_closed) {
      try {
        final response = await _send(
          'GET',
          '/api/v1/notifications/stream?after=$cursor',
        );
        if (response.statusCode != 200) {
          await response.stream.drain<void>();
          throw const FormatException();
        }
        if (_closed) return;
        onConnection?.call(true);
        await for (final line
            in response.stream
                .timeout(const Duration(seconds: 65))
                .transform(utf8.decoder)
                .transform(const LineSplitter())) {
          if (_closed) return;
          if (line.startsWith('id:')) {
            final id = int.tryParse(line.substring(3).trim()) ?? 0;
            if (id > cursor) {
              cursor = id;
              yield cursor;
            }
          }
        }
      } catch (_) {
        if (_closed) return;
      }
      if (_closed) return;
      onConnection?.call(false);
      await Future<void>.delayed(const Duration(seconds: 2));
    }
  }

  void close() {
    _closed = true;
    _http.close();
  }
}

/// One foreground event connection for the signed-in account, shared by pages.
class SocialFeed extends ChangeNotifier {
  SocialFeed(this.auth, this.session, {this.apiFactory});
  final AuthService auth;
  final AuthSession session;
  final SocialApi Function()? apiFactory;
  SocialApi? _streamApi;
  StreamSubscription<int>? _subscription;
  Timer? _notificationBatch;
  Timer? _reconnectTimer;
  bool reconnecting = false;
  int unread = 0, revision = 0, _cursor = 0;
  bool _disposed = false;
  void start() {
    if (_disposed || _subscription != null) return;
    final api = apiFactory?.call() ?? SocialApi(auth, session);
    _streamApi = api;
    // A pause can cancel a batched notification after its cursor advanced.
    // Always ask mounted views to reconcile when the app returns.
    scheduleMicrotask(() {
      if (_disposed || _streamApi != api) return;
      revision++;
      notifyListeners();
    });
    unawaited(_refresh(api));
    void connection(bool connected) {
      if (_disposed || _streamApi != api) return;
      if (connected) {
        _reconnectTimer?.cancel();
        _reconnectTimer = null;
        if (reconnecting) {
          reconnecting = false;
          notifyListeners();
        }
      } else {
        _reconnectTimer ??= Timer(const Duration(seconds: 10), () {
          if (_disposed || _streamApi != api) return;
          reconnecting = true;
          notifyListeners();
        });
      }
    }

    connection(false);
    _subscription = api
        .changes(after: _cursor, onConnection: connection)
        .listen((id) {
          _cursor = id;
          _notificationBatch ??= Timer(const Duration(milliseconds: 200), () {
            _notificationBatch = null;
            if (_disposed || _streamApi != api) return;
            revision++;
            notifyListeners();
            unawaited(_refresh(api));
          });
        });
  }

  Future<void> _refresh(SocialApi api) async {
    try {
      final data = await api.request('GET', '/api/v1/conversations/unread');
      var noticeCount = 0;
      try {
        final summary = await api.request(
          'GET',
          '/api/v1/notifications/summary',
        );
        noticeCount = (summary['unreadCount'] as num? ?? 0).toInt();
      } catch (_) {
        /* Older servers still provide chat unread counts. */
      }
      if (!_disposed && _streamApi == api) {
        unread = (data['count'] as num).toInt() + noticeCount;
        notifyListeners();
      }
    } catch (_) {}
  }

  void stop() {
    _reconnectTimer?.cancel();
    _reconnectTimer = null;
    reconnecting = false;
    _notificationBatch?.cancel();
    _notificationBatch = null;
    _streamApi?.close();
    _streamApi = null;
    unawaited(_subscription?.cancel());
    _subscription = null;
  }

  @override
  void dispose() {
    _disposed = true;
    stop();
    super.dispose();
  }
}
