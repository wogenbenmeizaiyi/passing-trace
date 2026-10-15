import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/media_api.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';
import 'subject_model.dart';
import 'subjects_api.dart';
import 'subject_graph_layout.dart';
import 'subject_graph_canvas.dart';
import 'subject_graph_controller.dart';
import 'subject_detail_view.dart';
import 'subject_form_view.dart';
import 'subject_filter_sheet.dart';
import 'subject_list_view.dart';

class SubjectsView extends StatefulWidget {
  const SubjectsView({
    super.key,
    required this.auth,
    required this.session,
    this.drawer,
    this.bottomNavigationBar,
    this.nickname = '自己',
    this.avatar,
    this.apiClient,
  });
  final AuthService auth;
  final AuthSession session;
  final Widget? drawer, bottomNavigationBar;
  final String nickname;
  final Uint8List? avatar;
  final SubjectsApi? apiClient;
  @override
  State<SubjectsView> createState() => _SubjectsViewState();
}

class _SubjectsViewState extends State<SubjectsView> {
  SubjectsApi? _api;
  SubjectGraph? _graph;
  String? _error;
  bool _list = false;
  String _kind = '', _query = '';
  SubjectGraphController? _graphController;
  MediaApiClient? _mediaApi;
  final _covers = <String, Future<MediaAccessTarget>>{};
  static final _views =
      <
        String,
        ({String query, String kind, bool list, SubjectGraphSnapshot graph})
      >{};
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
      final graph = await _api!.graph(widget.session);
      if (!mounted) return;
      final first = _graph == null;
      if (first) {
        final saved = _views[graph.rootId];
        _graphController = SubjectGraphController(
          graph,
          restored: saved?.graph,
        );
        if (saved != null) {
          _query = saved.query;
          _kind = saved.kind;
          _list = saved.list;
        }
      } else {
        _graphController!.reconcile(graph);
      }
      setState(() {
        _graph = graph;
        _error = null;
        _covers.clear();
      });
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    }
  }

  SubjectLayout get _layout => SubjectLayout(_graph!);
  void _saveView() {
    if (_graph != null) {
      _views[_graph!.rootId] = (
        query: _query,
        kind: _kind,
        list: _list,
        graph: _graphController!.snapshot(),
      );
    }
  }

  Future<void> _open(String id) async {
    _saveView();
    await Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => SubjectDetailView(
          auth: widget.auth,
          session: widget.session,
          subjectId: id,
          apiClient: widget.apiClient,
          nickname: widget.nickname,
          avatar: widget.avatar,
        ),
      ),
    );
    if (mounted) await _load();
  }

  Future<void> _create() async {
    await Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) =>
            SubjectFormView(auth: widget.auth, session: widget.session),
      ),
    );
    if (mounted) await _load();
  }

  Future<void> _filters() async {
    final selection = await showSubjectFilterSheet(
      context: context,
      selection: SubjectFilterSelection(query: _query, kind: _kind),
    );
    if (selection == null || !mounted) return;
    setState(() {
      _query = selection.query;
      _kind = selection.kind;
    });
  }

  Future<MediaAccessTarget> _cover(String id) =>
      _covers.putIfAbsent(id, () async {
        final base = await widget.auth.getEventsApiBaseUrl();
        if (!mounted) throw StateError('档案列表已关闭');
        _mediaApi ??= MediaApiClient(auth: widget.auth, baseUrl: base);
        return _mediaApi!.access(widget.session, id);
      });

  @override
  void dispose() {
    _saveView();
    if (widget.apiClient == null) _api?.close();
    _graphController?.dispose();
    _mediaApi?.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors, graph = _graph;
    final matches =
        graph?.nodes
            .where(
              (s) =>
                  (s.isSelf ? widget.nickname : s.name).contains(_query) &&
                  (_kind.isEmpty || '${s.kind}' == _kind),
            )
            .map((s) => s.id)
            .toSet() ??
        <String>{};
    final kept = graph == null
        ? <String>{}
        : _layout.pathsToRoot(matches, graph.rootId);
    return Scaffold(
      drawer: widget.drawer,
      bottomNavigationBar: widget.bottomNavigationBar,
      appBar: TraceAppBar(
        title: '人物',
        leading: widget.drawer == null
            ? null
            : Builder(
                builder: (context) => SizedBox.square(
                  dimension: 48,
                  child: TraceIconButton(
                    glyph: TraceGlyph.menu,
                    tooltip: '打开菜单',
                    onPressed: () => Scaffold.of(context).openDrawer(),
                  ),
                ),
              ),
        trailingWidth: 96,
        trailing: Row(
          children: [
            IconButton(
              onPressed: () => setState(() => _list = !_list),
              tooltip: _list ? '关系图' : '列表与关系清单',
              icon: Icon(_list ? Icons.hub_outlined : Icons.list),
            ),
            Expanded(
              child: TraceIconButton(
                key: const Key('subjects-filter-button'),
                glyph: TraceGlyph.filter,
                tooltip: _query.isEmpty && _kind.isEmpty
                    ? '筛选人物'
                    : '筛选人物，已应用 ${SubjectFilterSelection(query: _query, kind: _kind).activeCount} 项',
                color: _query.isEmpty && _kind.isEmpty
                    ? colors.inkSecondary
                    : colors.primaryStrong,
                onPressed: _filters,
              ),
            ),
          ],
        ),
      ),
      floatingActionButton: SizedBox.square(
        dimension: 48,
        child: FloatingActionButton(
          heroTag: 'subjects-create',
          onPressed: _create,
          tooltip: '新建档案',
          child: const TraceIcon(TraceGlyph.add),
        ),
      ),
      floatingActionButtonLocation: FloatingActionButtonLocation.endFloat,
      body: SafeArea(
        child: Column(
          children: [
            if (_error != null)
              Padding(
                padding: const EdgeInsets.all(12),
                child: Semantics(
                  liveRegion: true,
                  child: Text(_error!, style: TextStyle(color: colors.danger)),
                ),
              ),
            if (graph == null && _error == null)
              const Expanded(child: Center(child: CircularProgressIndicator())),
            if (graph != null)
              Expanded(
                child: _list
                    ? SubjectListView(
                        graph: graph,
                        matches: matches,
                        kept: kept,
                        nickname: widget.nickname,
                        avatar: widget.avatar,
                        coverLoader: _cover,
                        onOpen: _open,
                      )
                    : SubjectGraphCanvas(
                        graph: graph,
                        controller: _graphController!,
                        matches: matches,
                        kept: kept,
                        nickname: widget.nickname,
                        avatar: widget.avatar,
                        coverLoader: _cover,
                        onOpen: _open,
                      ),
              ),
          ],
        ),
      ),
    );
  }
}
