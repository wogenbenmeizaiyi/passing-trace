import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';

import '../auth_service.dart';
import '../events/media_api.dart';

class SubjectMediaEditor extends StatefulWidget {
  const SubjectMediaEditor({
    super.key,
    required this.auth,
    required this.session,
    required this.ids,
    required this.onChanged,
    this.onBusy,
    this.showHeading = true,
    this.enabled = true,
  });
  final AuthService auth;
  final AuthSession session;
  final List<String> ids;
  final ValueChanged<List<String>> onChanged;
  final ValueChanged<bool>? onBusy;
  final bool showHeading;
  final bool enabled;
  @override
  State<SubjectMediaEditor> createState() => _SubjectMediaEditorState();
}

class _SubjectMediaEditorState extends State<SubjectMediaEditor> {
  bool _busy = false;
  double _progress = 0;
  String? _error;
  Future<void> _upload() async {
    if (_busy || !widget.enabled) return;
    final result = await FilePicker.pickFiles(
      type: FileType.custom,
      allowedExtensions: [
        'jpg',
        'jpeg',
        'png',
        'webp',
        'mp4',
        'mov',
        'webm',
        'glb',
      ],
    );
    if (!mounted) return;
    setState(() => _busy = true);
    widget.onBusy?.call(true);
    MediaApiClient? api;
    try {
      final base = await widget.auth.getEventsApiBaseUrl();
      api = MediaApiClient(auth: widget.auth, baseUrl: base);
      var ids = [...widget.ids];
      for (final file in result) {
        if (ids.length >= 10) throw StateError('最多 10 个附件');
        final uploaded = await api.upload(
          widget.session,
          file,
          onProgress: (p) {
            if (mounted) setState(() => _progress = p);
          },
        );
        if (!mounted) return;
        ids = [...ids, uploaded.id];
        widget.onChanged(ids);
      }
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    } finally {
      api?.close();
      if (mounted) {
        setState(() => _busy = false);
        widget.onBusy?.call(false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      if (widget.showHeading) ...[
        const Text('图片、视频与三维模型', style: TextStyle(fontWeight: FontWeight.bold)),
        const SizedBox(height: 12),
      ],
      const Text('GLB 需要内嵌贴图，最多 10 个附件。'),
      const SizedBox(height: 12),
      for (var i = 0; i < widget.ids.length; i++)
        ListTile(
          contentPadding: EdgeInsets.zero,
          title: Text('附件 ${i + 1}'),
          trailing: IconButton(
            tooltip: '移除附件',
            onPressed: _busy || !widget.enabled
                ? null
                : () => widget.onChanged(
                    widget.ids.where((id) => id != widget.ids[i]).toList(),
                  ),
            icon: const Icon(Icons.close),
          ),
        ),
      if (_busy) LinearProgressIndicator(value: _progress),
      if (_error != null) Text(_error!),
      TextButton.icon(
        onPressed: _busy || !widget.enabled ? null : _upload,
        icon: const Icon(Icons.upload_file),
        label: Text(_busy ? '正在上传…' : '上传媒体'),
      ),
    ],
  );
}
