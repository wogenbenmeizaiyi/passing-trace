import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../theme/passingtrace_theme.dart';
import 'subject_model.dart';

class SubjectRelationsSection extends StatefulWidget {
  const SubjectRelationsSection({
    super.key,
    required this.subjectId,
    required this.graph,
    required this.busy,
    required this.onAdd,
    required this.onEdit,
    required this.onRemove,
  });
  final String subjectId;
  final SubjectGraph graph;
  final bool busy;
  final VoidCallback onAdd;
  final ValueChanged<SubjectRelation> onEdit, onRemove;

  @override
  State<SubjectRelationsSection> createState() =>
      _SubjectRelationsSectionState();
}

class _SubjectRelationsSectionState extends State<SubjectRelationsSection> {
  final _query = TextEditingController();
  final _expansion = ExpansibleController();
  String _status = '';

  @override
  void dispose() {
    _query.dispose();
    _expansion.dispose();
    super.dispose();
  }

  String _name(SubjectRelation relation) {
    final id = relation.from == widget.subjectId ? relation.to : relation.from;
    final subject = widget.graph.nodes.firstWhere((s) => s.id == id);
    return subject.isSelf ? '自己' : subject.name;
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final relations = widget.graph.relations
        .where((r) => r.from == widget.subjectId || r.to == widget.subjectId)
        .toList();
    final historical = relations.where((r) => r.endedAt != null).length;
    final query = _query.text.trim().toLowerCase();
    final visible = relations
        .where(
          (r) =>
              '${_name(r)} ${r.label}'.toLowerCase().contains(query) &&
              (_status.isEmpty ||
                  (_status == 'history'
                      ? r.endedAt != null
                      : r.endedAt == null)),
        )
        .toList();
    return Material(
      color: colors.surface,
      clipBehavior: Clip.antiAlias,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(20),
        side: BorderSide(color: colors.line),
      ),
      child: ExpansionTile(
        key: PageStorageKey('subject-relations-${widget.subjectId}'),
        controller: _expansion,
        tilePadding: const EdgeInsets.symmetric(horizontal: 18, vertical: 8),
        childrenPadding: const EdgeInsets.fromLTRB(14, 0, 14, 16),
        shape: const Border(),
        collapsedShape: const Border(),
        title: const Text(
          '人物关系',
          style: TextStyle(fontWeight: FontWeight.w700),
        ),
        subtitle: Text(
          '${relations.length} 条关系${historical == 0 ? '' : ' · $historical 条历史关系'}',
        ),
        trailing: ListenableBuilder(
          listenable: _expansion,
          builder: (context, child) => Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (widget.graph.nodes.length > 1)
                SizedBox.square(
                  dimension: 48,
                  child: IconButton(
                    tooltip: '新增关系',
                    onPressed: widget.busy ? null : widget.onAdd,
                    icon: const Icon(Icons.add),
                  ),
                ),
              SizedBox.square(
                dimension: 48,
                child: Semantics(
                  expanded: _expansion.isExpanded,
                  child: IconButton(
                    tooltip: _expansion.isExpanded ? '收起人物关系' : '展开人物关系',
                    onPressed: () => _expansion.isExpanded
                        ? _expansion.collapse()
                        : _expansion.expand(),
                    icon: AnimatedRotation(
                      turns: _expansion.isExpanded ? .5 : 0,
                      duration: const Duration(milliseconds: 180),
                      child: const Icon(Icons.expand_more),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
        children: [
          if (relations.isNotEmpty) ...[
            Row(
              children: [
                Expanded(
                  child: TextField(
                    key: PageStorageKey(
                      'subject-relations-search-${widget.subjectId}',
                    ),
                    controller: _query,
                    decoration: const InputDecoration(
                      hintText: '搜索名称或关系',
                      prefixIcon: Icon(Icons.search),
                    ),
                    onChanged: (_) => setState(() {}),
                  ),
                ),
                PopupMenuButton<String>(
                  tooltip: '筛选关系',
                  initialValue: _status,
                  onSelected: (value) => setState(() => _status = value),
                  icon: Icon(
                    _status.isEmpty ? Icons.filter_list : Icons.filter_alt,
                    color: _status.isEmpty
                        ? colors.inkSecondary
                        : colors.primary,
                  ),
                  itemBuilder: (_) => const [
                    PopupMenuItem(value: '', child: Text('全部关系')),
                    PopupMenuItem(value: 'current', child: Text('当前关系')),
                    PopupMenuItem(value: 'history', child: Text('历史关系')),
                  ],
                ),
              ],
            ),
            const SizedBox(height: 12),
            if (_status.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Align(
                  alignment: Alignment.centerLeft,
                  child: InputChip(
                    label: Text(_status == 'history' ? '历史关系' : '当前关系'),
                    onDeleted: () => setState(() => _status = ''),
                  ),
                ),
              ),
          ],
          if (visible.isEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 16),
              child: Text(relations.isEmpty ? '暂无人物关系' : '暂无匹配的关系'),
            )
          else
            ConstrainedBox(
              key: const Key('subject-relations-list'),
              constraints: BoxConstraints(
                maxHeight: math.min(
                  MediaQuery.sizeOf(context).height * .4,
                  340,
                ),
              ),
              child: ListView.separated(
                key: PageStorageKey(
                  'subject-relations-scroll-${widget.subjectId}',
                ),
                primary: false,
                shrinkWrap: true,
                keyboardDismissBehavior:
                    ScrollViewKeyboardDismissBehavior.onDrag,
                itemCount: visible.length,
                separatorBuilder: (_, index) => const SizedBox(height: 8),
                itemBuilder: (_, index) {
                  final relation = visible[index];
                  return Material(
                    color: colors.canvas,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(14),
                    ),
                    child: ListTile(
                      title: Text(
                        '${relation.label}${relation.directed ? (relation.from == widget.subjectId ? ' →' : ' ←') : ' —'} ${_name(relation)}',
                      ),
                      subtitle: relation.endedAt == null
                          ? null
                          : const Text('历史关系，仍参与图连接'),
                      onTap: widget.busy ? null : () => widget.onEdit(relation),
                      trailing: IconButton(
                        tooltip: '申请移除误关联',
                        onPressed: widget.busy
                            ? null
                            : () => widget.onRemove(relation),
                        icon: const Icon(Icons.link_off),
                      ),
                    ),
                  );
                },
              ),
            ),
        ],
      ),
    );
  }
}
