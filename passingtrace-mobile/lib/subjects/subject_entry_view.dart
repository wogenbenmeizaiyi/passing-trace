import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/event_datetime.dart';
import '../storylines/storyline_id.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';
import 'subject_model.dart';
import 'subjects_api.dart';
import 'subject_fields_editor.dart';
import 'subject_form_section.dart';
import 'subject_picker.dart';
import 'subject_media.dart';
import 'subject_media_editor.dart';
import 'subject_date_input.dart';
import 'subject_delete_dialog.dart';

class SubjectEntryView extends StatefulWidget {
  const SubjectEntryView({
    super.key,
    required this.auth,
    required this.session,
    this.subjectId,
    this.entryId,
    this.apiClient,
  });
  final AuthService auth;
  final AuthSession session;
  final String? subjectId, entryId;
  final SubjectsApi? apiClient;
  @override
  State<SubjectEntryView> createState() => _SubjectEntryViewState();
}

class _SubjectEntryViewState extends State<SubjectEntryView> {
  SubjectsApi? _api;
  SubjectModel? _subject;
  SubjectEntry? _entry;
  final _title = TextEditingController(),
      _content = TextEditingController(),
      _when = TextEditingController(),
      _actualWhen = TextEditingController();
  final _form = GlobalKey<FormState>(), _actualForm = GlobalKey<FormState>();
  final _key = newStorylineKey();
  Map<String, dynamic> _values = {}, _actualValues = {};
  List<String> _media = [], _marked = [];
  bool _busy = false, _uploading = false, _ready = false, _completing = false;
  int _kind = 0;
  String? _error;
  String get _timezone => defaultTimezone();
  String get _subjectId => _entry?.subjectId ?? widget.subjectId!;
  String get _entryLabel => _kind == 1 ? '计划' : '记录';
  bool get _planned => _kind == 1 && _entry?.state != 1;
  List<SubjectField> get _fields => _subject?.fields ?? _entry?.fields ?? [];
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      _api = widget.apiClient;
      if (_api == null) {
        final base = await widget.auth.getEventsApiBaseUrl();
        if (!mounted) return;
        _api = SubjectsApi(auth: widget.auth, baseUrl: base);
      }
      if (widget.entryId != null) {
        final entry = await _api!.entry(widget.session, widget.entryId!);
        if (!mounted) return;
        _entry = entry;
        _title.text = entry.title;
        _content.text = entry.content ?? '';
        _kind = entry.kind;
        _when.text = toWallClockLocal(
          DateTime.tryParse(
            (entry.state == 1 ? entry.happenedAt : entry.plannedAt) ?? '',
          ),
        );
        _values = {
          ...(entry.kind == 1 && entry.state == 1
              ? entry.actualFieldChanges
              : entry.fieldChanges),
        };
        _marked = [...entry.markedSubjectIds];
        _media = [...entry.mediaIds];
      }
      if (_entry?.sourceSubjectDeleted != true) {
        _subject = await _api!.get(widget.session, _subjectId);
      }
      if (mounted) setState(() => _ready = true);
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    }
  }

  Future<void> _run(Future<void> Function() action) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await action();
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _save() async {
    if (_uploading || !_form.currentState!.validate()) return;
    final date = toIsoWithOffset(_when.text, _timezone);
    final body = <String, dynamic>{
      'kind': _kind,
      'title': _title.text.trim(),
      'content': _content.text,
      'timezone': _timezone,
      if (_kind == 0 || _entry?.state == 1) 'happenedAt': date,
      if (_kind == 1 && _entry?.state != 1) 'plannedAt': date,
      'clearPlannedAt': _kind == 1 && _entry?.state != 1 && date == null,
      'fieldChanges': _values,
      'mediaIds': _media,
      'markedSubjectIds': _marked,
    };
    final result = _entry == null
        ? await _api!.createEntry(widget.session, _subjectId, body, _key)
        : await _api!.updateEntry(
            widget.session,
            _entry!.id,
            body,
            _entry!.version,
          );
    if (mounted) {
      setState(() => _entry = result);
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('$_entryLabel已保存')));
    }
  }

  Future<void> _decide(String operation) async {
    if (operation == 'complete' && !_actualForm.currentState!.validate()) {
      return;
    }
    final result = await _api!.decideEntry(widget.session, _entry!.id, {
      'operation': operation,
      if (operation == 'complete')
        'happenedAt': toIsoWithOffset(_actualWhen.text, _timezone),
      if (operation == 'complete') 'actualFieldChanges': _actualValues,
    }, _entry!.version);
    if (mounted) {
      setState(() {
        _entry = result;
        _completing = false;
        if (operation == 'complete') {
          _values = {...result.actualFieldChanges};
          _when.text = toWallClockLocal(
            DateTime.tryParse(result.happenedAt ?? ''),
          );
        }
      });
    }
  }

  Future<void> _delete() async {
    final result = await showSubjectDeleteDialog(
      context: context,
      api: _api!,
      session: widget.session,
      targetType: 'SubjectEntry',
      targetId: _entry!.id,
    );
    if (mounted && result?.state == 'Succeeded') Navigator.pop(context);
  }

  @override
  void dispose() {
    if (widget.apiClient == null) _api?.close();
    for (final c in [_title, _content, _when, _actualWhen]) {
      c.dispose();
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: TraceAppBar(
      title: widget.entryId == null && _entry == null
          ? '新建$_entryLabel'
          : _entryLabel,
      leading: TraceIconButton(
        glyph: TraceGlyph.chevronLeft,
        tooltip: '返回',
        onPressed: _busy ? null : () => Navigator.of(context).pop(),
      ),
    ),
    bottomNavigationBar: _entry?.sourceSubjectDeleted == true
        ? null
        : Padding(
            padding: EdgeInsets.only(
              bottom: MediaQuery.viewInsetsOf(context).bottom,
            ),
            child: TracePrimaryActionBar(
              label: '保存$_entryLabel',
              loading: _busy,
              onPressed: _busy || _uploading || !_ready
                  ? null
                  : () => _run(_save),
            ),
          ),
    body: SafeArea(
      child: !_ready && _error == null
          ? const Center(child: CircularProgressIndicator())
          : ListView(
              padding: const EdgeInsets.fromLTRB(18, 24, 18, 32),
              children: [
                if (_ready) ...[
                  _EntryContext(
                    subjectName: _subject?.name ?? _entry?.subjectName ?? '',
                    state: _entry == null
                        ? null
                        : ['待执行', '已完成', '已取消'][_entry!.state],
                  ),
                  const SizedBox(height: 24),
                ],
                if (_error != null)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 20),
                    child: Semantics(liveRegion: true, child: Text(_error!)),
                  ),
                if (_entry?.sourceSubjectDeleted == true) ...[
                  SubjectFormSection(
                    title: _entry!.title,
                    description: '所属档案已删除，仍可查看此内容。',
                    children: [Text(_entry!.content ?? '')],
                  ),
                ],
                if (_ready && _entry?.sourceSubjectDeleted != true)
                  Form(
                    key: _form,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        SubjectFormSection(
                          title: '内容',
                          children: [
                            if (_entry == null)
                              SegmentedButton<int>(
                                segments: const [
                                  ButtonSegment(value: 0, label: Text('记录')),
                                  ButtonSegment(value: 1, label: Text('计划')),
                                ],
                                selected: {_kind},
                                onSelectionChanged: (v) =>
                                    setState(() => _kind = v.first),
                              ),
                            TextFormField(
                              controller: _title,
                              decoration: const InputDecoration(
                                labelText: '标题',
                              ),
                              maxLength: 200,
                              validator: (v) => v == null || v.trim().isEmpty
                                  ? '请填写标题'
                                  : null,
                            ),
                            TextFormField(
                              controller: _content,
                              decoration: const InputDecoration(
                                labelText: '正文',
                              ),
                              minLines: 4,
                              maxLines: 10,
                              textAlignVertical: TextAlignVertical.top,
                            ),
                            SubjectDateInput(
                              controller: _when,
                              label: _planned ? '计划时间（可留空）' : '实际发生时间（留空为现在）',
                            ),
                          ],
                        ),
                        if (_fields.any((field) => !field.removed)) ...[
                          const SizedBox(height: 28),
                          SubjectFormSection(
                            title: _planned ? '预期变化' : '实际变化',
                            description: _planned
                                ? '填写预计变化，完成计划时再确认实际值。'
                                : '只填写本次发生变化的字段。',
                            children: [
                              SubjectFieldsEditor(
                                showHeading: false,
                                fields: _fields,
                                values: _values,
                                onChanged: (v) => setState(() => _values = v),
                              ),
                            ],
                          ),
                        ],
                        const SizedBox(height: 28),
                        SubjectFormSection(
                          title: '标记人物',
                          children: [
                            SubjectPicker(
                              showHeading: false,
                              auth: widget.auth,
                              session: widget.session,
                              ids: _marked,
                              exclude: _subjectId,
                              onChanged: (ids) => setState(() => _marked = ids),
                              apiClient: widget.apiClient,
                            ),
                          ],
                        ),
                        const SizedBox(height: 28),
                        SubjectFormSection(
                          title: '图片、视频与三维模型',
                          children: [
                            SubjectMediaEditor(
                              showHeading: false,
                              auth: widget.auth,
                              session: widget.session,
                              ids: _media,
                              onChanged: (ids) => setState(() => _media = ids),
                              onBusy: (value) =>
                                  setState(() => _uploading = value),
                            ),
                            for (final id in _media)
                              SubjectMedia(
                                auth: widget.auth,
                                session: widget.session,
                                id: id,
                              ),
                          ],
                        ),
                      ],
                    ),
                  ),
                if (_entry?.kind == 1 &&
                    _entry?.state == 0 &&
                    _entry?.sourceSubjectDeleted != true) ...[
                  const SizedBox(height: 28),
                  SubjectFormSection(
                    title: '计划状态',
                    children: [
                      OutlinedButton(
                        onPressed: _busy
                            ? null
                            : () => setState(() => _completing = !_completing),
                        child: const Text('确认实际情况并完成计划'),
                      ),
                      TextButton(
                        onPressed: _busy
                            ? null
                            : () => _run(() => _decide('cancel')),
                        child: const Text('取消计划'),
                      ),
                    ],
                  ),
                  if (_completing) ...[
                    const SizedBox(height: 28),
                    Form(
                      key: _actualForm,
                      child: SubjectFormSection(
                        title: '确认实际情况',
                        description: '请填写实际变化；留空表示没有变化，不会套用预期值。',
                        children: [
                          SubjectDateInput(
                            controller: _actualWhen,
                            label: '实际发生时间',
                            requiredDate: true,
                          ),
                          SubjectFieldsEditor(
                            showHeading: false,
                            fields: _fields,
                            values: _actualValues,
                            onChanged: (v) => setState(() => _actualValues = v),
                          ),
                          FilledButton(
                            onPressed: _busy
                                ? null
                                : () => _run(() => _decide('complete')),
                            child: const Text('确认完成'),
                          ),
                        ],
                      ),
                    ),
                  ],
                ],
                if (_entry != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 24),
                    child: TextButton(
                      onPressed: _busy ? null : () => _run(_delete),
                      child: Text('申请删除$_entryLabel'),
                    ),
                  ),
                if (_entry?.sourceSubjectDeleted == true)
                  for (final id in _media)
                    Padding(
                      padding: const EdgeInsets.only(top: 20),
                      child: SubjectMedia(
                        auth: widget.auth,
                        session: widget.session,
                        id: id,
                      ),
                    ),
              ],
            ),
    ),
  );
}

class _EntryContext extends StatelessWidget {
  const _EntryContext({required this.subjectName, this.state});

  final String subjectName;
  final String? state;

  @override
  Widget build(BuildContext context) => Wrap(
    alignment: WrapAlignment.spaceBetween,
    crossAxisAlignment: WrapCrossAlignment.center,
    spacing: 16,
    runSpacing: 12,
    children: [
      Text(
        '所属：$subjectName',
        style: TextStyle(color: context.traceColors.inkSecondary, height: 1.5),
      ),
      if (state != null)
        Chip(label: Text(state!), visualDensity: VisualDensity.compact),
    ],
  );
}
