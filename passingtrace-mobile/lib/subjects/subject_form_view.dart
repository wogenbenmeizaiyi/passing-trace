import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';

import '../auth_service.dart';
import '../events/event_datetime.dart';
import '../events/media_api.dart';
import '../storylines/storyline_id.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';
import 'subject_model.dart';
import 'subjects_api.dart';
import 'subject_fields_editor.dart';
import 'subject_form_section.dart';
import 'subject_media_editor.dart';
import 'subject_date_input.dart';
import 'subject_avatar_editor.dart';

class SubjectFormView extends StatefulWidget {
  const SubjectFormView({
    super.key,
    required this.auth,
    required this.session,
    this.subject,
    this.apiClient,
    this.mediaApiClient,
    this.avatarImagePicker,
  });
  final AuthService auth;
  final AuthSession session;
  final SubjectModel? subject;
  final SubjectsApi? apiClient;
  final MediaApiClient? mediaApiClient;
  final Future<PlatformFile?> Function()? avatarImagePicker;
  @override
  State<SubjectFormView> createState() => _SubjectFormViewState();
}

class _SubjectFormViewState extends State<SubjectFormView> {
  SubjectsApi? _api;
  SubjectModel? _current;
  List<SubjectModel> _subjects = [];
  List<SubjectField> _fields = [];
  Map<String, dynamic> _values = {}, _original = {};
  List<String> _media = [];
  final _name = TextEditingController(),
      _description = TextEditingController(),
      _started = TextEditingController(),
      _effective = TextEditingController(),
      _relation = TextEditingController(text: '关联');
  final _form = GlobalKey<FormState>();
  final _key = newStorylineKey();
  int _kind = 0;
  String _itemType = 'vehicle', _target = '';
  final _timezone = defaultTimezone();
  String? _cover, _error;
  bool _busy = false, _uploading = false, _ready = false, _directed = false;
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
      final subjects = await _api!.list(widget.session);
      SubjectModel? current;
      if (widget.subject != null) {
        current = await _api!.get(widget.session, widget.subject!.id);
      }
      final fields =
          current?.fields ?? await _api!.presets(widget.session, _kind, null);
      if (!mounted) return;
      setState(() {
        _subjects = subjects;
        _target = subjects.firstWhere((s) => s.isSelf).id;
        _current = current;
        _fields = fields;
        if (current != null) {
          _name.text = current.name;
          _description.text = current.description ?? '';
          _started.text = toWallClockLocal(
            DateTime.tryParse(current.startedAt ?? ''),
          );
          _kind = current.kind;
          _itemType = current.itemType ?? 'other';
          _values = {...current.values};
          _original = {...current.values};
          _media = [...current.mediaIds];
          _cover = current.coverMediaId;
        }
        _ready = true;
      });
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    }
  }

  Future<void> _preset() async {
    try {
      final fields = await _api!.presets(
        widget.session,
        _kind,
        _kind == 2 ? _itemType : null,
      );
      if (mounted) {
        setState(() {
          _fields = fields;
          _values = {};
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    }
  }

  Future<void> _save() async {
    if (_busy || _uploading || !_ready || !_form.currentState!.validate()) {
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final values = {
        for (final e in _values.entries)
          if (_fields.any((f) => f.id == e.key && !f.removed) &&
              (_current == null || _original[e.key] != e.value))
            e.key: e.value,
      };
      final body = <String, dynamic>{
        'description': _description.text,
        'fields': _fields.map((f) => f.toJson()).toList(),
        'values': values,
        'mediaIds': _media,
        'coverMediaId': _cover,
        'clearCover': _cover == null,
      };
      SubjectModel result;
      if (_current == null) {
        body.addAll({
          'kind': _kind,
          'name': _name.text.trim(),
          'itemType': _kind == 2 ? _itemType : null,
          'timezone': _timezone,
          'startedAt': toIsoWithOffset(_started.text, _timezone),
          'relations': [
            {
              'toSubjectId': _target,
              'label': _relation.text.trim(),
              'directed': _directed,
            },
          ],
        });
        result = await _api!.create(widget.session, body, _key);
      } else {
        body.addAll({
          if (!_current!.isSelf) 'name': _name.text.trim(),
          'effectiveAt': toIsoWithOffset(_effective.text, _timezone),
        });
        result = await _api!.update(
          widget.session,
          _current!.id,
          body,
          _current!.version,
        );
      }
      if (mounted) Navigator.pop(context, result);
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  void dispose() {
    if (widget.apiClient == null) _api?.close();
    for (final c in [_name, _description, _started, _effective, _relation]) {
      c.dispose();
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_busy && !_uploading,
    child: Scaffold(
      appBar: TraceAppBar(
        title: widget.subject == null ? '新建人物档案' : '编辑人物资料',
        leading: TraceIconButton(
          glyph: TraceGlyph.chevronLeft,
          tooltip: '返回',
          onPressed: _busy || _uploading
              ? null
              : () => Navigator.of(context).pop(),
        ),
      ),
      bottomNavigationBar: Padding(
        padding: EdgeInsets.only(
          bottom: MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: TracePrimaryActionBar(
          label: '保存档案',
          loading: _busy,
          onPressed: _busy || _uploading || !_ready ? null : _save,
        ),
      ),
      body: SafeArea(
        child: !_ready && _error == null
            ? const Center(child: CircularProgressIndicator())
            : Form(
                key: _form,
                child: ListView(
                  padding: const EdgeInsets.fromLTRB(18, 24, 18, 32),
                  children: [
                    if (_error != null)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 20),
                        child: Semantics(
                          liveRegion: true,
                          child: Text(_error!),
                        ),
                      ),
                    SubjectFormSection(
                      title: '基本信息',
                      children: [
                        if (_current?.isSelf == true)
                          const Text('自己的昵称和头像沿用账号资料。')
                        else ...[
                          SubjectAvatarEditor(
                            auth: widget.auth,
                            session: widget.session,
                            subject: SubjectModel({
                              ...?_current?.json,
                              'kind': _kind,
                              'itemType': _kind == 2 ? _itemType : null,
                              'state': _current?.state ?? 0,
                              'coverMediaId': _cover,
                            }),
                            apiClient: widget.mediaApiClient,
                            imagePicker: widget.avatarImagePicker,
                            enabled: _ready && !_busy && !_uploading,
                            onBusy: (busy) => setState(() => _uploading = busy),
                            onChanged: (id) => setState(() => _cover = id),
                          ),
                          TextFormField(
                            controller: _name,
                            decoration: const InputDecoration(labelText: '名称'),
                            maxLength: 200,
                            validator: (v) =>
                                v == null || v.trim().isEmpty ? '请填写名称' : null,
                          ),
                        ],
                        if (_current == null) ...[
                          DropdownButtonFormField<int>(
                            isExpanded: true,
                            initialValue: _kind,
                            decoration: const InputDecoration(labelText: '类型'),
                            items: const [
                              DropdownMenuItem(value: 0, child: Text('人')),
                              DropdownMenuItem(value: 1, child: Text('宠物')),
                              DropdownMenuItem(value: 2, child: Text('物品')),
                            ],
                            onChanged: (v) {
                              setState(() => _kind = v!);
                              _preset();
                            },
                          ),
                          if (_kind == 2)
                            DropdownButtonFormField<String>(
                              isExpanded: true,
                              initialValue: _itemType,
                              decoration: const InputDecoration(
                                labelText: '物品分类',
                              ),
                              items: const [
                                DropdownMenuItem(
                                  value: 'vehicle',
                                  child: Text('车辆'),
                                ),
                                DropdownMenuItem(
                                  value: 'property',
                                  child: Text('房屋'),
                                ),
                                DropdownMenuItem(
                                  value: 'bicycle',
                                  child: Text('自行车'),
                                ),
                                DropdownMenuItem(
                                  value: 'collectible',
                                  child: Text('收藏品'),
                                ),
                                DropdownMenuItem(
                                  value: 'other',
                                  child: Text('其他'),
                                ),
                              ],
                              onChanged: (v) {
                                setState(() => _itemType = v!);
                                _preset();
                              },
                            ),
                          SubjectDateInput(
                            controller: _started,
                            label: '起始日期（可不填写）',
                          ),
                        ],
                        TextFormField(
                          controller: _description,
                          decoration: const InputDecoration(labelText: '说明'),
                          minLines: 3,
                          maxLines: 6,
                          textAlignVertical: TextAlignVertical.top,
                        ),
                      ],
                    ),
                    if (_current == null) ...[
                      const SizedBox(height: 28),
                      SubjectFormSection(
                        title: '关联关系',
                        children: [
                          DropdownButtonFormField<String>(
                            isExpanded: true,
                            initialValue: _target.isEmpty ? null : _target,
                            decoration: const InputDecoration(
                              labelText: '关联已有档案',
                            ),
                            items: _subjects
                                .map(
                                  (s) => DropdownMenuItem(
                                    value: s.id,
                                    child: Text(s.isSelf ? '自己' : s.name),
                                  ),
                                )
                                .toList(),
                            onChanged: (v) => _target = v!,
                            validator: (v) => v == null ? '请选择关联档案' : null,
                          ),
                          TextFormField(
                            controller: _relation,
                            decoration: const InputDecoration(
                              labelText: '关系名称',
                            ),
                            validator: (v) => v == null || v.trim().isEmpty
                                ? '请填写关系名称'
                                : null,
                          ),
                          CheckboxListTile(
                            contentPadding: EdgeInsets.zero,
                            dense: false,
                            title: const Text('新档案指向所选档案'),
                            value: _directed,
                            onChanged: (v) => setState(() => _directed = v!),
                          ),
                        ],
                      ),
                    ],
                    const SizedBox(height: 28),
                    SubjectFormSection(
                      title: '生活字段',
                      children: [
                        SubjectFieldsEditor(
                          showHeading: false,
                          fields: _fields,
                          values: _values,
                          onChanged: (v) => setState(() => _values = v),
                          onFieldsChanged: (f) => setState(() => _fields = f),
                        ),
                        if (_current != null)
                          SubjectDateInput(
                            controller: _effective,
                            label: '字段变化实际时间（留空为现在）',
                          ),
                      ],
                    ),
                    const SizedBox(height: 28),
                    SubjectFormSection(
                      title: '图片、视频与三维模型',
                      children: [
                        SubjectMediaEditor(
                          enabled: _ready && !_busy && !_uploading,
                          showHeading: false,
                          auth: widget.auth,
                          session: widget.session,
                          ids: _media,
                          onChanged: (ids) => setState(() => _media = ids),
                          onBusy: (b) => setState(() => _uploading = b),
                        ),
                      ],
                    ),
                    const SizedBox(height: 24),
                    Text(
                      '初始值和后续字段变化会保存在该人物的时间轴中。',
                      style: TextStyle(
                        color: context.traceColors.inkSecondary,
                        height: 1.5,
                      ),
                    ),
                  ],
                ),
              ),
      ),
    ),
  );
}
