import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../events/event_model.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_icons.dart';

class EventFilterSelection {
  EventFilterSelection({
    this.fromDate,
    this.toDate,
    this.kind,
    this.status,
    this.categoryKey,
    List<String> tagKeys = const [],
  }) : tagKeys = List.unmodifiable(tagKeys);

  final DateTime? fromDate;
  final DateTime? toDate;
  final EventKind? kind;
  final EventStatus? status;
  final String? categoryKey;
  final List<String> tagKeys;

  bool get hasFilters => activeCount > 0;

  int get activeCount =>
      (fromDate == null ? 0 : 1) +
      (toDate == null ? 0 : 1) +
      (kind == null ? 0 : 1) +
      (status == null ? 0 : 1) +
      (categoryKey == null ? 0 : 1) +
      tagKeys.length;

  String? get fromIso8601 {
    final date = fromDate;
    if (date == null) return null;
    return DateTime(date.year, date.month, date.day).toUtc().toIso8601String();
  }

  String? get toIso8601 {
    final date = toDate;
    if (date == null) return null;
    final nextDay = DateTime(date.year, date.month, date.day + 1);
    return nextDay
        .subtract(const Duration(microseconds: 1))
        .toUtc()
        .toIso8601String();
  }
}

Future<EventFilterSelection?> showEventFilterSheet({
  required BuildContext context,
  required EventFilterSelection selection,
  required EventTaxonomyModel? taxonomy,
}) => showModalBottomSheet<EventFilterSelection>(
  context: context,
  isScrollControlled: true,
  isDismissible: true,
  enableDrag: true,
  useSafeArea: true,
  barrierLabel: '关闭筛选',
  barrierColor: context.traceColors.ink.withValues(alpha: 0.34),
  builder: (sheetContext) {
    final screenHeight = MediaQuery.sizeOf(sheetContext).height;
    return SizedBox(
      height: math.min(screenHeight * 0.74, 640),
      child: EventFilterSheet(selection: selection, taxonomy: taxonomy),
    );
  },
);

class EventFilterSheet extends StatefulWidget {
  const EventFilterSheet({
    super.key,
    required this.selection,
    required this.taxonomy,
  });

  final EventFilterSelection selection;
  final EventTaxonomyModel? taxonomy;

  @override
  State<EventFilterSheet> createState() => _EventFilterSheetState();
}

class _EventFilterSheetState extends State<EventFilterSheet> {
  DateTime? _fromDate;
  DateTime? _toDate;
  EventKind? _kind;
  EventStatus? _status;
  String? _categoryKey;
  late Set<String> _tagKeys;
  late bool _tagsExpanded;

  @override
  void initState() {
    super.initState();
    final selection = widget.selection;
    _fromDate = selection.fromDate;
    _toDate = selection.toDate;
    _kind = selection.kind;
    _status = selection.status;
    _categoryKey = selection.categoryKey;
    _tagKeys = {...selection.tagKeys};
    _tagsExpanded = _tagKeys.isNotEmpty;
  }

  EventFilterSelection get _selection => EventFilterSelection(
    fromDate: _fromDate,
    toDate: _toDate,
    kind: _kind,
    status: _status,
    categoryKey: _categoryKey,
    tagKeys: _tagKeys.toList()..sort(),
  );

  Future<void> _pickDate({required bool isStart}) async {
    final now = DateTime.now();
    final initial = isStart
        ? (_fromDate ?? _toDate ?? now)
        : (_toDate ?? _fromDate ?? now);
    final picked = await showDatePicker(
      context: context,
      initialDate: initial,
      firstDate: DateTime(1900),
      lastDate: DateTime(now.year + 20, 12, 31),
      helpText: isStart ? '选择开始日期' : '选择结束日期',
      cancelText: '取消',
      confirmText: '确定',
    );
    if (picked == null || !mounted) return;
    final date = DateTime(picked.year, picked.month, picked.day);
    setState(() {
      if (isStart) {
        _fromDate = date;
        if (_toDate != null && _toDate!.isBefore(date)) _toDate = date;
      } else {
        _toDate = date;
        if (_fromDate != null && _fromDate!.isAfter(date)) _fromDate = date;
      }
    });
  }

  void _reset() => setState(() {
    _fromDate = null;
    _toDate = null;
    _kind = null;
    _status = null;
    _categoryKey = null;
    _tagKeys.clear();
    _tagsExpanded = false;
  });

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final taxonomy = widget.taxonomy;
    final hasDraftFilters = _selection.hasFilters;
    return Material(
      key: const Key('event-filter-sheet'),
      color: colors.surface,
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 2, 12, 10),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        '筛选记录',
                        style: TextStyle(
                          color: colors.ink,
                          fontSize: 20,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        hasDraftFilters
                            ? '已选择 ${_selection.activeCount} 项条件'
                            : '组合条件，快速找到过去的记录',
                        style: TextStyle(
                          color: colors.inkTertiary,
                          fontSize: 12,
                        ),
                      ),
                    ],
                  ),
                ),
                TextButton(
                  key: const Key('filter-reset'),
                  onPressed: hasDraftFilters ? _reset : null,
                  child: const Text('重置'),
                ),
              ],
            ),
          ),
          Divider(height: 1, color: colors.line),
          Expanded(
            child: SingleChildScrollView(
              padding: const EdgeInsets.fromLTRB(20, 18, 20, 24),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const _FilterSectionTitle('时间区间'),
                  Row(
                    children: [
                      Expanded(
                        child: _DateFilterButton(
                          key: const Key('filter-date-from'),
                          label: '开始日期',
                          value: _fromDate,
                          onTap: () => _pickDate(isStart: true),
                        ),
                      ),
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 8),
                        child: Text(
                          '至',
                          style: TextStyle(color: colors.inkTertiary),
                        ),
                      ),
                      Expanded(
                        child: _DateFilterButton(
                          key: const Key('filter-date-to'),
                          label: '结束日期',
                          value: _toDate,
                          onTap: () => _pickDate(isStart: false),
                        ),
                      ),
                    ],
                  ),
                  if (_fromDate != null || _toDate != null)
                    Align(
                      alignment: Alignment.centerRight,
                      child: TextButton(
                        onPressed: () => setState(() {
                          _fromDate = null;
                          _toDate = null;
                        }),
                        child: const Text('清除时间'),
                      ),
                    ),
                  const SizedBox(height: 18),
                  const _FilterSectionTitle('记录类型'),
                  _FilterChoiceGroup<EventKind>(
                    value: _kind,
                    allLabel: '全部',
                    values: EventKind.values,
                    labelFor: (value) => value.label,
                    onChanged: (value) => setState(() => _kind = value),
                  ),
                  const SizedBox(height: 20),
                  const _FilterSectionTitle('记录状态'),
                  _FilterChoiceGroup<EventStatus>(
                    value: _status,
                    allLabel: '全部',
                    values: EventStatus.values,
                    labelFor: (value) => value.label,
                    onChanged: (value) => setState(() => _status = value),
                  ),
                  const SizedBox(height: 20),
                  const _FilterSectionTitle('主分类'),
                  if (taxonomy == null)
                    _UnavailableTaxonomy(colors: colors)
                  else
                    DropdownButtonFormField<String>(
                      key: ValueKey('filter-category-${_categoryKey ?? 'all'}'),
                      initialValue: _categoryKey ?? '',
                      isExpanded: true,
                      menuMaxHeight: 360,
                      decoration: const InputDecoration(
                        hintText: '全部分类',
                        contentPadding: EdgeInsets.symmetric(
                          horizontal: 14,
                          vertical: 12,
                        ),
                      ),
                      icon: TraceIcon(
                        TraceGlyph.chevronDown,
                        size: 17,
                        color: colors.inkTertiary,
                      ),
                      items: [
                        const DropdownMenuItem(value: '', child: Text('全部分类')),
                        ...taxonomy.categories.map(
                          (item) => DropdownMenuItem(
                            value: item.key,
                            child: Text(item.label),
                          ),
                        ),
                      ],
                      onChanged: (value) => setState(
                        () => _categoryKey = value == null || value.isEmpty
                            ? null
                            : value,
                      ),
                    ),
                  const SizedBox(height: 16),
                  _BehaviorTagDisclosure(
                    expanded: _tagsExpanded,
                    selectedCount: _tagKeys.length,
                    enabled: taxonomy != null,
                    onTap: taxonomy == null
                        ? null
                        : () => setState(() => _tagsExpanded = !_tagsExpanded),
                  ),
                  if (_tagsExpanded && taxonomy != null) ...[
                    const SizedBox(height: 12),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: taxonomy.behaviorTags.map((item) {
                        final selected = _tagKeys.contains(item.key);
                        return FilterChip(
                          key: ValueKey('filter-tag-${item.key}'),
                          label: Text(item.label),
                          selected: selected,
                          showCheckmark: false,
                          materialTapTargetSize: MaterialTapTargetSize.padded,
                          onSelected: (_) => setState(() {
                            if (selected) {
                              _tagKeys.remove(item.key);
                            } else {
                              _tagKeys.add(item.key);
                            }
                          }),
                        );
                      }).toList(),
                    ),
                  ],
                ],
              ),
            ),
          ),
          Divider(height: 1, color: colors.line),
          SafeArea(
            top: false,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(20, 12, 20, 14),
              child: Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      key: const Key('filter-cancel'),
                      onPressed: () => Navigator.of(context).pop(),
                      child: const Text('取消'),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: FilledButton(
                      key: const Key('filter-apply'),
                      onPressed: () => Navigator.of(context).pop(_selection),
                      child: const Text('应用筛选'),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _FilterSectionTitle extends StatelessWidget {
  const _FilterSectionTitle(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 9),
    child: Text(
      text,
      style: TextStyle(
        color: context.traceColors.inkSecondary,
        fontSize: 12,
        fontWeight: FontWeight.w700,
      ),
    ),
  );
}

class _DateFilterButton extends StatelessWidget {
  const _DateFilterButton({
    super.key,
    required this.label,
    required this.value,
    required this.onTap,
  });

  final String label;
  final DateTime? value;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final date = value;
    final display = date == null
        ? '不限'
        : '${date.year}.${date.month.toString().padLeft(2, '0')}.${date.day.toString().padLeft(2, '0')}';
    return Semantics(
      button: true,
      label: '$label，$display',
      child: Material(
        color: colors.surfaceSoft,
        borderRadius: BorderRadius.circular(12),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Container(
            constraints: const BoxConstraints(minHeight: 58),
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
            decoration: BoxDecoration(
              border: Border.all(color: colors.lineStrong),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Row(
              children: [
                TraceIcon(
                  TraceGlyph.calendar,
                  size: 18,
                  color: date == null
                      ? colors.inkTertiary
                      : colors.primaryStrong,
                ),
                const SizedBox(width: 9),
                Expanded(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        label,
                        style: TextStyle(
                          color: colors.inkTertiary,
                          fontSize: 10,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        display,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          color: date == null
                              ? colors.inkSecondary
                              : colors.ink,
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                        ),
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
}

class _FilterChoiceGroup<T> extends StatelessWidget {
  const _FilterChoiceGroup({
    required this.value,
    required this.allLabel,
    required this.values,
    required this.labelFor,
    required this.onChanged,
  });

  final T? value;
  final String allLabel;
  final List<T> values;
  final String Function(T value) labelFor;
  final ValueChanged<T?> onChanged;

  @override
  Widget build(BuildContext context) => Wrap(
    spacing: 8,
    runSpacing: 8,
    children: [
      ChoiceChip(
        label: Text(allLabel),
        selected: value == null,
        showCheckmark: false,
        materialTapTargetSize: MaterialTapTargetSize.padded,
        onSelected: (_) => onChanged(null),
      ),
      ...values.map(
        (item) => ChoiceChip(
          label: Text(labelFor(item)),
          selected: value == item,
          showCheckmark: false,
          materialTapTargetSize: MaterialTapTargetSize.padded,
          onSelected: (_) => onChanged(item),
        ),
      ),
    ],
  );
}

class _BehaviorTagDisclosure extends StatelessWidget {
  const _BehaviorTagDisclosure({
    required this.expanded,
    required this.selectedCount,
    required this.enabled,
    required this.onTap,
  });

  final bool expanded;
  final int selectedCount;
  final bool enabled;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Semantics(
      button: true,
      enabled: enabled,
      expanded: expanded,
      label: '行为标签',
      child: Material(
        key: const Key('filter-tags-toggle'),
        color: colors.surfaceSoft,
        borderRadius: BorderRadius.circular(12),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Container(
            constraints: const BoxConstraints(minHeight: 58),
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
            decoration: BoxDecoration(
              border: Border.all(color: colors.line),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Text(
                        '行为标签',
                        style: TextStyle(
                          color: enabled ? colors.ink : colors.inkTertiary,
                          fontSize: 13,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        !enabled
                            ? '分类数据暂不可用'
                            : selectedCount == 0
                            ? '可多选，按需展开'
                            : '已选择 $selectedCount 个',
                        style: TextStyle(
                          color: colors.inkTertiary,
                          fontSize: 11,
                        ),
                      ),
                    ],
                  ),
                ),
                TraceIcon(
                  expanded ? TraceGlyph.chevronUp : TraceGlyph.chevronDown,
                  size: 18,
                  color: colors.inkTertiary,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _UnavailableTaxonomy extends StatelessWidget {
  const _UnavailableTaxonomy({required this.colors});

  final PassingTraceThemeColors colors;

  @override
  Widget build(BuildContext context) => Container(
    width: double.infinity,
    constraints: const BoxConstraints(minHeight: 52),
    alignment: Alignment.centerLeft,
    padding: const EdgeInsets.symmetric(horizontal: 14),
    decoration: BoxDecoration(
      color: colors.surfaceSoft,
      border: Border.all(color: colors.line),
      borderRadius: BorderRadius.circular(12),
    ),
    child: Text(
      '分类数据暂不可用，可继续使用其他筛选',
      style: TextStyle(color: colors.inkTertiary, fontSize: 12),
    ),
  );
}
