import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../storylines/storyline_api.dart';
import '../storylines/storyline_model.dart';
import '../events/media_api.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';
import 'storyline_create_view.dart';
import 'storyline_detail_view.dart';

class StorylinesListView extends StatefulWidget {
  const StorylinesListView({
    super.key,
    required this.auth,
    required this.session,
    this.drawer,
    this.bottomNavigationBar,
    this.onSessionExpired,
    this.apiClient,
  });

  final AuthService auth;
  final AuthSession session;
  final Widget? drawer;
  final Widget? bottomNavigationBar;
  final Future<void> Function()? onSessionExpired;
  final StorylineApiClient? apiClient;

  @override
  State<StorylinesListView> createState() => _StorylinesListViewState();
}

class _StorylinesListViewState extends State<StorylinesListView> {
  late StorylineApiClient _api;
  MediaApiClient? _mediaApi;
  bool _ownsApi = false;
  bool _loading = true;
  String? _error;
  List<StorylineSummary> _items = [];
  StorylineStatus? _status;
  String? _category;
  DateTime? _from;
  DateTime? _to;
  final Map<String, Future<Uri>> _coverUrls = {};

  @override
  void initState() {
    super.initState();
    _init();
  }

  Future<void> _init() async {
    if (widget.apiClient case final api?) {
      _api = api;
    } else {
      final base = await widget.auth.getEventsApiBaseUrl();
      _api = StorylineApiClient(auth: widget.auth, baseUrl: base);
      _mediaApi = MediaApiClient(auth: widget.auth, baseUrl: base);
      _ownsApi = true;
    }
    await _load();
  }

  @override
  void dispose() {
    if (_ownsApi) {
      _api.close();
      _mediaApi?.close();
    }
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final rows = await _api.list(
        widget.session,
        status: _status?.value,
        categoryKey: _category,
        from: _from?.toUtc().toIso8601String(),
        to: _to
            ?.add(const Duration(days: 1))
            .subtract(const Duration(microseconds: 1))
            .toUtc()
            .toIso8601String(),
      );
      if (mounted) {
        setState(() {
          _items = rows;
          for (final item in rows) {
            final coverId = item.coverMediaAssetId;
            if (coverId != null && _mediaApi != null) {
              _coverUrls.putIfAbsent(
                coverId,
                () => _mediaApi!.access(widget.session, coverId),
              );
            }
          }
        });
      }
    } catch (error) {
      if (mounted) setState(() => _error = '$error');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _create() async {
    final id = await Navigator.of(context).push<String>(
      MaterialPageRoute(
        builder: (_) =>
            StorylineCreateView(auth: widget.auth, session: widget.session),
      ),
    );
    if (id != null && mounted) {
      await _load();
      if (mounted) await _open(id);
    }
  }

  Future<void> _open(String id) async {
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => StorylineDetailView(
          auth: widget.auth,
          session: widget.session,
          storylineId: id,
        ),
      ),
    );
    if (mounted) await _load();
  }

  Future<void> _filters() async {
    var selectedStatus = _status;
    var selectedCategory = _category;
    var selectedFrom = _from;
    var selectedTo = _to;
    final applied = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (context) => StatefulBuilder(
        builder: (context, setLocal) => SafeArea(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(20, 18, 20, 24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('筛选故事线', style: Theme.of(context).textTheme.titleLarge),
                const SizedBox(height: 18),
                const TraceFieldLabel('进度'),
                DropdownButtonFormField<StorylineStatus?>(
                  initialValue: selectedStatus,
                  items: [
                    const DropdownMenuItem(value: null, child: Text('全部')),
                    for (final item in StorylineStatus.values)
                      DropdownMenuItem(value: item, child: Text(item.label)),
                  ],
                  onChanged: (value) => setLocal(() => selectedStatus = value),
                ),
                const SizedBox(height: 14),
                const TraceFieldLabel('分类'),
                DropdownButtonFormField<String?>(
                  initialValue: selectedCategory,
                  items: const [
                    DropdownMenuItem(value: null, child: Text('全部')),
                    DropdownMenuItem(value: 'trip', child: Text('行程旅行')),
                    DropdownMenuItem(value: 'activity', child: Text('活动纪实')),
                    DropdownMenuItem(value: 'project', child: Text('项目过程')),
                    DropdownMenuItem(value: 'challenge', child: Text('目标挑战')),
                    DropdownMenuItem(value: 'lifecycle', child: Text('成长陪伴')),
                    DropdownMenuItem(value: 'series', child: Text('主题系列')),
                    DropdownMenuItem(value: 'life-period', child: Text('生活阶段')),
                    DropdownMenuItem(value: 'other', child: Text('其他')),
                  ],
                  onChanged: (value) =>
                      setLocal(() => selectedCategory = value),
                ),
                const SizedBox(height: 14),
                const TraceFieldLabel('时间范围'),
                Row(
                  children: [
                    Expanded(
                      child: TraceRowButton(
                        glyph: TraceGlyph.calendar,
                        title: selectedFrom == null
                            ? '开始日期'
                            : '${selectedFrom!.year}-${selectedFrom!.month}-${selectedFrom!.day}',
                        subtitle: '可不填',
                        onTap: () async {
                          final value = await showDatePicker(
                            context: context,
                            initialDate: selectedFrom ?? DateTime.now(),
                            firstDate: DateTime(2000),
                            lastDate: DateTime(2100),
                          );
                          if (value != null) {
                            setLocal(() => selectedFrom = value);
                          }
                        },
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: TraceRowButton(
                        glyph: TraceGlyph.calendar,
                        title: selectedTo == null
                            ? '结束日期'
                            : '${selectedTo!.year}-${selectedTo!.month}-${selectedTo!.day}',
                        subtitle: '可不填',
                        onTap: () async {
                          final value = await showDatePicker(
                            context: context,
                            initialDate:
                                selectedTo ?? selectedFrom ?? DateTime.now(),
                            firstDate: selectedFrom ?? DateTime(2000),
                            lastDate: DateTime(2100),
                          );
                          if (value != null) {
                            setLocal(() => selectedTo = value);
                          }
                        },
                      ),
                    ),
                  ],
                ),
                if (selectedFrom != null || selectedTo != null)
                  Align(
                    alignment: Alignment.centerRight,
                    child: TextButton(
                      onPressed: () => setLocal(() {
                        selectedFrom = null;
                        selectedTo = null;
                      }),
                      child: const Text('清除时间'),
                    ),
                  ),
                const SizedBox(height: 22),
                SizedBox(
                  width: double.infinity,
                  height: 52,
                  child: FilledButton(
                    onPressed: () => Navigator.pop(context, true),
                    child: const Text('应用筛选'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
    if (applied == true && mounted) {
      setState(() {
        _status = selectedStatus;
        _category = selectedCategory;
        _from = selectedFrom;
        _to = selectedTo;
      });
      await _load();
    }
  }

  String _range(StorylineSummary item) {
    String format(DateTime? value) =>
        value == null ? '未定' : '${value.month}月${value.day}日';
    if (item.rangeStart == null && item.rangeEnd == null) return '时间范围待补充';
    return '${format(item.rangeStart)} — ${format(item.rangeEnd)}';
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    drawer: widget.drawer,
    bottomNavigationBar: widget.bottomNavigationBar,
    appBar: TraceAppBar(
      title: '故事线',
      leading: Builder(
        builder: (context) => TraceIconButton(
          glyph: TraceGlyph.menu,
          tooltip: '打开菜单',
          onPressed: () => Scaffold.of(context).openDrawer(),
        ),
      ),
      trailing: TraceIconButton(
        glyph: TraceGlyph.filter,
        tooltip: '筛选',
        onPressed: _filters,
      ),
    ),
    floatingActionButton: FloatingActionButton(
      onPressed: _create,
      tooltip: '新建故事线',
      child: const TraceIcon(TraceGlyph.add),
    ),
    body: RefreshIndicator(onRefresh: _load, child: _body()),
  );

  Widget _body() {
    if (_loading) return const Center(child: CircularProgressIndicator());
    if (_error != null) {
      return ListView(
        children: [
          Padding(
            padding: const EdgeInsets.all(24),
            child: Text(
              _error!,
              style: TextStyle(color: context.traceColors.danger),
            ),
          ),
        ],
      );
    }
    if (_items.isEmpty) {
      return ListView(
        children: [
          const SizedBox(height: 150),
          Center(
            child: Column(
              children: [
                const TraceIcon(TraceGlyph.storyline, size: 48),
                const SizedBox(height: 14),
                Text('还没有故事线', style: Theme.of(context).textTheme.titleLarge),
                const SizedBox(height: 6),
                Text(
                  '从一次旅行、活动或项目开始',
                  style: TextStyle(color: context.traceColors.inkTertiary),
                ),
              ],
            ),
          ),
        ],
      );
    }
    return ListView.separated(
      padding: const EdgeInsets.fromLTRB(18, 18, 18, 100),
      itemCount: _items.length,
      separatorBuilder: (_, _) => const SizedBox(height: 12),
      itemBuilder: (context, index) => _card(_items[index]),
    );
  }

  Widget _card(StorylineSummary item) => Material(
    color: context.traceColors.surface,
    shape: RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(16),
      side: BorderSide(color: context.traceColors.line),
    ),
    clipBehavior: Clip.antiAlias,
    child: InkWell(
      onTap: () => _open(item.id),
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 142),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            children: [
              Container(
                width: 76,
                height: 100,
                decoration: BoxDecoration(
                  color: context.traceColors.primarySoft,
                  borderRadius: BorderRadius.circular(13),
                ),
                clipBehavior: Clip.antiAlias,
                child:
                    item.coverMediaAssetId == null ||
                        !_coverUrls.containsKey(item.coverMediaAssetId)
                    ? Center(
                        child: TraceIcon(
                          TraceGlyph.storyline,
                          size: 30,
                          color: context.traceColors.primaryStrong,
                        ),
                      )
                    : FutureBuilder<Uri>(
                        future: _coverUrls[item.coverMediaAssetId!],
                        builder: (context, snapshot) => snapshot.hasData
                            ? Image.network(
                                snapshot.data.toString(),
                                fit: BoxFit.cover,
                                errorBuilder: (_, _, _) => const Center(
                                  child: TraceIcon(TraceGlyph.storyline),
                                ),
                              )
                            : const Center(
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              ),
                      ),
              ),
              const SizedBox(width: 15),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Wrap(
                      spacing: 6,
                      children: [
                        TraceTag(label: item.categoryLabel, category: true),
                        TraceTag(label: item.status.label),
                      ],
                    ),
                    const SizedBox(height: 10),
                    Text(
                      item.title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 5),
                    Text(
                      item.description ?? '把散落的记录慢慢连起来',
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 12,
                        color: context.traceColors.inkSecondary,
                      ),
                    ),
                    const SizedBox(height: 11),
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            _range(item),
                            style: TextStyle(
                              fontSize: 10,
                              color: context.traceColors.inkTertiary,
                            ),
                          ),
                        ),
                        Text(
                          '${item.nodeCount} 个节点',
                          style: TextStyle(
                            fontSize: 10,
                            color: context.traceColors.inkSecondary,
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}
