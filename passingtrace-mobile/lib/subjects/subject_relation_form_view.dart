import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/event_datetime.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';
import 'subject_date_input.dart';
import 'subject_form_section.dart';
import 'subject_model.dart';
import 'subjects_api.dart';

class SubjectRelationFormView extends StatefulWidget {
  const SubjectRelationFormView({
    super.key,
    required this.api,
    required this.session,
    required this.subject,
    required this.subjects,
    this.relation,
  });

  final SubjectsApi api;
  final AuthSession session;
  final SubjectModel subject;
  final List<SubjectModel> subjects;
  final SubjectRelation? relation;

  @override
  State<SubjectRelationFormView> createState() =>
      _SubjectRelationFormViewState();
}

class _SubjectRelationFormViewState extends State<SubjectRelationFormView> {
  final _form = GlobalKey<FormState>();
  final _scroll = ScrollController(keepScrollOffset: false);
  late final TextEditingController _label, _start, _end;
  late String _target;
  late int _direction;
  bool _busy = false;
  String? _error;

  String _name(SubjectModel subject) => subject.isSelf ? '自己' : subject.name;
  SubjectModel get _targetSubject =>
      widget.subjects.firstWhere((s) => s.id == _target);

  @override
  void initState() {
    super.initState();
    final relation = widget.relation;
    _target = relation == null
        ? widget.subjects.firstWhere((s) => s.id != widget.subject.id).id
        : relation.from == widget.subject.id
        ? relation.to
        : relation.from;
    _direction = relation?.directed != true
        ? 0
        : relation!.to == widget.subject.id
        ? 2
        : 1;
    _label = TextEditingController(text: relation?.label ?? '关联');
    _start = TextEditingController(
      text: toWallClockLocal(
        DateTime.tryParse(relation?.json['startedAt'] as String? ?? ''),
      ),
    );
    _end = TextEditingController(
      text: toWallClockLocal(DateTime.tryParse(relation?.endedAt ?? '')),
    );
  }

  @override
  void dispose() {
    _label.dispose();
    _start.dispose();
    _end.dispose();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_busy || !_form.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    setState(() {
      _busy = true;
      _error = null;
    });
    final reverse =
        _direction == 2 ||
        (_direction == 0 && widget.relation?.to == widget.subject.id);
    final from = reverse ? _targetSubject : widget.subject;
    final body = <String, dynamic>{
      'label': _label.text.trim(),
      'directed': _direction != 0,
      'startedAt': toIsoWithOffset(_start.text, defaultTimezone()),
      'endedAt': toIsoWithOffset(_end.text, defaultTimezone()),
      'resume': _end.text.isEmpty,
      'fromSubjectId': from.id,
      'toSubjectId': reverse ? widget.subject.id : _target,
    };
    try {
      if (widget.relation case final relation?) {
        await widget.api.updateRelation(
          widget.session,
          relation.id,
          body,
          relation.version,
        );
      } else {
        await widget.api.relate(widget.session, from.id, body, from.version);
      }
      if (mounted) Navigator.of(context).pop(true);
    } catch (error) {
      if (mounted) {
        setState(() => _error = '$error');
        if (_scroll.hasClients) {
          await _scroll.animateTo(
            0,
            duration: const Duration(milliseconds: 200),
            curve: Curves.easeOut,
          );
        }
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_busy,
    child: Scaffold(
      appBar: TraceAppBar(
        title: widget.relation == null ? '新增人物关系' : '编辑人物关系',
        leading: TraceIconButton(
          glyph: TraceGlyph.chevronLeft,
          tooltip: '取消编辑',
          onPressed: _busy ? null : () => Navigator.of(context).pop(),
        ),
      ),
      bottomNavigationBar: Padding(
        padding: EdgeInsets.only(
          bottom: MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: TracePrimaryActionBar(
          label: '保存关系',
          loading: _busy,
          onPressed: _busy ? null : _save,
        ),
      ),
      body: SafeArea(
        top: false,
        bottom: false,
        child: Form(
          key: _form,
          child: SingleChildScrollView(
            controller: _scroll,
            padding: const EdgeInsets.fromLTRB(18, 24, 18, 32),
            keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (_error != null) ...[
                  Semantics(
                    liveRegion: true,
                    child: Text(
                      _error!,
                      style: TextStyle(color: context.traceColors.danger),
                    ),
                  ),
                  const SizedBox(height: 20),
                ],
                SubjectFormSection(
                  title: '关联信息',
                  description: '当前档案：${_name(widget.subject)}',
                  children: [
                    DropdownButtonFormField<String>(
                      initialValue: _target,
                      isExpanded: true,
                      menuMaxHeight: 360,
                      decoration: const InputDecoration(labelText: '关联档案'),
                      items: widget.subjects
                          .where((s) => s.id != widget.subject.id)
                          .map(
                            (s) => DropdownMenuItem(
                              value: s.id,
                              child: Text(
                                _name(s),
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                              ),
                            ),
                          )
                          .toList(),
                      onChanged: _busy
                          ? null
                          : (value) => setState(() => _target = value!),
                    ),
                    TextFormField(
                      controller: _label,
                      enabled: !_busy,
                      decoration: const InputDecoration(
                        labelText: '关系名称',
                        hintText: '例如：朋友、饲养、拥有',
                      ),
                      validator: (value) =>
                          value == null || value.trim().isEmpty
                          ? '请填写关系名称'
                          : null,
                    ),
                  ],
                ),
                const SizedBox(height: 28),
                SubjectFormSection(
                  title: '关系方向',
                  description: '选择这条关系的连接方式。',
                  children: [
                    RadioGroup<int>(
                      groupValue: _direction,
                      onChanged: (value) {
                        if (!_busy) setState(() => _direction = value!);
                      },
                      child: Column(
                        children: [
                          _directionTile(
                            0,
                            '相互关联',
                            '${_name(widget.subject)} ↔ ${_name(_targetSubject)}',
                          ),
                          const SizedBox(height: 12),
                          _directionTile(
                            1,
                            '由本档案指向',
                            '${_name(widget.subject)} → ${_name(_targetSubject)}',
                          ),
                          const SizedBox(height: 12),
                          _directionTile(
                            2,
                            '由关联档案指向',
                            '${_name(_targetSubject)} → ${_name(widget.subject)}',
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 28),
                SubjectFormSection(
                  title: '关系时间',
                  description: '时间可留空。填写结束时间后，关系按历史关系展示，仍保留图连接。',
                  children: [
                    SubjectDateInput(
                      controller: _start,
                      label: '开始时间',
                      enabled: !_busy,
                    ),
                    SubjectDateInput(
                      controller: _end,
                      label: '结束时间',
                      enabled: !_busy,
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    ),
  );

  Widget _directionTile(int value, String title, String subtitle) =>
      RadioListTile<int>(
        key: ValueKey('subject-relation-direction-$value'),
        value: value,
        enabled: !_busy,
        title: Text(title),
        subtitle: Padding(
          padding: const EdgeInsets.only(top: 6),
          child: Text(subtitle, style: const TextStyle(height: 1.5)),
        ),
        contentPadding: const EdgeInsets.symmetric(horizontal: 4, vertical: 8),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(14),
          side: BorderSide(
            color: _direction == value
                ? context.traceColors.primary
                : context.traceColors.line,
          ),
        ),
        selected: _direction == value,
        selectedTileColor: context.traceColors.primary.withValues(alpha: .06),
      );
}
