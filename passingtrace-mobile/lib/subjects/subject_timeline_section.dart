import 'package:flutter/material.dart';

import '../events/event_datetime.dart';
import '../theme/passingtrace_theme.dart';
import 'subject_timeline_filter_sheet.dart';

class SubjectTimelineSection extends StatefulWidget {
  const SubjectTimelineSection({
    super.key,
    required this.groups,
    required this.filter,
    required this.busy,
    required this.loading,
    required this.hasMore,
    required this.onFilter,
    required this.onMore,
    required this.onOpen,
  });
  final List<Map<String, dynamic>> groups;
  final SubjectTimelineFilter filter;
  final bool busy, loading, hasMore;
  final VoidCallback onFilter, onMore;
  final ValueChanged<Map> onOpen;

  @override
  State<SubjectTimelineSection> createState() => _SubjectTimelineSectionState();
}

class _SubjectTimelineSectionState extends State<SubjectTimelineSection> {
  final _expanded = <String, bool>{};
  bool? _expandNewGroups;

  String _id(Map<String, dynamic> group) =>
      '${widget.filter.groupBy}/${group['key']}';

  @override
  Widget build(BuildContext context) {
    for (var i = 0; i < widget.groups.length; i++) {
      _expanded.putIfAbsent(
        _id(widget.groups[i]),
        () => _expandNewGroups ?? i == 0,
      );
    }
    final allCollapsed = widget.groups.every((g) => !_expanded[_id(g)]!);
    return SliverMainAxisGroup(
      slivers: [
        PinnedHeaderSliver(
          child: Material(
            color: context.traceColors.canvas,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      const Expanded(
                        child: Text(
                          '时间轴',
                          style: TextStyle(
                            fontWeight: FontWeight.w700,
                            fontSize: 18,
                          ),
                        ),
                      ),
                      TextButton.icon(
                        key: const Key('subject-timeline-filter-button'),
                        onPressed: widget.busy ? null : widget.onFilter,
                        icon: const Icon(Icons.filter_list),
                        label: Text(
                          widget.filter.activeCount == 0
                              ? '筛选'
                              : '筛选 (${widget.filter.activeCount})',
                        ),
                      ),
                    ],
                  ),
                  Row(
                    children: [
                      Expanded(child: Text(widget.filter.summary)),
                      if (widget.groups.isNotEmpty)
                        TextButton(
                          key: const Key('subject-timeline-toggle-all'),
                          onPressed: () => setState(() {
                            _expandNewGroups = allCollapsed;
                            for (final group in widget.groups) {
                              _expanded[_id(group)] = allCollapsed;
                            }
                          }),
                          child: Text(allCollapsed ? '全部展开' : '全部收起'),
                        ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
        SliverPadding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          sliver: SliverList.list(
            children: [
              for (final group in widget.groups) ...[
                _groupHeader(group),
                if (_expanded[_id(group)]!)
                  for (final raw in group['items'] as List)
                    _TimelineItem(
                      key: ValueKey(
                        '${_id(group)}/${raw['sourceType']}/${raw['sourceId']}',
                      ),
                      item: raw as Map,
                      onOpen: widget.onOpen,
                    ),
              ],
            ],
          ),
        ),
        SliverToBoxAdapter(
          child: Padding(
            padding: EdgeInsets.fromLTRB(
              16,
              8,
              16,
              88 + MediaQuery.viewInsetsOf(context).bottom,
            ),
            child: Column(
              children: [
                if (widget.loading) const CircularProgressIndicator(),
                if (!widget.loading && widget.groups.isEmpty)
                  const Text('暂无符合条件的内容'),
                if (widget.hasMore)
                  TextButton(
                    onPressed: widget.loading ? null : widget.onMore,
                    child: const Text('加载更多'),
                  ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Widget _groupHeader(Map<String, dynamic> group) {
    final id = _id(group);
    final expanded = _expanded[id]!;
    final label = group['key'] as String;
    final count = (group['items'] as List).length;
    return Padding(
      padding: const EdgeInsets.only(top: 8, bottom: 10),
      child: Semantics(
        button: true,
        expanded: expanded,
        child: InkWell(
          key: ValueKey('subject-timeline-group-$id'),
          borderRadius: BorderRadius.circular(12),
          onTap: () => setState(() => _expanded[id] = !expanded),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    label,
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                ),
                Text('$count 条'),
                const SizedBox(width: 12),
                AnimatedRotation(
                  turns: expanded ? .5 : 0,
                  duration: const Duration(milliseconds: 180),
                  child: const Icon(Icons.expand_more),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _TimelineItem extends StatelessWidget {
  const _TimelineItem({super.key, required this.item, required this.onOpen});
  final Map item;
  final ValueChanged<Map> onOpen;

  @override
  Widget build(BuildContext context) {
    final source = item['sourceType'];
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Card(
        child: ListTile(
          title: Text(item['title'] as String),
          subtitle: Text(
            '${source == 'Event'
                ? '原记录'
                : source == 'SubjectEntry'
                ? '来自人物'
                : '生命周期'} · ${item['kind'] == 'Plan' ? '计划' : '记录'} · ${{'Planned': '待执行', 'Completed': '已完成', 'Cancelled': '已取消', 'Corrected': '已纠正'}[item['state']] ?? item['state']}\n${formatLocal(DateTime.tryParse(item['occurredAt'] as String? ?? ''))}${item['isReference'] == true ? ' · 引用 ${item['originSubjectName'] ?? '原记录'}' : ''}${item['afterEnd'] == true ? ' · 结束期间内容' : ''}',
          ),
          isThreeLine: true,
          onTap: () => onOpen(item),
        ),
      ),
    );
  }
}
