import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_icons.dart';
import 'subject_model.dart';
import 'subject_picker_sheet.dart';
import 'subjects_api.dart';

class SubjectPicker extends StatefulWidget {
  const SubjectPicker({
    super.key,
    required this.auth,
    required this.session,
    required this.ids,
    required this.onChanged,
    this.exclude,
    this.apiClient,
    this.showHeading = true,
  });
  final AuthService auth;
  final AuthSession session;
  final List<String> ids;
  final ValueChanged<List<String>> onChanged;
  final String? exclude;
  final SubjectsApi? apiClient;
  final bool showHeading;
  @override
  State<SubjectPicker> createState() => _SubjectPickerState();
}

class _SubjectPickerState extends State<SubjectPicker> {
  SubjectsApi? _api;
  List<SubjectModel> _subjects = [];
  String? _error;
  bool _loading = false, _selecting = false;
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      _api ??= widget.apiClient;
      if (_api == null) {
        final base = await widget.auth.getEventsApiBaseUrl();
        if (!mounted) return;
        _api = SubjectsApi(auth: widget.auth, baseUrl: base);
      }
      final result = await _api!.list(widget.session);
      if (mounted) setState(() => _subjects = result);
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _select() async {
    if (_loading || _selecting || _error != null) return;
    _selecting = true;
    FocusScope.of(context).unfocus();
    try {
      final ids = await showSubjectPickerSheet(
        context: context,
        subjects: _subjects.where((s) => s.id != widget.exclude).toList(),
        selectedIds: widget.ids,
      );
      if (mounted && ids != null) widget.onChanged(ids);
    } finally {
      _selecting = false;
    }
  }

  String _name(String id) {
    for (final subject in _subjects) {
      if (subject.id == id) return subject.isSelf ? '自己' : subject.name;
    }
    return '已标记档案';
  }

  @override
  void dispose() {
    if (widget.apiClient == null) _api?.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      if (widget.showHeading) ...[
        const Text('标记人物', style: TextStyle(fontWeight: FontWeight.bold)),
        const SizedBox(height: 12),
      ],
      Text(
        '这条内容也会显示在标记人物的时间轴中。',
        style: TextStyle(color: context.traceColors.inkSecondary, height: 1.5),
      ),
      const SizedBox(height: 12),
      if (_error != null) ...[
        Semantics(liveRegion: true, child: Text('人物列表加载失败，请重试。')),
        TextButton(onPressed: _loading ? null : _load, child: const Text('重试')),
      ],
      OutlinedButton.icon(
        key: const Key('subject-picker-open'),
        onPressed: _loading || _error != null ? null : _select,
        icon: _loading
            ? const SizedBox.square(
                dimension: 18,
                child: CircularProgressIndicator(strokeWidth: 2),
              )
            : const TraceIcon(TraceGlyph.add),
        label: Text(
          _loading
              ? '正在加载人物…'
              : widget.ids.isEmpty
              ? '选择人物'
              : '管理已标记（${widget.ids.length}）',
        ),
      ),
      if (widget.ids.isNotEmpty) ...[
        const SizedBox(height: 12),
        LayoutBuilder(
          builder: (context, constraints) => Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final id in widget.ids.take(3))
                ConstrainedBox(
                  constraints: BoxConstraints(maxWidth: constraints.maxWidth),
                  child: InputChip(
                    key: ValueKey('subject-mark-$id'),
                    label: Text(
                      _name(id),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    deleteButtonTooltipMessage: '移除${_name(id)}',
                    onDeleted: () => widget.onChanged(
                      widget.ids.where((value) => value != id).toList(),
                    ),
                  ),
                ),
            ],
          ),
        ),
        if (widget.ids.length > 3) ...[
          const SizedBox(height: 8),
          Text(
            '另有 ${widget.ids.length - 3} 个，可在选择列表中管理。',
            style: TextStyle(color: context.traceColors.inkSecondary),
          ),
        ],
      ],
    ],
  );
}
