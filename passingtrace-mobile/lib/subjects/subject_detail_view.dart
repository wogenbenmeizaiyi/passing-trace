import 'dart:async';
import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../profile/profile_api.dart';
import '../events/media_api.dart';
import '../events/event_datetime.dart';
import '../views/event_detail_view.dart';
import 'subject_model.dart';
import 'subjects_api.dart';
import 'subject_form_view.dart';
import 'subject_entry_view.dart';
import 'subject_media.dart';
import 'subject_lifecycle_dialog.dart';
import 'subject_delete_dialog.dart';
import 'subject_relation_form_view.dart';
import 'subject_relations_section.dart';
import 'subject_timeline_filter_sheet.dart';
import 'subject_timeline_section.dart';
import 'subject_actions_menu.dart';
import 'subject_avatar.dart';

class SubjectDetailView extends StatefulWidget {
  const SubjectDetailView({
    super.key,
    required this.auth,
    required this.session,
    required this.subjectId,
    this.apiClient,
    this.nickname,
    this.avatar,
  });
  final AuthService auth;
  final AuthSession session;
  final String subjectId;
  final SubjectsApi? apiClient;
  final String? nickname;
  final Uint8List? avatar;
  @override
  State<SubjectDetailView> createState() => _SubjectDetailViewState();
}

class _SubjectDetailViewState extends State<SubjectDetailView> {
  final _scroll = ScrollController();
  SubjectsApi? _api;
  SubjectModel? _subject;
  SubjectGraph? _graph;
  List<Map<String, dynamic>> _groups = [];
  SubjectTimelineFilter _filter = const SubjectTimelineFilter();
  String? _cursor, _error;
  bool _busy = false, _loading = true;
  String? _nickname;
  Uint8List? _avatar;
  ProfileApi? _profileApi;
  MediaApiClient? _mediaApi;
  final _covers = <String, Future<MediaAccessTarget>>{};
  int _request = 0;
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      _api ??= widget.apiClient;
      if (_api == null) {
        final base = await widget.auth.getEventsApiBaseUrl();
        if (!mounted) return;
        _api = SubjectsApi(auth: widget.auth, baseUrl: base);
      }
      final subject = await _api!.get(widget.session, widget.subjectId);
      final graph = await _api!.graph(widget.session);
      if (!mounted) return;
      setState(() {
        _subject = subject;
        _graph = graph;
        _error = null;
        _covers.clear();
      });
      if (subject.isSelf &&
          widget.nickname == null &&
          widget.apiClient == null &&
          _profileApi == null) {
        unawaited(_loadProfile());
      }
      await _timeline();
    } catch (e) {
      if (mounted) {
        setState(() {
          _error = '$e';
          _loading = false;
        });
      }
    }
  }

  Future<void> _loadProfile() async {
    _profileApi = ProfileApi(widget.auth);
    try {
      final profile = await _profileApi!.get(widget.session);
      if (!mounted) return;
      setState(() => _nickname = profile.nickname);
      if (profile.hasAvatar) {
        final avatar = await _profileApi!.avatar(widget.session);
        if (mounted) setState(() => _avatar = avatar);
      }
    } catch (_) {
      /* Account media failures do not prevent reading the timeline. */
    }
  }

  Future<MediaAccessTarget> _cover(String id) =>
      _covers.putIfAbsent(id, () async {
        final base = await widget.auth.getEventsApiBaseUrl();
        if (!mounted) throw StateError('档案已关闭');
        _mediaApi ??= MediaApiClient(auth: widget.auth, baseUrl: base);
        return _mediaApi!.access(widget.session, id);
      });

  Future<void> _timeline({bool more = false}) async {
    final request = ++_request;
    setState(() => _loading = true);
    try {
      final result = await _api!.timeline(widget.session, widget.subjectId, {
        'groupBy': _filter.groupBy,
        'timezone': defaultTimezone(),
        'kind': _filter.kind,
        'state': _filter.state,
        'limit': 40,
        if (more) 'cursor': _cursor,
        'from': toIsoWithOffset(_filter.from, defaultTimezone()),
        'to': toIsoWithOffset(_filter.to, defaultTimezone()),
      });
      if (!mounted || request != _request) return;
      setState(() {
        if (!more) _groups = [];
        for (final raw in result['groups'] as List) {
          final group = Map<String, dynamic>.from(raw as Map);
          final index = _groups.indexWhere((g) => g['key'] == group['key']);
          if (index < 0) {
            _groups.add(group);
          } else {
            _groups[index]['items'] = [
              ..._groups[index]['items'] as List,
              ...group['items'] as List,
            ];
          }
        }
        _cursor = result['nextCursor'] as String?;
        _error = null;
      });
    } catch (e) {
      if (mounted && request == _request) setState(() => _error = '$e');
    } finally {
      if (mounted && request == _request) setState(() => _loading = false);
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

  void _manage(SubjectAction action) => _run(
    () => switch (action) {
      SubjectAction.end => _lifecycle('end'),
      SubjectAction.resume => _lifecycle('resume'),
      SubjectAction.correct => _lifecycle('correct'),
      SubjectAction.delete => _delete('Subject', widget.subjectId),
    },
  );

  Future<void> _filterTimeline() async {
    final selection = await showSubjectTimelineFilterSheet(
      context: context,
      selection: _filter,
    );
    if (selection == null || !mounted) return;
    setState(() => _filter = selection);
    await _timeline();
  }

  Future<void> _edit() async {
    await Navigator.push(
      context,
      MaterialPageRoute<void>(
        builder: (_) => SubjectFormView(
          auth: widget.auth,
          session: widget.session,
          subject: _subject,
          apiClient: widget.apiClient,
        ),
      ),
    );
    if (mounted) await _load();
  }

  Future<void> _entry({String? entryId}) async {
    await Navigator.push(
      context,
      MaterialPageRoute<void>(
        builder: (_) => SubjectEntryView(
          auth: widget.auth,
          session: widget.session,
          subjectId: widget.subjectId,
          entryId: entryId,
          apiClient: widget.apiClient,
        ),
      ),
    );
    if (mounted) await _load();
  }

  Future<void> _delete(String type, String id) async {
    final relation = type == 'SubjectRelation'
        ? _graph!.relations.firstWhere((r) => r.id == id)
        : null;
    final result = await showSubjectDeleteDialog(
      context: context,
      api: _api!,
      session: widget.session,
      targetType: type,
      targetId: id,
      contextLabel: relation == null
          ? null
          : '${_graph!.nodes.firstWhere((n) => n.id == relation.from).name} — ${_graph!.nodes.firstWhere((n) => n.id == relation.to).name}',
    );
    if (!mounted) return;
    if (result?.state == 'Succeeded' && type == 'Subject') {
      Navigator.pop(context);
    } else if (result != null && result.state != 'Cancelled') {
      await _load();
      if (mounted && result.state == 'Succeeded') {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('关系已移除')));
      }
    }
  }

  Future<void> _relation([SubjectRelation? existing]) async {
    final saved = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => SubjectRelationFormView(
          api: _api!,
          session: widget.session,
          subject: _subject!,
          subjects: _graph!.nodes,
          relation: existing,
        ),
      ),
    );
    if (saved == true && mounted) await _load();
  }

  Future<void> _lifecycle(String operation) async {
    final preview = await _api!.previewLifecycle(
      widget.session,
      widget.subjectId,
    );
    if (!mounted) return;
    final plans = (preview['plans'] as List)
        .map((x) => SubjectEntry(Map<String, dynamic>.from(x as Map)))
        .toList();
    final ends =
        (preview['milestones'] as List)
            .cast<Map>()
            .where((x) => x['operation'] == 'end' && x['voidedAt'] == null)
            .toList()
          ..sort(
            (a, b) => (b['effectiveAt'] as String).compareTo(
              a['effectiveAt'] as String,
            ),
          );
    final subject = SubjectModel(
      Map<String, dynamic>.from(preview['subject'] as Map),
    );
    final body = await showSubjectLifecycleDialog(
      context: context,
      subject: subject,
      operation: operation,
      plans: plans,
      correctsId: ends.isEmpty ? null : ends.first['id'] as String,
    );
    if (body == null || !mounted) return;
    await _api!.lifecycle(
      widget.session,
      widget.subjectId,
      body,
      subject.version,
    );
    await _load();
  }

  Future<void> _openTimeline(Map item) async {
    if (item['invalid'] == true || item['sourceType'] == 'Milestone') return;
    if (item['sourceType'] == 'SubjectEntry') {
      await _entry(entryId: item['sourceId'] as String);
    } else {
      await Navigator.push(
        context,
        MaterialPageRoute<void>(
          builder: (_) => EventDetailView(
            auth: widget.auth,
            session: widget.session,
            eventId: int.parse(item['sourceId'] as String),
          ),
        ),
      );
      if (mounted) await _load();
    }
  }

  @override
  void dispose() {
    _scroll.dispose();
    _profileApi?.dispose();
    _mediaApi?.close();
    _request++;
    if (widget.apiClient == null) _api?.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final s = _subject;
    return Scaffold(
      appBar: AppBar(
        title: Text(
          s?.isSelf == true
              ? '${_nickname ?? widget.nickname ?? '自己'}（自己）'
              : s?.name ?? '人物档案',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        actions: [
          if (s != null)
            IconButton(
              onPressed: _busy ? null : _edit,
              tooltip: '编辑资料',
              icon: const Icon(Icons.edit_outlined),
            ),
          if (s != null && !s.isSelf)
            SubjectActionsMenu(subject: s, busy: _busy, onSelected: _manage),
        ],
      ),
      body: SafeArea(
        child: s == null && _error == null
            ? const Center(child: CircularProgressIndicator())
            : CustomScrollView(
                key: PageStorageKey(
                  'subject-detail-scroll-${widget.subjectId}',
                ),
                controller: _scroll,
                keyboardDismissBehavior:
                    ScrollViewKeyboardDismissBehavior.onDrag,
                slivers: [
                  SliverPadding(
                    padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
                    sliver: SliverList.list(
                      children: [
                        if (_error != null)
                          Semantics(liveRegion: true, child: Text(_error!)),
                        if (s != null) ...[
                          Align(
                            alignment: Alignment.centerLeft,
                            child: s.isSelf
                                ? SubjectAvatar(
                                    subject: s,
                                    accountAvatar: _avatar ?? widget.avatar,
                                    size: 72,
                                  )
                                : Tooltip(
                                    message: '更换头像',
                                    child: InkWell(
                                      customBorder: const CircleBorder(),
                                      onTap: _busy ? null : _edit,
                                      child: SubjectAvatar(
                                        subject: s,
                                        size: 72,
                                        cover: s.coverMediaId == null
                                            ? null
                                            : _cover(s.coverMediaId!),
                                      ),
                                    ),
                                  ),
                          ),
                          const SizedBox(height: 12),
                          Text(
                            s.isSelf
                                ? '昵称和头像沿用账号资料'
                                : ['人', '宠物', '物品'][s.kind],
                          ),
                          Text(
                            s.state == 1
                                ? '已结束 · ${subjectReasons[s.endReason] ?? s.endReason ?? ''} · ${formatLocal(DateTime.tryParse(s.endedAt ?? ''))}'
                                : '进行中',
                          ),
                          if (s.ageDays != null)
                            Text(
                              '年龄约 ${(s.ageDays! / 365.2425).floor()} 岁${s.endReason == 'deceased' ? '（截至离世日期）' : ''}',
                            ),
                          if (s.description != null) Text(s.description!),
                          for (final f in s.fields.where((f) => !f.removed))
                            ListTile(
                              contentPadding: EdgeInsets.zero,
                              title: Text(f.name),
                              trailing: Text(
                                '${s.values[f.id] ?? '—'}${f.unit == null ? '' : ' ${f.unit}'}',
                              ),
                            ),
                          for (final id in s.mediaIds)
                            Padding(
                              padding: const EdgeInsets.symmetric(vertical: 8),
                              child: SubjectMedia(
                                auth: widget.auth,
                                session: widget.session,
                                id: id,
                              ),
                            ),
                          const Divider(),
                          SubjectRelationsSection(
                            subjectId: s.id,
                            graph: _graph!,
                            busy: _busy,
                            onAdd: () => _relation(),
                            onEdit: _relation,
                            onRemove: (relation) => _run(
                              () => _delete('SubjectRelation', relation.id),
                            ),
                          ),
                          const SizedBox(height: 24),
                          FilledButton.icon(
                            onPressed: _busy ? null : () => _entry(),
                            icon: const Icon(Icons.add),
                            label: const Text('追加记录／计划'),
                          ),
                          const SizedBox(height: 24),
                        ],
                      ],
                    ),
                  ),
                  if (s != null)
                    SubjectTimelineSection(
                      key: ValueKey('subject-timeline-${s.id}'),
                      groups: _groups,
                      filter: _filter,
                      busy: _busy,
                      loading: _loading,
                      hasMore: _cursor != null,
                      onFilter: _filterTimeline,
                      onMore: () => _timeline(more: true),
                      onOpen: _openTimeline,
                    ),
                ],
              ),
      ),
      floatingActionButton: ListenableBuilder(
        listenable: _scroll,
        builder: (context, child) => !_scroll.hasClients || _scroll.offset < 320
            ? const SizedBox.shrink()
            : FloatingActionButton.small(
                heroTag: null,
                tooltip: '回到顶部',
                onPressed: () {
                  if (MediaQuery.disableAnimationsOf(context)) {
                    _scroll.jumpTo(0);
                  } else {
                    _scroll.animateTo(
                      0,
                      duration: const Duration(milliseconds: 300),
                      curve: Curves.easeOutCubic,
                    );
                  }
                },
                child: const Icon(Icons.vertical_align_top),
              ),
      ),
    );
  }
}
