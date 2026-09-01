// 时间线列表页：
//   - 游标分页（"加载更多"）。
//   - 类型 / 状态筛选。
//   - 顶部"新建"按钮。

import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/event_model.dart';
import '../events/events_api.dart';
import '../theme/passingtrace_theme.dart';
import 'event_detail_view.dart';
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
  });

  final AuthService auth;
  final AuthSession session;
  final Widget? drawer;
  final Widget? bottomNavigationBar;
  final Future<void> Function()? onSessionExpired;

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
  EventKind? _filterKind;
  EventStatus? _filterStatus;
  bool _filtersOpen = false;

  @override
  void initState() {
    super.initState();
    _initApi();
  }

  Future<void> _initApi() async {
    final baseUrl = await widget.auth.getEventsApiBaseUrl();
    if (!mounted) return;
    setState(() {
      _api = EventApiClient(auth: widget.auth, baseUrl: baseUrl);
    });
    await _reload();
  }

  @override
  void dispose() {
    _api.close();
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
        kind: _filterKind,
        status: _filterStatus,
      );
      if (!mounted) return;
      setState(() {
        _items
          ..clear()
          ..addAll(page.items);
        _nextCursor = page.nextCursor;
      });
    } on EventApiException catch (e) {
      if (!mounted) return;
      if (e.status == 401) {
        await _handleSessionExpired();
        return;
      }
      setState(() => _error = e.message);
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = '加载失败：$e');
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
        kind: _filterKind,
        status: _filterStatus,
      );
      if (!mounted) return;
      setState(() {
        _items.addAll(page.items);
        _nextCursor = page.nextCursor;
      });
    } on EventApiException catch (e) {
      if (!mounted) return;
      if (e.status == 401) {
        await _handleSessionExpired();
        return;
      }
      setState(() => _error = e.message);
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = '加载更多失败：$e');
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
      return;
    }
    if (mounted) Navigator.of(context).pop(true);
  }

  void _toggleFilter() {
    setState(() {
      _filtersOpen = !_filtersOpen;
    });
  }

  void _clearFilters() {
    setState(() {
      _filterKind = null;
      _filterStatus = null;
    });
    _reload();
  }

  bool get _hasActiveFilters => _filterKind != null || _filterStatus != null;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      drawer: widget.drawer,
      bottomNavigationBar: widget.bottomNavigationBar,
      appBar: AppBar(
        title: const Text('我的记录'),
        actions: [
          IconButton(
            tooltip: _filtersOpen ? '收起筛选' : '筛选',
            onPressed: _toggleFilter,
            icon: Icon(
              _filtersOpen ? Icons.filter_alt : Icons.filter_alt_outlined,
              color: _hasActiveFilters
                  ? Theme.of(context).colorScheme.primary
                  : null,
            ),
          ),
        ],
      ),
      body: _buildBody(),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _openCreate,
        icon: const Icon(Icons.edit_outlined),
        label: const Text('记一笔'),
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
    return Column(
      children: [
        if (_filtersOpen) _buildFilterBar(),
        Expanded(
          child: _items.isEmpty
              ? _MessageView(
                  title: '还没有记录',
                  detail: '点右下角"记一笔"写下第一条。',
                  actionText: '重新筛选',
                  onAction: _hasActiveFilters ? _clearFilters : null,
                )
              : RefreshIndicator(
                  onRefresh: _reload,
                  child: ListView.builder(
                    padding: const EdgeInsets.fromLTRB(20, 14, 20, 96),
                    itemCount: _items.length + 1,
                    itemBuilder: (context, index) {
                      if (index == _items.length) {
                        return _buildFooter();
                      }
                      final event = _items[index];
                      return EventCard(
                        event: event,
                        onTap: () => _openDetail(event),
                      );
                    },
                  ),
                ),
        ),
      ],
    );
  }

  Widget _buildFilterBar() {
    final colors = context.traceColors;
    return Container(
      color: colors.surface,
      padding: const EdgeInsets.fromLTRB(20, 8, 12, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '类型',
            style: TextStyle(
              fontSize: 11,
              color: colors.inkSecondary,
              fontWeight: FontWeight.w700,
              letterSpacing: 1.2,
            ),
          ),
          const SizedBox(height: 6),
          Wrap(
            spacing: 8,
            children: [
              _filterChip(
                label: '全部',
                selected: _filterKind == null,
                onSelected: () {
                  setState(() => _filterKind = null);
                  _reload();
                },
              ),
              for (final kind in EventKind.values)
                _filterChip(
                  label: kind.label,
                  selected: _filterKind == kind,
                  onSelected: () {
                    setState(() => _filterKind = kind);
                    _reload();
                  },
                ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            '状态',
            style: TextStyle(
              fontSize: 11,
              color: colors.inkSecondary,
              fontWeight: FontWeight.w700,
              letterSpacing: 1.2,
            ),
          ),
          const SizedBox(height: 6),
          Wrap(
            spacing: 8,
            children: [
              _filterChip(
                label: '全部',
                selected: _filterStatus == null,
                onSelected: () {
                  setState(() => _filterStatus = null);
                  _reload();
                },
              ),
              for (final status in EventStatus.values)
                _filterChip(
                  label: status.label,
                  selected: _filterStatus == status,
                  onSelected: () {
                    setState(() => _filterStatus = status);
                    _reload();
                  },
                ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _filterChip({
    required String label,
    required bool selected,
    required VoidCallback onSelected,
  }) {
    final colors = context.traceColors;
    return ChoiceChip(
      label: Text(label),
      selected: selected,
      showCheckmark: false,
      onSelected: (_) => onSelected(),
      selectedColor: colors.primarySoft,
      labelStyle: TextStyle(
        color: selected ? colors.primaryStrong : colors.ink,
        fontSize: 12,
        fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
      ),
      side: BorderSide(color: selected ? colors.primary : colors.lineStrong),
    );
  }

  Widget _buildFooter() {
    if (_nextCursor == null) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 24),
        child: Center(
          child: Text(
            '已经到底了。',
            style: TextStyle(
              fontSize: 12,
              color: context.traceColors.inkTertiary,
            ),
          ),
        ),
      );
    }
    if (_error != null) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 16),
        child: Center(
          child: Column(
            children: [
              Text(
                _error!,
                style: TextStyle(
                  color: Theme.of(context).colorScheme.error,
                  fontSize: 12,
                ),
              ),
              const SizedBox(height: 6),
              OutlinedButton(
                onPressed: _loadingMore ? null : _loadMore,
                style: OutlinedButton.styleFrom(
                  shape: const RoundedRectangleBorder(),
                ),
                child: const Text('重试'),
              ),
            ],
          ),
        ),
      );
    }
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 18),
      child: Center(
        child: OutlinedButton(
          onPressed: _loadingMore ? null : _loadMore,
          style: OutlinedButton.styleFrom(
            shape: const RoundedRectangleBorder(),
          ),
          child: Text(_loadingMore ? '加载中…' : '加载更多'),
        ),
      ),
    );
  }
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
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 32),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            title,
            style: TextStyle(
              color: context.traceColors.ink,
              fontSize: 22,
              fontWeight: FontWeight.w700,
            ),
          ),
          const SizedBox(height: 10),
          Text(
            detail,
            textAlign: TextAlign.center,
            style: TextStyle(
              color: context.traceColors.inkSecondary,
              fontSize: 13,
            ),
          ),
          if (onAction != null && actionText != null) ...[
            const SizedBox(height: 16),
            FilledButton(
              onPressed: onAction,
              style: FilledButton.styleFrom(
                shape: const RoundedRectangleBorder(),
                minimumSize: const Size(140, 44),
              ),
              child: Text(actionText!),
            ),
          ],
        ],
      ),
    ),
  );
}
