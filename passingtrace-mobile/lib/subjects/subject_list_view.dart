import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../events/media_api.dart';
import '../theme/passingtrace_theme.dart';
import 'subject_avatar.dart';
import 'subject_model.dart';

class SubjectListView extends StatelessWidget {
  const SubjectListView({
    super.key,
    required this.graph,
    required this.matches,
    required this.kept,
    required this.nickname,
    required this.onOpen,
    this.avatar,
    this.coverLoader,
  });
  final SubjectGraph graph;
  final Set<String> matches, kept;
  final String nickname;
  final Uint8List? avatar;
  final ValueChanged<String> onOpen;
  final Future<MediaAccessTarget> Function(String)? coverLoader;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final visible = graph.nodes.where((s) => matches.contains(s.id));
    final profiles = [
      ...visible.where((s) => s.isSelf),
      ...visible.where((s) => !s.isSelf),
    ];
    return ListView.builder(
      key: PageStorageKey('subjects-list-${graph.rootId}'),
      padding: const EdgeInsets.fromLTRB(16, 20, 16, 100),
      itemCount: profiles.length + 2,
      itemBuilder: (context, index) {
        if (index == 0) {
          return Padding(
            padding: const EdgeInsets.only(bottom: 16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    const Expanded(
                      child: Text(
                        '档案列表',
                        style: TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ),
                    Text(
                      '${profiles.length} 份档案',
                      style: TextStyle(color: colors.inkSecondary),
                    ),
                  ],
                ),
                if (profiles.isEmpty)
                  const Padding(
                    padding: EdgeInsets.only(top: 24),
                    child: Text('没有找到匹配的档案'),
                  ),
              ],
            ),
          );
        }
        if (index == profiles.length + 1) {
          return Padding(
            padding: const EdgeInsets.only(top: 12),
            child: _RelationshipOverview(
              graph: graph,
              kept: kept,
              nickname: nickname,
              onOpen: onOpen,
            ),
          );
        }
        final subject = profiles[index - 1];
        return Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: Card(
            key: ValueKey('subject-list-card-${subject.id}'),
            color: colors.surface,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(20),
              side: BorderSide(
                color: subject.isSelf
                    ? colors.primary.withValues(alpha: .4)
                    : colors.line,
              ),
            ),
            clipBehavior: Clip.antiAlias,
            child: ListTile(
              contentPadding: const EdgeInsets.symmetric(
                horizontal: 16,
                vertical: 14,
              ),
              leading: SubjectAvatar(
                subject: subject,
                accountAvatar: avatar,
                cover: subject.isSelf || subject.coverMediaId == null
                    ? null
                    : coverLoader?.call(subject.coverMediaId!),
              ),
              title: Text(
                subject.isSelf ? '$nickname（自己）' : subject.name,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontWeight: FontWeight.w700),
              ),
              subtitle: Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Wrap(
                      spacing: 8,
                      runSpacing: 6,
                      children: [
                        _Tag(
                          label: subjectTypeLabel(subject),
                          background: colors.primarySoft,
                          foreground: colors.primaryStrong,
                        ),
                        _Tag(
                          label: subject.state == 1 ? '已结束' : '进行中',
                          background: colors.canvas,
                          foreground: colors.inkSecondary,
                        ),
                      ],
                    ),
                    if (subject.description?.trim().isNotEmpty == true)
                      Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: Text(
                          subject.description!,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                  ],
                ),
              ),
              trailing: Icon(Icons.chevron_right, color: colors.inkTertiary),
              onTap: () => onOpen(subject.id),
            ),
          ),
        );
      },
    );
  }
}

class _Tag extends StatelessWidget {
  const _Tag({
    required this.label,
    required this.background,
    required this.foreground,
  });
  final String label;
  final Color background, foreground;
  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
    decoration: BoxDecoration(
      color: background,
      borderRadius: BorderRadius.circular(16),
    ),
    child: Text(label, style: TextStyle(fontSize: 12, color: foreground)),
  );
}

class _RelationshipOverview extends StatefulWidget {
  const _RelationshipOverview({
    required this.graph,
    required this.kept,
    required this.nickname,
    required this.onOpen,
  });
  final SubjectGraph graph;
  final Set<String> kept;
  final String nickname;
  final ValueChanged<String> onOpen;
  @override
  State<_RelationshipOverview> createState() => _RelationshipOverviewState();
}

class _RelationshipOverviewState extends State<_RelationshipOverview> {
  final _query = TextEditingController();
  String _status = '';
  @override
  void dispose() {
    _query.dispose();
    super.dispose();
  }

  String _name(String id) {
    final subject = widget.graph.nodes.firstWhere((s) => s.id == id);
    return subject.isSelf ? '${widget.nickname}（自己）' : subject.name;
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final relations = widget.graph.relations
        .where(
          (r) => widget.kept.contains(r.from) && widget.kept.contains(r.to),
        )
        .toList();
    final historical = relations.where((r) => r.endedAt != null).length;
    final query = _query.text.trim().toLowerCase();
    final visible = relations
        .where(
          (r) =>
              '${_name(r.from)} ${_name(r.to)} ${r.label}'
                  .toLowerCase()
                  .contains(query) &&
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
        key: PageStorageKey('subjects-relationships-${widget.graph.rootId}'),
        tilePadding: const EdgeInsets.symmetric(horizontal: 18, vertical: 10),
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
        children: [
          Row(
            children: [
              Expanded(
                child: TextField(
                  key: PageStorageKey(
                    'subjects-relationships-search-${widget.graph.rootId}',
                  ),
                  controller: _query,
                  decoration: const InputDecoration(
                    hintText: '搜索档案或关系',
                    prefixIcon: Icon(Icons.search),
                  ),
                  onChanged: (_) => setState(() {}),
                ),
              ),
              PopupMenuButton<String>(
                tooltip: '筛选关系清单',
                initialValue: _status,
                icon: Icon(
                  _status.isEmpty ? Icons.filter_list : Icons.filter_alt,
                  color: _status.isEmpty ? colors.inkSecondary : colors.primary,
                ),
                onSelected: (value) => setState(() => _status = value),
                itemBuilder: (_) => [
                  CheckedPopupMenuItem(
                    value: '',
                    checked: _status.isEmpty,
                    child: const Text('全部关系'),
                  ),
                  CheckedPopupMenuItem(
                    value: 'current',
                    checked: _status == 'current',
                    child: const Text('当前关系'),
                  ),
                  CheckedPopupMenuItem(
                    value: 'history',
                    checked: _status == 'history',
                    child: const Text('历史关系'),
                  ),
                ],
              ),
            ],
          ),
          if (_status.isNotEmpty)
            Align(
              alignment: Alignment.centerLeft,
              child: InputChip(
                label: Text(_status == 'history' ? '历史关系' : '当前关系'),
                onDeleted: () => setState(() => _status = ''),
              ),
            ),
          const SizedBox(height: 12),
          if (visible.isEmpty)
            Padding(
              padding: const EdgeInsets.all(16),
              child: Text(relations.isEmpty ? '暂无人物关系' : '暂无匹配的关系'),
            )
          else
            ConstrainedBox(
              key: const Key('subjects-relationships-list'),
              constraints: BoxConstraints(
                maxHeight: math.min(
                  MediaQuery.sizeOf(context).height * .4,
                  340,
                ),
              ),
              child: ListView.separated(
                key: PageStorageKey(
                  'subjects-relationships-scroll-${widget.graph.rootId}',
                ),
                primary: false,
                shrinkWrap: true,
                keyboardDismissBehavior:
                    ScrollViewKeyboardDismissBehavior.onDrag,
                itemCount: visible.length,
                separatorBuilder: (_, index) => const SizedBox(height: 10),
                itemBuilder: (context, index) {
                  final relation = visible[index];
                  return Container(
                    key: ValueKey('subject-overview-relation-${relation.id}'),
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(
                      color: colors.canvas,
                      borderRadius: BorderRadius.circular(14),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Wrap(
                          spacing: 8,
                          runSpacing: 4,
                          children: [
                            Text(
                              relation.label,
                              style: const TextStyle(
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            if (relation.endedAt != null)
                              Text(
                                '历史关系',
                                style: TextStyle(color: colors.inkTertiary),
                              ),
                          ],
                        ),
                        const SizedBox(height: 4),
                        Row(
                          children: [
                            Expanded(child: _endpoint(relation.from)),
                            Padding(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 6,
                              ),
                              child: Icon(
                                relation.directed
                                    ? Icons.arrow_forward
                                    : Icons.swap_horiz,
                                size: 18,
                                color: colors.inkTertiary,
                              ),
                            ),
                            Expanded(child: _endpoint(relation.to)),
                          ],
                        ),
                      ],
                    ),
                  );
                },
              ),
            ),
        ],
      ),
    );
  }

  Widget _endpoint(String id) => TextButton(
    style: TextButton.styleFrom(
      alignment: Alignment.centerLeft,
      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 8),
    ),
    onPressed: () => widget.onOpen(id),
    child: Text(_name(id), maxLines: 2, overflow: TextOverflow.ellipsis),
  );
}
