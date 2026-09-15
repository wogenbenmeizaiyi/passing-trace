import 'package:flutter/foundation.dart';

import '../auth_service.dart';
import 'profile_api.dart';

class ProfileController extends ChangeNotifier {
  ProfileController({required this.api, required this.session});
  final ProfileApi api;
  final AuthSession Function() session;
  AccountProfile? profile;
  Uint8List? avatar;
  int _generation = 0;
  bool _disposed = false;
  bool _saving = false;
  String _avatarVersion = '';
  String get nickname => profile?.nickname ?? '我的星期八';
  Future<void> refresh() async {
    if (_saving || _disposed) return;
    final ticket = ++_generation;
    final next = await api.get(session());
    await _accept(next, ticket);
  }

  Future<void> _accept(AccountProfile next, int ticket) async {
    if (_disposed || ticket != _generation) return;
    profile = next;
    if (!next.hasAvatar) {
      avatar = null;
      _avatarVersion = '';
    }
    notifyListeners();
    if (next.hasAvatar && (avatar == null || _avatarVersion != next.version)) {
      Uint8List? bytes;
      try {
        bytes = await api.avatar(session());
      } catch (_) {
        /* A failed image never blocks account information. */
      }
      if (_disposed || ticket != _generation) return;
      avatar = bytes;
      _avatarVersion = bytes == null ? '' : next.version;
      notifyListeners();
    }
  }

  Future<void> save({
    required String nickname,
    required String bio,
    required String version,
    Uint8List? image,
    bool removeAvatar = false,
  }) async {
    final ticket = ++_generation;
    _saving = true;
    try {
      final next = await api.save(
        session(),
        nickname: nickname,
        bio: bio,
        version: version,
        avatar: image,
        removeAvatar: removeAvatar,
      );
      await _accept(next, ticket);
    } finally {
      _saving = false;
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _generation++;
    api.dispose();
    super.dispose();
  }
}
