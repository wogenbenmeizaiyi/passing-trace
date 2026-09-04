import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/event_model.dart';
import '../events/events_api.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';
import 'event_detail_view.dart';
import 'event_filter_sheet.dart';
import 'event_form_view.dart';
import 'event_widgets.dart';

class EventsListView extends StatefulWidget {
  const EventsListView({
    super.key,
    required this.auth,
    required this.session,
    this.drawer,
    this.bottomNavigationBar,
    this.onSessionExpired,
    this.eventApiClient,
  });

  final AuthService auth;
  final AuthSession session;
  final Widget? drawer;
  final Widget? bottomNavigationBar;
  final Future<void> Function()? onSessionExpired;
  final EventApiClient? eventApiClient;

  @override
  State<EventsListView> createState() => _EventsListViewState();
}

class _EventsListViewState extends State<EventsListView> {
  late EventApiClient _api;
  final List<EventModel> _items = [];
  int? _nextCursor;
  bool _initialLoading = true;
  bool _loadingMore = false;
  String? _error;
  EventTaxonomyModel? _taxonomy;
  EventFilterSelection _filters = EventFilterSelection();
  final Set<int> _collapsedYears = {};
  final Set<String> _collapsedMonths = {};
  final Set<int> _knownYears = {};
  final Set<String> _knownMonths = {};
  bool _ownsApi = false;
  bool _apiInitialized = false;

  @override
  void initState() {
    super.initState();
    _initApi();
  }

  Future<void> _initApi() async {
    if (widget.eventApiClient case final api?) {
      _api = api;
      _apiInitialized = true;
      await _reload();
      if (mounted) await _loadTaxonomy();
      return;
    }
    final baseUrl = await widget.auth.getEventsApiBaseUrl();
    if (!mounted) return;
    _api = EventApiClient(auth: widget.auth, baseUrl: baseUrl);
    _apiInitialized = true;
    _ownsApi = true;
    await _reload();
    if (mounted) await _loadTaxonomy();
  }

  Future<void> _loadTaxonomy() async {
    try {
      final taxonomy = await _api.taxonomy(widget.session);
      if (mounted) setState(() => _taxonomy = taxonomy);
    } on EventApiException catch (error) {
      if (error.status == 401 && mounted) await _handleSessionExpired();
      // 分类数据加载失败不阻塞时间、类型和状态筛选。
    } catch (_) {
      // 分类数据加载失败不阻塞记录列表。
    }
  }

  @override
  void dispose() {
    if (_apiInitialized && _ownsApi) _api.close();
    super.dispose();
  }

  Future<void> _reload() async {
    setState(() {
      _initialLoading = true;
      _error = null;
    });
    try {
      final page = await _api.list(
        widget.session,
        limit: 50,
        kind: _filters.kind,
        status: _filters.status,
        from: _filters.fromIso8601,
        to: _filters.toIso8601,
        categoryKey: _filters.categoryKey,
        tagKeys: _filters.tagKeys,
      );
      if (!mounted) return;
      setState(() {
        _items
          ..clear()
          ..addAll(page.items);
        _nextCursor = page.nextCursor;
        _syncArchiveState(reset: true);
      });
    } on EventApiException catch (error) {
      if (!mounted) return;
      if (error.status == 401) {
        await _handleSessionExpired();
        return;
      }
      setState(() => _error = error.message);
    } catch (error) {
      if (!mounted) return;
      setState(() => _error = '加载失败：$error');
    } finally {
      if (mounted) setState(() => _initialLoading = false);
    }
  }

  Future<void> _loadMore() async {
    if (_nextCursor == null || _loadingMore) return;
    setState(() {
      _loadingMore = true;
      _error = null;
    });
    try {
      final page = await _api.list(
        widget.session,
        limit: 50,
        cursor: _nextCursor,
        kind: _filters.kind,
        status: _filters.status,
        from: _filters.fromIso8601,
        to: _filters.toIso8601,
        categoryKey: _filters.categoryKey,
        tagKeys: _filters.tagKeys,
      );
      if (!mounted) return;
      setState(() {
        _items.addAll(page.items);
        _nextCursor = page.nextCursor;
        _syncArchiveState();
      });
    } on EventApiException catch (error) {
      if (!mounted) return;
      if (error.status == 401) {
        await _handleSessionExpired();
        return;
      }
      setState(() => _error = error.message);
    } catch (error) {
      if (!mounted) return;
      setState(() => _error = '加载更多失败：$error');
    } finally {
      if (mounted) setState(() => _loadingMore = false);
    }
  }

  Future<void> _openDetail(EventModel event) async {
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => EventDetailView(
          auth: widget.auth,
          session: widget.session,
          eventId: event.id,
        ),
      ),
    );
    if (mounted) await _reload();
  }

  Future<void> _openCreate() async {
    final result = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) =>
            EventFormView(auth: widget.auth, session: widget.session),
      ),
    );
    if (result == true && mounted) await _reload();
  }

  Future<void> _handleSessionExpired() async {
    final handler = widget.onSessionExpired;
    if (handler != null) {
      await handler();
    } else if (mounted) {
      Navigator.of(context).pop(true);
    }
  }

  Future<void> _clearFilters() async {
    setState(() => _filters = EventFilterSelection());
    await _reload();
  }

  Future<void> _showFilters() async {
    if (_taxonomy == null) await _loadTaxonomy();
    if (!mounted) return;
    final selection = await showEventFilterSheet(
      context: context,
      selection: _filters,
      taxonomy: _taxonomy,
    );
    if (selection == null || !mounted) return;
    setState(() => _filters = selection);
    await _reload();
  }

  bool get _hasActiveFilters => _filters.hasFilters;

  @override
  Widget build(BuildContext context) => Scaffold(
    drawer: widget.drawer,
    bottomNavigationBar: widget.bottomNavigationBar,
    appBar: TraceAppBar(
      title: '我的记录',
      leading: Builder(
        builder: (context) => TraceIconButton(
          glyph: TraceGlyph.menu,
          tooltip: '打开菜单',
          onPressed: () => Scaffold.of(context).openDrawer(),
        ),
      ),
      trailing: TraceIconButton(
        glyph: TraceGlyph.add,
        tooltip: '新建记录',
        onPressed: _openCreate,
      ),
    ),
    floatingActionButton: _buildFixedFilterButton(),
    floatingActionButtonLocation: FloatingActionButtonLocation.endFloat,
    body: _buildBody(),
  );

  Widget _buildFixedFilterButton() {
    final colors = context.traceColors;
    return SizedBox.square(
      key: const Key('events-filter-button'),
      dimension: 52,
      child: TraceIconButton(
        glyph: TraceGlyph.filter,
        tooltip: _hasActiveFilters
            ? '筛选记录，已应用 ${_filters.activeCount} 项'
            : '筛选记录',
        onPressed: _showFilters,
        color: _hasActiveFilters ? colors.primaryStrong : colors.inkSecondary,
        backgroundColor: _hasActiveFilters
            ? colors.primarySoft
            : colors.surface,
        borderColor: _hasActiveFilters ? colors.primary : colors.lineStrong,
      ),
    );
  }

  Widget _buildBody() {
    if (_initialLoading) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_error != null && _items.isEmpty) {
      return _MessageView(
        title: '无法加载记录',
        detail: _error!,
        actionText: '重试',
        onAction: _reload,
      );
    }

    final entries = _timelineEntries();
    return _items.isEmpty
        ? _MessageView(
            title: '还没有记录',
            detail: _hasActiveFilters
                ? '没有符合当前筛选条件的记录。'
                : '从今天开始，留下第一件值得回看的小事。',
            actionText: _hasActiveFilters ? '清除筛选' : '记一笔',
            onAction: _hasActiveFilters ? _clearFilters : _openCreate,
          )
        : RefreshIndicator(
            onRefresh: _reload,
            child: ListView.builder(
              padding: const EdgeInsets.fromLTRB(18, 20, 18, 28),
              itemCount: entries.length,
              itemBuilder: (context, index) => _buildEntry(entries[index]),
            ),
          );
  }

  List<_TimelineEntry> _timelineEntries() {
    final sorted = [..._items]
      ..sort((a, b) => _eventTime(b).compareTo(_eventTime(a)));
    final years = <int, Map<int, Map<DateTime, List<EventModel>>>>{};
    for (final event in sorted) {
      final time = _eventTime(event);
      final day = DateTime(time.year, time.month, time.day);
      years
          .putIfAbsent(time.year, () => {})
          .putIfAbsent(time.month, () => {})
          .putIfAbsent(day, () => [])
          .add(event);
    }

    final entries = <_TimelineEntry>[
      _IntroEntry(hasFilters: _hasActiveFilters),
    ];
    for (final yearEntry in years.entries) {
      final yearCount = yearEntry.value.values.fold<int>(
        0,
        (sum, days) =>
            sum +
            days.values.fold<int>(
              0,
              (daySum, events) => daySum + events.length,
            ),
      );
      entries.add(
        _YearEntry(
          yearEntry.key,
          yearCount,
          collapsed: _collapsedYears.contains(yearEntry.key),
        ),
      );
      if (_collapsedYears.contains(yearEntry.key)) continue;
      for (final monthEntry in yearEntry.value.entries) {
        final monthKey = _monthKey(yearEntry.key, monthEntry.key);
        final monthCount = monthEntry.value.values.fold<int>(
          0,
          (sum, events) => sum + events.length,
        );
        entries.add(
          _MonthEntry(
            yearEntry.key,
            monthEntry.key,
            monthCount,
            collapsed: _collapsedMonths.contains(monthKey),
          ),
        );
        if (_collapsedMonths.contains(monthKey)) continue;
        for (final dayEntry in monthEntry.value.entries) {
          entries.add(_DayEntry(dayEntry.key, dayEntry.value.length));
          for (var index = 0; index < dayEntry.value.length; index++) {
            entries.add(
              _EventEntry(
                dayEntry.value[index],
                isLastInDay: index == dayEntry.value.length - 1,
              ),
            );
          }
        }
      }
    }
    entries.add(const _FooterEntry());
    return entries;
  }

  Widget _buildEntry(_TimelineEntry entry) => switch (entry) {
    _IntroEntry() => _buildIntro(entry),
    _YearEntry() => _buildYearHeader(entry),
    _MonthEntry() => _buildMonthHeader(entry),
    _DayEntry() => _buildDayHeader(entry),
    _EventEntry() => _buildTimelineEvent(entry),
    _FooterEntry() => _buildFooter(),
  };

  Widget _buildIntro(_IntroEntry entry) {
    final colors = context.traceColors;
    return Padding(
      padding: const EdgeInsets.only(bottom: 26),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '时间里的生活',
            style: TextStyle(
              color: colors.ink,
              fontSize: 26,
              height: 1.25,
              fontWeight: FontWeight.w700,
              letterSpacing: -1,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            entry.hasFilters ? '正在显示筛选后的经历' : '按年份和月份展开，回看每一段生活',
            style: TextStyle(color: colors.inkSecondary, fontSize: 13),
          ),
        ],
      ),
    );
  }

  Widget _buildYearHeader(_YearEntry entry) {
    final colors = context.traceColors;
    final motionDuration =
        MediaQuery.maybeOf(context)?.disableAnimations ?? false
        ? Duration.zero
        : const Duration(milliseconds: 140);
    return Padding(
      key: ValueKey('events-year-${entry.year}'),
      padding: const EdgeInsets.only(top: 2, bottom: 8),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(12),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: () => setState(() {
            if (!_collapsedYears.remove(entry.year)) {
              _collapsedYears.add(entry.year);
            }
          }),
          child: Semantics(
            button: true,
            expanded: !entry.collapsed,
            label: '${entry.year} 年，共 ${entry.count} 条记录',
            excludeSemantics: true,
            child: ConstrainedBox(
              constraints: const BoxConstraints(minHeight: 52),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      '${entry.year} 年',
                      style: TextStyle(
                        color: colors.ink,
                        fontSize: 22,
                        fontWeight: FontWeight.w700,
                        letterSpacing: -0.6,
                      ),
                    ),
                  ),
                  Text(
                    '${entry.count} 条',
                    style: TextStyle(color: colors.inkMuted, fontSize: 12),
                  ),
                  const SizedBox(width: 8),
                  AnimatedRotation(
                    turns: entry.collapsed ? 0 : 0.5,
                    duration: motionDuration,
                    child: TraceIcon(
                      TraceGlyph.chevronDown,
                      size: 18,
                      color: colors.inkMuted,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildMonthHeader(_MonthEntry entry) {
    final colors = context.traceColors;
    final key = _monthKey(entry.year, entry.month);
    final motionDuration =
        MediaQuery.maybeOf(context)?.disableAnimations ?? false
        ? Duration.zero
        : const Duration(milliseconds: 140);
    return Padding(
      key: ValueKey('events-month-$key'),
      padding: const EdgeInsets.only(bottom: 10),
      child: Material(
        color: colors.surfaceSoft,
        borderRadius: BorderRadius.circular(12),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: () => setState(() {
            if (!_collapsedMonths.remove(key)) _collapsedMonths.add(key);
          }),
          child: Semantics(
            button: true,
            expanded: !entry.collapsed,
            label: '${entry.year} 年 ${entry.month} 月，共 ${entry.count} 条记录',
            excludeSemantics: true,
            child: Container(
              constraints: const BoxConstraints(minHeight: 48),
              padding: const EdgeInsets.symmetric(horizontal: 12),
              decoration: BoxDecoration(
                border: Border.all(color: colors.line),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      '${_monthLabel(entry.month)}月',
                      style: TextStyle(
                        color: colors.ink,
                        fontSize: 15,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                  Text(
                    '${entry.count} 条',
                    style: TextStyle(color: colors.inkMuted, fontSize: 11),
                  ),
                  const SizedBox(width: 8),
                  AnimatedRotation(
                    turns: entry.collapsed ? 0 : 0.5,
                    duration: motionDuration,
                    child: TraceIcon(
                      TraceGlyph.chevronDown,
                      size: 17,
                      color: colors.inkMuted,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildDayHeader(_DayEntry entry) {
    final colors = context.traceColors;
    final label = _relativeDayLabel(entry.day);
    return Padding(
      padding: const EdgeInsets.only(bottom: 12, top: 2),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.baseline,
        textBaseline: TextBaseline.alphabetic,
        children: [
          Text(
            label,
            style: TextStyle(
              color: colors.ink,
              fontSize: 15,
              fontWeight: FontWeight.w700,
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              '${entry.day.month} 月 ${entry.day.day} 日 · ${_weekday(entry.day.weekday)} · ${entry.count} 条',
              style: TextStyle(color: colors.inkTertiary, fontSize: 12),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildTimelineEvent(_EventEntry entry) {
    final colors = context.traceColors;
    return Padding(
      padding: EdgeInsets.only(bottom: entry.isLastInDay ? 28 : 12),
      child: IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SizedBox(
              width: 14,
              child: Stack(
                children: [
                  Positioned(
                    left: 5,
                    top: 0,
                    bottom: entry.isLastInDay ? 24 : 0,
                    child: Container(width: 1, color: colors.lineStrong),
                  ),
                  Positioned(
                    left: 1,
                    top: 21,
                    child: Container(
                      width: 9,
                      height: 9,
                      decoration: BoxDecoration(
                        color: colors.primary,
                        shape: BoxShape.circle,
                        border: Border.all(color: colors.surfaceSoft, width: 2),
                        boxShadow: [
                          BoxShadow(color: colors.primary, spreadRadius: 1),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: EventCard(
                event: entry.event,
                onTap: () => _openDetail(entry.event),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildFooter() {
    final colors = context.traceColors;
    if (_nextCursor == null) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 16),
        child: Center(
          child: Text(
            '已经看到最早的一条记录',
            style: TextStyle(color: colors.inkTertiary, fontSize: 12),
          ),
        ),
      );
    }
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: OutlinedButton(
        onPressed: _loadingMore ? null : _loadMore,
        child: Text(_loadingMore ? '加载中…' : _error ?? '加载更多'),
      ),
    );
  }

  static DateTime _eventTime(EventModel event) =>
      (event.kind == EventKind.plan
              ? event.plannedAt
              : event.happenedAt ?? event.createdAt)
          ?.toLocal() ??
      event.createdAt.toLocal();

  void _syncArchiveState({bool reset = false}) {
    if (reset) {
      _collapsedYears.clear();
      _collapsedMonths.clear();
      _knownYears.clear();
      _knownMonths.clear();
    }
    if (_items.isEmpty) return;
    final newest = _items
        .map(_eventTime)
        .reduce((a, b) => a.isAfter(b) ? a : b);
    for (final item in _items) {
      final time = _eventTime(item);
      if (_knownYears.add(time.year) && time.year != newest.year) {
        _collapsedYears.add(time.year);
      }
      final key = _monthKey(time.year, time.month);
      if (_knownMonths.add(key) &&
          (time.year != newest.year || time.month != newest.month)) {
        _collapsedMonths.add(key);
      }
    }
  }

  static String _monthKey(int year, int month) =>
      '$year-${month.toString().padLeft(2, '0')}';

  static String _monthLabel(int month) => const [
    '一',
    '二',
    '三',
    '四',
    '五',
    '六',
    '七',
    '八',
    '九',
    '十',
    '十一',
    '十二',
  ][month - 1];

  static String _weekday(int weekday) =>
      const ['星期一', '星期二', '星期三', '星期四', '星期五', '星期六', '星期日'][weekday - 1];

  static String _relativeDayLabel(DateTime day) {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    final difference = today.difference(day).inDays;
    if (difference == 0) return '今天';
    if (difference == 1) return '昨天';
    return '${day.month} 月 ${day.day} 日';
  }
}

sealed class _TimelineEntry {
  const _TimelineEntry();
}

class _IntroEntry extends _TimelineEntry {
  const _IntroEntry({required this.hasFilters});
  final bool hasFilters;
}

class _YearEntry extends _TimelineEntry {
  const _YearEntry(this.year, this.count, {required this.collapsed});
  final int year;
  final int count;
  final bool collapsed;
}

class _MonthEntry extends _TimelineEntry {
  const _MonthEntry(
    this.year,
    this.month,
    this.count, {
    required this.collapsed,
  });
  final int year;
  final int month;
  final int count;
  final bool collapsed;
}

class _DayEntry extends _TimelineEntry {
  const _DayEntry(this.day, this.count);
  final DateTime day;
  final int count;
}

class _EventEntry extends _TimelineEntry {
  const _EventEntry(this.event, {required this.isLastInDay});
  final EventModel event;
  final bool isLastInDay;
}

class _FooterEntry extends _TimelineEntry {
  const _FooterEntry();
}

class _MessageView extends StatelessWidget {
  const _MessageView({
    required this.title,
    required this.detail,
    this.actionText,
    this.onAction,
  });

  final String title;
  final String detail;
  final String? actionText;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 52,
              height: 52,
              decoration: BoxDecoration(
                color: colors.primarySoft,
                borderRadius: BorderRadius.circular(14),
              ),
              child: Center(
                child: TraceIcon(
                  TraceGlyph.journal,
                  size: 25,
                  color: colors.primaryStrong,
                ),
              ),
            ),
            const SizedBox(height: 16),
            Text(
              title,
              style: TextStyle(
                color: colors.ink,
                fontSize: 22,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 8),
            Text(
              detail,
              textAlign: TextAlign.center,
              style: TextStyle(color: colors.inkSecondary, height: 1.6),
            ),
            if (onAction != null && actionText != null) ...[
              const SizedBox(height: 18),
              FilledButton(onPressed: onAction, child: Text(actionText!)),
            ],
          ],
        ),
      ),
    );
  }
}
