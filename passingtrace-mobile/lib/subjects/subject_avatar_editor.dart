import 'dart:typed_data';

import 'package:cross_file/cross_file.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/media_api.dart';
import '../profile/avatar_crop_view.dart';
import '../theme/passingtrace_theme.dart';
import 'subject_avatar.dart';
import 'subject_model.dart';

class SubjectAvatarEditor extends StatefulWidget {
  const SubjectAvatarEditor({
    super.key,
    required this.auth,
    required this.session,
    required this.subject,
    required this.onChanged,
    this.onBusy,
    this.enabled = true,
    this.apiClient,
    this.imagePicker,
  });

  final AuthService auth;
  final AuthSession session;
  final SubjectModel subject;
  final ValueChanged<String?> onChanged;
  final ValueChanged<bool>? onBusy;
  final bool enabled;
  final MediaApiClient? apiClient;
  final Future<PlatformFile?> Function()? imagePicker;

  @override
  State<SubjectAvatarEditor> createState() => _SubjectAvatarEditorState();
}

class _SubjectAvatarEditorState extends State<SubjectAvatarEditor> {
  MediaApiClient? _api;
  Future<MediaApiClient>? _clientFuture;
  Future<MediaAccessTarget>? _cover;
  String? _coverId, _previewId, _error;
  Uint8List? _preview;
  bool _busy = false;
  double _progress = 0;

  Future<MediaApiClient> _client() async {
    try {
      return await (_clientFuture ??= _loadClient());
    } catch (_) {
      _clientFuture = null;
      rethrow;
    }
  }

  Future<MediaApiClient> _loadClient() async {
    if (_api != null) return _api!;
    if (widget.apiClient != null) return _api = widget.apiClient!;
    final base = await widget.auth.getEventsApiBaseUrl();
    if (!mounted) throw StateError('头像编辑已关闭');
    return _api = MediaApiClient(auth: widget.auth, baseUrl: base);
  }

  Future<PlatformFile?> _pick() async {
    final files = await FilePicker.pickFiles(
      type: FileType.custom,
      allowedExtensions: ['jpg', 'jpeg', 'png', 'webp'],
    );
    return files.isEmpty ? null : files.first;
  }

  Future<void> _upload() async {
    if (_busy || !widget.enabled) return;
    setState(() {
      _busy = true;
      _error = null;
      _progress = 0;
    });
    widget.onBusy?.call(true);
    try {
      final file = await (widget.imagePicker ?? _pick)();
      if (file == null || !mounted) return;
      if (await file.length() > 5 * 1024 * 1024) {
        throw const FormatException('头像不能超过 5MB，请选择较小的图片。');
      }
      final bytes = await file.readAsBytes();
      if (!mounted) return;
      final cropped = await Navigator.of(context).push<Uint8List>(
        MaterialPageRoute(builder: (_) => AvatarCropView(bytes: bytes)),
      );
      if (cropped == null || !mounted) return;
      final api = await _client();
      final uploaded = await api.upload(
        widget.session,
        _AvatarFile(cropped),
        onProgress: (value) {
          if (mounted) setState(() => _progress = value);
        },
      );
      if (!mounted) return;
      setState(() {
        _previewId = uploaded.id;
        _preview = cropped;
      });
      widget.onChanged(uploaded.id);
    } catch (error) {
      if (mounted) {
        setState(
          () => _error = error is FormatException
              ? error.message
              : '头像上传失败，请重试。原头像已保留。',
        );
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
        widget.onBusy?.call(false);
      }
    }
  }

  @override
  void dispose() {
    if (widget.apiClient == null) _api?.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final id = widget.subject.coverMediaId;
    if (_coverId != id) {
      _coverId = id;
      _cover = id == null || id == _previewId
          ? null
          : _client().then((api) => api.access(widget.session, id));
    }
    final enabled = widget.enabled && !_busy;
    final colors = context.traceColors;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Semantics(
              button: true,
              label: id == null ? '设置头像' : '更换头像',
              excludeSemantics: true,
              child: InkWell(
                key: const Key('subject-avatar-edit'),
                customBorder: const CircleBorder(),
                onTap: enabled ? _upload : null,
                child: SubjectAvatar(
                  subject: widget.subject,
                  size: 80,
                  imageBytes: id == _previewId ? _preview : null,
                  cover: _cover,
                ),
              ),
            ),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text(
                    '头像',
                    style: TextStyle(fontWeight: FontWeight.w700),
                  ),
                  const SizedBox(height: 6),
                  Text(
                    '显示在档案列表和关系图中',
                    style: TextStyle(color: colors.inkSecondary, height: 1.5),
                  ),
                  Wrap(
                    spacing: 8,
                    children: [
                      TextButton(
                        onPressed: enabled ? _upload : null,
                        child: Text(id == null ? '设置头像' : '更换头像'),
                      ),
                      if (id != null)
                        TextButton(
                          onPressed: enabled
                              ? () => widget.onChanged(null)
                              : null,
                          child: const Text('移除头像'),
                        ),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ),
        if (_busy) ...[
          const SizedBox(height: 12),
          LinearProgressIndicator(value: _progress == 0 ? null : _progress),
          const SizedBox(height: 8),
          const Text('正在设置头像…'),
        ],
        if (_error != null) ...[
          const SizedBox(height: 12),
          Semantics(
            liveRegion: true,
            child: Text(_error!, style: TextStyle(color: colors.danger)),
          ),
        ],
        const SizedBox(height: 8),
        Text(
          '支持 JPG、PNG、WebP，最大 5MB。保存档案后生效。',
          style: TextStyle(
            color: colors.inkTertiary,
            fontSize: 12,
            height: 1.5,
          ),
        ),
      ],
    );
  }
}

final class _AvatarFile extends PlatformFile {
  _AvatarFile(Uint8List bytes)
    : xFile = XFile.fromData(
        bytes,
        name: 'subject-avatar.png',
        mimeType: 'image/png',
      );
  @override
  final XFile xFile;
  @override
  String get name => 'subject-avatar.png';
  @override
  Uri get uri => Uri.parse(xFile.path);
  @override
  int? lengthSync() => null;
  @override
  Future<int> length() => xFile.length();
  @override
  Future<Uint8List> readAsBytes() => xFile.readAsBytes();
  @override
  Stream<Uint8List> readAsByteStream() => xFile.openRead();
}
