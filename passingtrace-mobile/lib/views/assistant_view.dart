import 'package:flutter/material.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';
import 'package:url_launcher/url_launcher.dart';

import '../auth_service.dart';
import '../events/ai_api.dart';
import '../events/events_api.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';
import '../user_facing_error.dart';
import 'event_detail_view.dart';
import 'storyline_detail_view.dart';

class AssistantView extends StatefulWidget {
  const AssistantView({
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
  State<AssistantView> createState() => _AssistantViewState();
}

class AssistantErrorNotice extends StatelessWidget {
  const AssistantErrorNotice({
    super.key,
    required this.message,
    required this.onDismiss,
  });

  final String message;
  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Semantics(
      liveRegion: true,
      container: true,
      label: '回答失败：$message',
      child: Material(
        color: colors.surface,
        shape: RoundedRectangleBorder(
          side: BorderSide(color: colors.danger),
          borderRadius: BorderRadius.circular(14),
        ),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(14, 10, 4, 10),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '这次没有完成',
                      style: TextStyle(
                        color: colors.ink,
                        fontSize: 13,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      message,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: colors.inkSecondary,
                        fontSize: 12,
                        height: 1.4,
                      ),
                    ),
                  ],
                ),
              ),
              SizedBox.square(
                dimension: 48,
                child: TraceIconButton(
                  glyph: TraceGlyph.close,
                  tooltip: '关闭提示',
                  onPressed: onDismiss,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _AssistantViewState extends State<AssistantView> {
  late AiApiClient _api;
  final _input = TextEditingController();
  final List<_ChatBubble> _messages = [];
  List<AiConversationModel> _conversations = const [];
  String? _conversationId;
  bool _initialLoading = true;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _initialize();
  }

  Future<void> _initialize() async {
    final baseUrl = await widget.auth.getEventsApiBaseUrl();
    _api = AiApiClient(auth: widget.auth, baseUrl: baseUrl);
    try {
      var conversations = await _api.listConversations(widget.session);
      final conversation = conversations.isEmpty
          ? await _api.createConversation(widget.session)
          : conversations.first;
      if (conversations.isEmpty) conversations = [conversation];
      final detail = await _api.getConversation(
        widget.session,
        conversation.id,
      );
      if (mounted) {
        setState(() {
          _conversationId = conversation.id;
          _conversations = conversations;
          _messages
            ..clear()
            ..addAll(
              detail.messages.map(
                (message) => _ChatBubble(
                  role: message.role,
                  text: message.content,
                  evidenceRecords: message.evidenceRecords,
                  evidenceStorylines: message.evidenceStorylines,
                  amapPlaces: message.amapPlaces,
                  actions: message.actions,
                ),
              ),
            );
          _initialLoading = false;
        });
      }
    } catch (error) {
      _handleError(error);
      if (mounted) setState(() => _initialLoading = false);
    }
  }

  @override
  void dispose() {
    _api.close();
    _input.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    final question = _input.text.trim();
    final conversationId = _conversationId;
    if (question.isEmpty || conversationId == null || _busy) return;
    _input.clear();
    final answer = _ChatBubble(role: 'assistant', text: '');
    setState(() {
      _busy = true;
      _error = null;
      _messages.add(_ChatBubble(role: 'user', text: question));
      _messages.add(answer);
    });
    try {
      await for (final chunk in _api.send(
        widget.session,
        conversationId,
        question,
      )) {
        if (!mounted) return;
        if (chunk.type == 'delta') {
          final raw = chunk.data as Map<String, dynamic>;
          setState(() {
            if (raw['replacement'] == true) answer.text = '';
            answer.text += raw['text'] as String? ?? '';
          });
        } else if (chunk.type == 'evidence') {
          final raw = chunk.data as Map<String, dynamic>;
          final records = AiEvidenceRecord.fromEnvelope(raw);
          final storylines = AiEvidenceStoryline.fromEnvelope(raw);
          final places = AmapPlaceModel.fromEnvelope(raw);
          final actions = AssistantActionModel.fromEnvelope(raw);
          setState(() {
            answer.eventTitles = {
              for (final record in records) record.eventId: record.displayTitle,
            };
            answer.storylineTitles = {
              for (final storyline in storylines)
                storyline.storylineId.toLowerCase(): storyline.displayTitle,
            };
            answer.amapPlaces = places;
            answer.actions = actions;
          });
        } else if (chunk.type == 'action') {
          final action = AssistantActionModel.fromJson(
            chunk.data as Map<String, dynamic>,
          );
          if (action.isSafe) {
            setState(() {
              if (!answer.actions.any(
                (existing) =>
                    existing.type == action.type &&
                    existing.label == action.label,
              )) {
                answer.actions = [...answer.actions, action];
              }
            });
          }
        } else if (chunk.type == 'error') {
          final raw = chunk.data as Map<String, dynamic>;
          throw EventApiException(
            status: 503,
            message: raw['message'] as String? ?? '暂时没能完成这次回答，请稍后重试。',
          );
        }
      }
    } catch (error) {
      _handleError(error);
    } finally {
      if (mounted) {
        setState(() => _busy = false);
        await _reloadConversations();
      }
    }
  }

  Future<void> _reloadConversations() async {
    try {
      final conversations = await _api.listConversations(widget.session);
      if (mounted) setState(() => _conversations = conversations);
    } catch (error) {
      _handleError(error);
    }
  }

  Future<void> _newConversation() async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final conversation = await _api.createConversation(widget.session);
      if (!mounted) return;
      setState(() {
        _conversationId = conversation.id;
        _messages.clear();
        _conversations = [conversation, ..._conversations];
      });
    } catch (error) {
      _handleError(error);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _openConversation(AiConversationModel conversation) async {
    Navigator.of(context).pop();
    setState(() {
      _initialLoading = true;
      _error = null;
    });
    try {
      final detail = await _api.getConversation(
        widget.session,
        conversation.id,
      );
      if (!mounted) return;
      setState(() {
        _conversationId = conversation.id;
        _messages
          ..clear()
          ..addAll(
            detail.messages.map(
              (message) => _ChatBubble(
                role: message.role,
                text: message.content,
                evidenceRecords: message.evidenceRecords,
                evidenceStorylines: message.evidenceStorylines,
                amapPlaces: message.amapPlaces,
                actions: message.actions,
              ),
            ),
          );
      });
    } catch (error) {
      _handleError(error);
    } finally {
      if (mounted) setState(() => _initialLoading = false);
    }
  }

  Future<void> _deleteConversation(AiConversationModel conversation) async {
    await _api.deleteConversation(widget.session, conversation.id);
    if (!mounted) return;
    var conversations = _conversations
        .where((item) => item.id != conversation.id)
        .toList(growable: false);
    if (conversation.id == _conversationId) {
      final next = conversations.isEmpty
          ? await _api.createConversation(widget.session)
          : conversations.first;
      if (conversations.isEmpty) conversations = [next];
      final detail = await _api.getConversation(widget.session, next.id);
      if (!mounted) return;
      setState(() {
        _conversationId = next.id;
        _messages
          ..clear()
          ..addAll(
            detail.messages.map(
              (message) => _ChatBubble(
                role: message.role,
                text: message.content,
                evidenceRecords: message.evidenceRecords,
                evidenceStorylines: message.evidenceStorylines,
                amapPlaces: message.amapPlaces,
                actions: message.actions,
              ),
            ),
          );
      });
    }
    if (mounted) setState(() => _conversations = conversations);
  }

  Future<void> _showConversations() async {
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      backgroundColor: context.traceColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
      ),
      builder: (sheetContext) {
        final colors = sheetContext.traceColors;
        return SafeArea(
          child: SizedBox(
            height: MediaQuery.sizeOf(context).height * 0.52,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(20, 4, 20, 14),
                  child: Row(
                    children: [
                      Text(
                        '聊天记录',
                        style: TextStyle(
                          color: colors.ink,
                          fontSize: 20,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const Spacer(),
                      Text(
                        '按月份整理',
                        style: TextStyle(color: colors.inkMuted, fontSize: 12),
                      ),
                    ],
                  ),
                ),
                Divider(height: 1, color: colors.line),
                Expanded(
                  child: _conversations.isEmpty
                      ? Center(
                          child: Text(
                            '还没有聊天记录',
                            style: TextStyle(color: colors.inkSecondary),
                          ),
                        )
                      : AssistantConversationHistory(
                          conversations: _conversations,
                          selectedConversationId: _conversationId,
                          onOpen: _openConversation,
                          onDelete: (conversation) async {
                            Navigator.of(sheetContext).pop();
                            await _deleteConversation(conversation);
                          },
                        ),
                ),
              ],
            ),
          ),
        );
      },
    );
  }

  void _handleError(Object error) {
    if (!mounted) return;
    if (error is EventApiException && error.status == 401) {
      final handler = widget.onSessionExpired;
      if (handler != null) {
        handler();
      } else {
        Navigator.of(context).pop(true);
      }
      return;
    }
    setState(
      () =>
          _error = userFacingErrorMessage(error, fallback: '暂时没能完成这次回答，请稍后重试。'),
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    drawer: widget.drawer,
    bottomNavigationBar: widget.bottomNavigationBar,
    appBar: TraceAppBar(
      title: '问问记录',
      leading: Builder(
        builder: (menuContext) => TraceIconButton(
          glyph: TraceGlyph.menu,
          tooltip: '打开菜单',
          onPressed: () => Scaffold.of(menuContext).openDrawer(),
        ),
      ),
      trailingWidth: 96,
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          SizedBox.square(
            dimension: 48,
            child: TraceIconButton(
              glyph: TraceGlyph.newChat,
              tooltip: '新建会话',
              onPressed: _busy ? null : _newConversation,
            ),
          ),
          SizedBox.square(
            dimension: 48,
            child: TraceIconButton(
              glyph: TraceGlyph.history,
              tooltip: '聊天记录',
              onPressed: _showConversations,
            ),
          ),
        ],
      ),
    ),
    body: _buildChat(),
  );

  Widget _buildChat() {
    final colors = context.traceColors;
    return Stack(
      children: [
        Positioned.fill(
          child: _initialLoading
              ? Center(child: CircularProgressIndicator(color: colors.primary))
              : _messages.isEmpty
              ? _buildEmptyChat()
              : ListView.builder(
                  padding: const EdgeInsets.fromLTRB(16, 20, 16, 112),
                  itemCount: _messages.length,
                  itemBuilder: (_, index) => _buildMessage(_messages[index]),
                ),
        ),
        if (_error != null)
          Positioned(
            left: 16,
            right: 16,
            bottom: 88,
            child: AssistantErrorNotice(
              message: _error!,
              onDismiss: () => setState(() => _error = null),
            ),
          ),
        Positioned(left: 10, right: 10, bottom: 10, child: _buildComposer()),
      ],
    );
  }

  Widget _buildMessage(_ChatBubble message) {
    final colors = context.traceColors;
    final mine = message.role == 'user';
    return Align(
      alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
      child: Container(
        constraints: BoxConstraints(
          maxWidth: MediaQuery.sizeOf(context).width * 0.88,
        ),
        margin: const EdgeInsets.only(bottom: 12),
        padding: EdgeInsets.symmetric(
          horizontal: mine ? 14 : 15,
          vertical: mine ? 11 : 15,
        ),
        decoration: BoxDecoration(
          color: mine ? colors.primary : colors.surface,
          borderRadius: BorderRadius.only(
            topLeft: Radius.circular(mine ? 18 : 5),
            topRight: const Radius.circular(18),
            bottomLeft: const Radius.circular(18),
            bottomRight: Radius.circular(mine ? 5 : 18),
          ),
          border: mine ? null : Border.all(color: colors.line),
          boxShadow: mine
              ? null
              : [
                  BoxShadow(
                    color: colors.ink.withValues(alpha: 0.05),
                    blurRadius: 14,
                    offset: const Offset(0, 5),
                  ),
                ],
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            AssistantMessageContent(
              text: message.text,
              isUser: mine,
              eventTitles: message.eventTitles,
              storylineTitles: message.storylineTitles,
              onOpenEvent: _openEvent,
              onOpenStoryline: _openStoryline,
            ),
            if (message.eventTitles.isNotEmpty)
              AssistantEvidenceDisclosure(
                records: message.eventTitles,
                onOpenEvent: _openEvent,
              ),
            if (message.amapPlaces.isNotEmpty || message.actions.isNotEmpty)
              AmapActionCards(
                places: message.amapPlaces,
                actions: message.actions,
              ),
          ],
        ),
      ),
    );
  }

  void _openEvent(int id) {
    Navigator.push(
      context,
      MaterialPageRoute(
        builder: (_) => EventDetailView(
          auth: widget.auth,
          session: widget.session,
          eventId: id,
        ),
      ),
    );
  }

  void _openStoryline(String id) {
    Navigator.push(
      context,
      MaterialPageRoute(
        builder: (_) => StorylineDetailView(
          auth: widget.auth,
          session: widget.session,
          storylineId: id,
        ),
      ),
    );
  }

  Widget _buildComposer() {
    final colors = context.traceColors;
    final enabled = !_busy && _conversationId != null;
    return SafeArea(
      top: false,
      child: Material(
        color: colors.surface,
        elevation: 0,
        borderRadius: BorderRadius.circular(18),
        child: Container(
          constraints: const BoxConstraints(minHeight: 60),
          padding: const EdgeInsets.fromLTRB(14, 6, 6, 6),
          decoration: BoxDecoration(
            border: Border.all(color: colors.lineStrong),
            borderRadius: BorderRadius.circular(18),
            boxShadow: [
              BoxShadow(
                color: colors.ink.withValues(alpha: 0.10),
                blurRadius: 22,
                offset: const Offset(0, 8),
              ),
            ],
          ),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(
                child: TextField(
                  controller: _input,
                  minLines: 1,
                  maxLines: 4,
                  textInputAction: TextInputAction.newline,
                  decoration: InputDecoration(
                    hintText: '询问自己的记录…',
                    hintStyle: TextStyle(color: colors.inkMuted),
                    border: InputBorder.none,
                    enabledBorder: InputBorder.none,
                    focusedBorder: InputBorder.none,
                    filled: false,
                    contentPadding: const EdgeInsets.symmetric(vertical: 13),
                  ),
                ),
              ),
              const SizedBox(width: 8),
              Semantics(
                button: true,
                label: '发送',
                child: Material(
                  color: enabled ? colors.primary : colors.lineStrong,
                  borderRadius: BorderRadius.circular(14),
                  child: InkWell(
                    onTap: enabled ? _send : null,
                    borderRadius: BorderRadius.circular(14),
                    child: SizedBox.square(
                      dimension: 48,
                      child: Center(
                        child: _busy
                            ? SizedBox.square(
                                dimension: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  color: colors.onPrimary,
                                ),
                              )
                            : TraceIcon(
                                TraceGlyph.send,
                                size: 21,
                                color: colors.onPrimary,
                              ),
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildEmptyChat() {
    final colors = context.traceColors;
    return ListView(
      padding: const EdgeInsets.fromLTRB(24, 32, 24, 116),
      children: [
        Align(
          alignment: Alignment.centerLeft,
          child: Container(
            width: 42,
            height: 42,
            decoration: BoxDecoration(
              color: colors.primarySoft,
              borderRadius: BorderRadius.circular(14),
            ),
            child: Center(
              child: TraceIcon(
                TraceGlyph.sparkle,
                color: colors.primaryStrong,
                size: 22,
              ),
            ),
          ),
        ),
        const SizedBox(height: 18),
        Text(
          '问问你的记录',
          style: TextStyle(
            color: colors.ink,
            fontSize: 22,
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 8),
        Text(
          '我会结合你的文字、图片分析和长期记忆来回答，并给出记录证据。',
          style: TextStyle(
            height: 1.6,
            color: colors.inkSecondary,
            fontSize: 13,
          ),
        ),
        const SizedBox(height: 26),
        for (final suggestion in const [
          '我最近去过哪些地方？',
          '帮我总结这个月的生活。',
          '我记录过哪些值得回顾的事？',
        ])
          Padding(
            padding: const EdgeInsets.only(bottom: 9),
            child: Material(
              color: colors.surface,
              borderRadius: BorderRadius.circular(12),
              child: InkWell(
                onTap: () {
                  _input.text = suggestion;
                  _send();
                },
                borderRadius: BorderRadius.circular(12),
                child: Container(
                  constraints: const BoxConstraints(minHeight: 48),
                  padding: const EdgeInsets.symmetric(horizontal: 14),
                  decoration: BoxDecoration(
                    border: Border.all(color: colors.line),
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: Row(
                    children: [
                      Expanded(
                        child: Text(
                          suggestion,
                          style: TextStyle(color: colors.ink, fontSize: 13),
                        ),
                      ),
                      TraceIcon(
                        TraceGlyph.chevronRight,
                        size: 16,
                        color: colors.inkMuted,
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
      ],
    );
  }
}

class AssistantConversationHistory extends StatefulWidget {
  const AssistantConversationHistory({
    super.key,
    required this.conversations,
    required this.selectedConversationId,
    required this.onOpen,
    required this.onDelete,
  });

  final List<AiConversationModel> conversations;
  final String? selectedConversationId;
  final Future<void> Function(AiConversationModel conversation) onOpen;
  final Future<void> Function(AiConversationModel conversation) onDelete;

  @override
  State<AssistantConversationHistory> createState() =>
      _AssistantConversationHistoryState();
}

class _AssistantConversationHistoryState
    extends State<AssistantConversationHistory> {
  late final Set<String> _expandedMonths;

  @override
  void initState() {
    super.initState();
    final groups = _groupConversations(widget.conversations);
    _expandedMonths = {
      if (groups.isNotEmpty) groups.first.key,
      for (final group in groups)
        if (group.conversations.any(
          (conversation) => conversation.id == widget.selectedConversationId,
        ))
          group.key,
    };
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final groups = _groupConversations(widget.conversations);
    final entries = <Object>[
      for (final group in groups) ...[
        group,
        if (_expandedMonths.contains(group.key)) ...group.conversations,
      ],
    ];

    return ListView.builder(
      padding: EdgeInsets.zero,
      itemCount: entries.length,
      itemBuilder: (context, index) {
        final entry = entries[index];
        if (entry is _ConversationMonthGroup) {
          final expanded = _expandedMonths.contains(entry.key);
          return Semantics(
            button: true,
            expanded: expanded,
            label: '${entry.label}，${entry.conversations.length}个会话',
            child: Ink(
              decoration: BoxDecoration(
                color: colors.surfaceSoft,
                border: Border(bottom: BorderSide(color: colors.line)),
              ),
              child: InkWell(
                onTap: () => setState(() {
                  if (expanded) {
                    _expandedMonths.remove(entry.key);
                  } else {
                    _expandedMonths.add(entry.key);
                  }
                }),
                child: ConstrainedBox(
                  constraints: const BoxConstraints(minHeight: 52),
                  child: Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 20),
                    child: Row(
                      children: [
                        Expanded(
                          child: Text(
                            entry.label,
                            style: TextStyle(
                              color: colors.ink,
                              fontSize: 14,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ),
                        Text(
                          '${entry.conversations.length} 个',
                          style: TextStyle(
                            color: colors.inkMuted,
                            fontSize: 12,
                          ),
                        ),
                        const SizedBox(width: 8),
                        TraceIcon(
                          expanded
                              ? TraceGlyph.chevronUp
                              : TraceGlyph.chevronDown,
                          size: 18,
                          color: colors.inkMuted,
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          );
        }

        final conversation = entry as AiConversationModel;
        final selected = conversation.id == widget.selectedConversationId;
        return Ink(
          key: ValueKey(conversation.id),
          decoration: BoxDecoration(
            color: selected ? colors.primarySoft : Colors.transparent,
            border: Border(bottom: BorderSide(color: colors.line)),
          ),
          child: InkWell(
            onTap: () => widget.onOpen(conversation),
            child: ConstrainedBox(
              constraints: const BoxConstraints(minHeight: 64),
              child: Padding(
                padding: const EdgeInsets.only(left: 20, right: 8),
                child: Row(
                  children: [
                    TraceIcon(
                      TraceGlyph.sparkle,
                      size: 20,
                      color: selected ? colors.primary : colors.inkSecondary,
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            conversation.title,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              color: colors.ink,
                              fontWeight: selected
                                  ? FontWeight.w700
                                  : FontWeight.w500,
                            ),
                          ),
                          const SizedBox(height: 3),
                          Text(
                            _formatConversationTime(conversation.updatedAt),
                            style: TextStyle(
                              color: colors.inkMuted,
                              fontSize: 12,
                            ),
                          ),
                        ],
                      ),
                    ),
                    TraceIconButton(
                      glyph: TraceGlyph.delete,
                      tooltip: '删除对话',
                      color: colors.inkMuted,
                      onPressed: () => widget.onDelete(conversation),
                    ),
                  ],
                ),
              ),
            ),
          ),
        );
      },
    );
  }
}

class _ConversationMonthGroup {
  const _ConversationMonthGroup({
    required this.year,
    required this.month,
    required this.conversations,
  });

  final int year;
  final int month;
  final List<AiConversationModel> conversations;

  String get key => '$year-${month.toString().padLeft(2, '0')}';
  String get label => '$year年$month月';
}

List<_ConversationMonthGroup> _groupConversations(
  List<AiConversationModel> conversations,
) {
  final sorted = [...conversations]
    ..sort((left, right) => right.updatedAt.compareTo(left.updatedAt));
  final grouped = <String, List<AiConversationModel>>{};
  final monthValues = <String, ({int year, int month})>{};
  for (final conversation in sorted) {
    final local = conversation.updatedAt.toLocal();
    final key = '${local.year}-${local.month.toString().padLeft(2, '0')}';
    grouped.putIfAbsent(key, () => []).add(conversation);
    monthValues[key] = (year: local.year, month: local.month);
  }
  return grouped.entries
      .map((entry) {
        final value = monthValues[entry.key]!;
        return _ConversationMonthGroup(
          year: value.year,
          month: value.month,
          conversations: entry.value,
        );
      })
      .toList(growable: false);
}

String _formatConversationTime(DateTime value) {
  final local = value.toLocal();
  return '${local.month}月${local.day}日 '
      '${local.hour.toString().padLeft(2, '0')}:'
      '${local.minute.toString().padLeft(2, '0')}';
}

class AssistantEvidenceDisclosure extends StatefulWidget {
  const AssistantEvidenceDisclosure({
    super.key,
    required this.records,
    required this.onOpenEvent,
  });

  final Map<int, String> records;
  final ValueChanged<int> onOpenEvent;

  @override
  State<AssistantEvidenceDisclosure> createState() =>
      _AssistantEvidenceDisclosureState();
}

class _AssistantEvidenceDisclosureState
    extends State<AssistantEvidenceDisclosure> {
  bool _expanded = false;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final count = widget.records.length;
    final reduceMotion =
        MediaQuery.maybeOf(context)?.disableAnimations ?? false;
    return Container(
      margin: const EdgeInsets.only(top: 12),
      decoration: BoxDecoration(
        border: Border(top: BorderSide(color: colors.line)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Material(
            color: Colors.transparent,
            borderRadius: BorderRadius.circular(10),
            clipBehavior: Clip.antiAlias,
            child: InkWell(
              onTap: () => setState(() => _expanded = !_expanded),
              child: Semantics(
                button: true,
                expanded: _expanded,
                label: _expanded ? '收起相关记录，共 $count 条' : '展开相关记录，共 $count 条',
                excludeSemantics: true,
                child: ConstrainedBox(
                  constraints: const BoxConstraints(minHeight: 48),
                  child: Row(
                    children: [
                      TraceIcon(
                        TraceGlyph.note,
                        size: 18,
                        color: colors.accent,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          '相关记录',
                          style: TextStyle(
                            color: colors.inkSecondary,
                            fontSize: 12,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ),
                      Text(
                        '$count 条',
                        style: TextStyle(color: colors.inkMuted, fontSize: 11),
                      ),
                      const SizedBox(width: 6),
                      AnimatedRotation(
                        turns: _expanded ? 0.5 : 0,
                        duration: reduceMotion
                            ? Duration.zero
                            : const Duration(milliseconds: 140),
                        child: TraceIcon(
                          TraceGlyph.chevronDown,
                          size: 16,
                          color: colors.inkMuted,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
          if (_expanded)
            for (final record in widget.records.entries)
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: _EvidenceCard(
                  title: record.value.isEmpty ? '来自你的记录' : record.value,
                  onTap: () => widget.onOpenEvent(record.key),
                ),
              ),
        ],
      ),
    );
  }
}

class _EvidenceCard extends StatelessWidget {
  const _EvidenceCard({required this.title, required this.onTap});

  final String title;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Material(
      color: colors.surfaceSoft,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Container(
          constraints: const BoxConstraints(minHeight: 54),
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          decoration: BoxDecoration(
            border: Border.all(color: colors.lineStrong),
            borderRadius: BorderRadius.circular(12),
          ),
          child: Row(
            children: [
              TraceIcon(TraceGlyph.note, size: 20, color: colors.primaryStrong),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: colors.ink,
                        fontSize: 13,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      '来自你的记录',
                      style: TextStyle(color: colors.inkMuted, fontSize: 11),
                    ),
                  ],
                ),
              ),
              TraceIcon(
                TraceGlyph.chevronRight,
                size: 16,
                color: colors.inkMuted,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class MemoriesView extends StatefulWidget {
  const MemoriesView({
    super.key,
    required this.auth,
    required this.session,
    this.drawer,
    this.onSessionExpired,
  });

  final AuthService auth;
  final AuthSession session;
  final Widget? drawer;
  final Future<void> Function()? onSessionExpired;

  @override
  State<MemoriesView> createState() => _MemoriesViewState();
}

class _MemoriesViewState extends State<MemoriesView> {
  AiApiClient? _api;
  List<UserMemoryModel> _memories = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  @override
  void dispose() {
    _api?.close();
    super.dispose();
  }

  Future<void> _refresh() async {
    try {
      final api = _api ??= AiApiClient(
        auth: widget.auth,
        baseUrl: await widget.auth.getEventsApiBaseUrl(),
      );
      final memories = await api.listMemories(widget.session);
      if (mounted) {
        setState(() {
          _memories = memories;
          _loading = false;
          _error = null;
        });
      }
    } catch (error) {
      if (!mounted) return;
      if (error is EventApiException && error.status == 401) {
        await widget.onSessionExpired?.call();
        return;
      }
      setState(() {
        _loading = false;
        _error = userFacingErrorMessage(error, fallback: '暂时无法加载记忆，请稍后重试。');
      });
    }
  }

  Future<void> _updateMemory(UserMemoryModel memory, bool confirm) async {
    final api = _api;
    if (api == null) return;
    if (confirm) {
      await api.confirmMemory(widget.session, memory.id);
    } else {
      await api.forgetMemory(widget.session, memory.id);
    }
    await _refresh();
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Scaffold(
      drawer: widget.drawer,
      appBar: TraceAppBar(
        title: '我的记忆',
        leading: Builder(
          builder: (menuContext) => TraceIconButton(
            glyph: TraceGlyph.menu,
            tooltip: '打开菜单',
            onPressed: () => Scaffold.of(menuContext).openDrawer(),
          ),
        ),
      ),
      body: _loading
          ? Center(child: CircularProgressIndicator(color: colors.primary))
          : RefreshIndicator(
              onRefresh: _refresh,
              child: _memories.isEmpty
                  ? ListView(
                      padding: const EdgeInsets.fromLTRB(24, 108, 24, 40),
                      children: [
                        Align(
                          child: Container(
                            width: 48,
                            height: 48,
                            decoration: BoxDecoration(
                              color: colors.primarySoft,
                              borderRadius: BorderRadius.circular(15),
                            ),
                            child: Center(
                              child: TraceIcon(
                                TraceGlyph.memory,
                                size: 24,
                                color: colors.primaryStrong,
                              ),
                            ),
                          ),
                        ),
                        const SizedBox(height: 16),
                        Text(
                          '还没有长期记忆',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            color: colors.ink,
                            fontSize: 18,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'AI 会在有明确记录证据时，逐渐建立对你有用的长期记忆。',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            color: colors.inkSecondary,
                            height: 1.6,
                            fontSize: 13,
                          ),
                        ),
                        if (_error != null) ...[
                          const SizedBox(height: 12),
                          Text(
                            _error!,
                            textAlign: TextAlign.center,
                            style: TextStyle(
                              color: Theme.of(context).colorScheme.error,
                              fontSize: 12,
                            ),
                          ),
                        ],
                      ],
                    )
                  : ListView.builder(
                      padding: const EdgeInsets.fromLTRB(18, 20, 18, 36),
                      itemCount: _memories.length,
                      itemBuilder: (_, index) {
                        final memory = _memories[index];
                        return Container(
                          margin: const EdgeInsets.only(bottom: 10),
                          padding: const EdgeInsets.fromLTRB(15, 14, 7, 10),
                          decoration: BoxDecoration(
                            color: colors.surface,
                            border: Border.all(color: colors.line),
                            borderRadius: BorderRadius.circular(16),
                          ),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Padding(
                                padding: const EdgeInsets.only(right: 8),
                                child: Text(
                                  memory.content,
                                  style: TextStyle(
                                    color: colors.ink,
                                    fontSize: 14,
                                    height: 1.55,
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                              ),
                              const SizedBox(height: 9),
                              Row(
                                children: [
                                  Expanded(
                                    child: Text(
                                      '${memory.type} · ${memory.status} · ${memory.evidenceEventIds.length} 条证据',
                                      style: TextStyle(
                                        color: colors.inkMuted,
                                        fontSize: 11,
                                      ),
                                    ),
                                  ),
                                  TraceIconButton(
                                    glyph: TraceGlyph.check,
                                    tooltip: '确认这条记忆',
                                    color: colors.primary,
                                    onPressed: () =>
                                        _updateMemory(memory, true),
                                  ),
                                  TraceIconButton(
                                    glyph: TraceGlyph.delete,
                                    tooltip: '忘记这条记忆',
                                    color: colors.danger,
                                    onPressed: () =>
                                        _updateMemory(memory, false),
                                  ),
                                ],
                              ),
                            ],
                          ),
                        );
                      },
                    ),
            ),
    );
  }
}

class _ChatBubble {
  _ChatBubble({
    required this.role,
    required this.text,
    List<AiEvidenceRecord>? evidenceRecords,
    List<AiEvidenceStoryline>? evidenceStorylines,
    List<AmapPlaceModel>? amapPlaces,
    List<AssistantActionModel>? actions,
  }) : eventTitles = {
         for (final record in evidenceRecords ?? const <AiEvidenceRecord>[])
           record.eventId: record.displayTitle,
       },
       storylineTitles = {
         for (final storyline
             in evidenceStorylines ?? const <AiEvidenceStoryline>[])
           storyline.storylineId.toLowerCase(): storyline.displayTitle,
       },
       amapPlaces = amapPlaces ?? [],
       actions = actions ?? [];
  final String role;
  String text;
  Map<int, String> eventTitles;
  Map<String, String> storylineTitles;
  List<AmapPlaceModel> amapPlaces;
  List<AssistantActionModel> actions;
}

class AmapActionCards extends StatelessWidget {
  const AmapActionCards({
    super.key,
    required this.places,
    required this.actions,
  });

  final List<AmapPlaceModel> places;
  final List<AssistantActionModel> actions;

  @override
  Widget build(BuildContext context) {
    final actionKeys = actions
        .map(
          (action) => '${action.poiId}:${action.latitude}:${action.longitude}',
        )
        .toSet();
    final passivePlaces = places
        .where(
          (place) => !actionKeys.contains(
            '${place.poiId}:${place.latitude}:${place.longitude}',
          ),
        )
        .toList(growable: false);
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: Column(
        children: [
          for (final action in actions)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: _AmapActionCard(action: action),
            ),
          for (final place in passivePlaces)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: _AmapPlaceCard(place: place),
            ),
        ],
      ),
    );
  }
}

class _AmapActionCard extends StatelessWidget {
  const _AmapActionCard({required this.action});

  final AssistantActionModel action;

  Future<void> _open(BuildContext context) async {
    final messenger = ScaffoldMessenger.of(context);
    try {
      if (action.type == 'amap-trip-map') {
        final target = Uri.parse(action.webUrl!);
        if (!await launchUrl(target, mode: LaunchMode.externalApplication)) {
          messenger.showSnackBar(const SnackBar(content: Text('暂时无法打开高德地图。')));
        }
        return;
      }
      final appUri = Uri(
        scheme: 'amapuri',
        host: 'route',
        path: '/plan/',
        queryParameters: {
          'sourceApplication': '星期八',
          'dlat': action.latitude.toStringAsFixed(6),
          'dlon': action.longitude.toStringAsFixed(6),
          'dname': action.placeName,
          'dev': '0',
          't': '0',
        },
      );
      if (await canLaunchUrl(appUri) &&
          await launchUrl(appUri, mode: LaunchMode.externalApplication)) {
        return;
      }
      final webUri = Uri.https('uri.amap.com', '/navigation', {
        'to': '${action.longitude},${action.latitude},${action.placeName}',
        'mode': 'car',
        'coordinate': 'gaode',
        'callnative': '1',
        'src': 'passingtrace',
      });
      if (!await launchUrl(webUri, mode: LaunchMode.externalApplication)) {
        messenger.showSnackBar(const SnackBar(content: Text('暂时无法打开高德地图。')));
      }
    } catch (_) {
      messenger.showSnackBar(const SnackBar(content: Text('导航链接无效，请重新查询地点。')));
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Semantics(
      button: true,
      container: true,
      label: action.label,
      child: Material(
        color: colors.primarySoft.withValues(alpha: 0.52),
        shape: RoundedRectangleBorder(
          side: BorderSide(color: colors.primary.withValues(alpha: 0.32)),
          borderRadius: BorderRadius.circular(14),
        ),
        child: InkWell(
          onTap: () => _open(context),
          borderRadius: BorderRadius.circular(14),
          child: Padding(
            padding: const EdgeInsets.all(10),
            child: Row(
              children: [
                _AmapIcon(colors: colors),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        action.placeName,
                        style: TextStyle(
                          color: colors.ink,
                          fontSize: 13,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 3),
                      Text(
                        _subtitle,
                        style: TextStyle(
                          color: colors.inkMuted,
                          fontSize: 11,
                          height: 1.4,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                ConstrainedBox(
                  constraints: const BoxConstraints(
                    minWidth: 48,
                    minHeight: 48,
                  ),
                  child: Center(
                    child: Text(
                      action.type == 'amap-trip-map' ? '打开' : '导航',
                      style: TextStyle(
                        color: colors.primaryStrong,
                        fontSize: 12,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  String get _subtitle {
    if (action.type == 'amap-trip-map') return '高德专属地图';
    final source = action.source == 'personal-record' ? '来自你的记录' : '来自高德地图';
    return action.address == null ? source : '$source · ${action.address}';
  }
}

class _AmapPlaceCard extends StatelessWidget {
  const _AmapPlaceCard({required this.place});

  final AmapPlaceModel place;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Container(
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: colors.surfaceSoft,
        border: Border.all(color: colors.line),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Row(
        children: [
          _AmapIcon(colors: colors),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  place.name,
                  style: TextStyle(
                    color: colors.ink,
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 3),
                Text(
                  '来自高德地图 · ${place.address ?? '地址未提供'}',
                  style: TextStyle(
                    color: colors.inkMuted,
                    fontSize: 11,
                    height: 1.4,
                  ),
                ),
              ],
            ),
          ),
          Text('高德', style: TextStyle(color: colors.inkMuted, fontSize: 10)),
        ],
      ),
    );
  }
}

class _AmapIcon extends StatelessWidget {
  const _AmapIcon({required this.colors});

  final PassingTraceThemeColors colors;

  @override
  Widget build(BuildContext context) => Container(
    width: 40,
    height: 40,
    decoration: BoxDecoration(
      color: colors.primarySoft,
      borderRadius: BorderRadius.circular(10),
    ),
    child: Center(
      child: TraceIcon(
        TraceGlyph.mapPin,
        size: 20,
        color: colors.primaryStrong,
      ),
    ),
  );
}

class AssistantMessageContent extends StatelessWidget {
  const AssistantMessageContent({
    super.key,
    required this.text,
    required this.isUser,
    this.eventTitles = const {},
    this.storylineTitles = const {},
    this.onOpenEvent,
    this.onOpenStoryline,
  });

  final String text;
  final bool isUser;
  final Map<int, String> eventTitles;
  final Map<String, String> storylineTitles;
  final ValueChanged<int>? onOpenEvent;
  final ValueChanged<String>? onOpenStoryline;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    if (isUser || text.isEmpty) {
      return Text(
        text.isEmpty ? '正在检索…' : text,
        style: TextStyle(color: isUser ? colors.onPrimary : colors.ink),
      );
    }

    return MarkdownBody(
      data: _replaceCitations(text, eventTitles, storylineTitles),
      selectable: true,
      onTapLink: (_, href, _) {
        final uri = Uri.tryParse(href ?? '');
        if (uri?.scheme != 'passingtrace') return;
        final id = uri!.pathSegments.firstOrNull;
        if (uri.host == 'event') {
          final eventId = int.tryParse(id ?? '');
          if (eventId != null) onOpenEvent?.call(eventId);
        } else if (uri.host == 'storyline' && id != null) {
          onOpenStoryline?.call(id);
        }
      },
      styleSheet: MarkdownStyleSheet(
        p: TextStyle(color: colors.ink, fontSize: 13, height: 1.7),
        h1: TextStyle(
          color: colors.ink,
          fontSize: 20,
          fontWeight: FontWeight.w700,
          height: 1.4,
        ),
        h2: TextStyle(
          color: colors.ink,
          fontSize: 18,
          fontWeight: FontWeight.w700,
          height: 1.4,
        ),
        h3: TextStyle(
          color: colors.ink,
          fontSize: 16,
          fontWeight: FontWeight.w700,
          height: 1.4,
        ),
        listBullet: TextStyle(color: colors.accent, fontSize: 13, height: 1.7),
        a: TextStyle(
          color: colors.primaryStrong,
          fontWeight: FontWeight.w700,
          decoration: TextDecoration.underline,
          decorationColor: colors.primary.withValues(alpha: 0.72),
          decorationThickness: 1.2,
        ),
        blockquoteDecoration: BoxDecoration(
          color: colors.surfaceSoft,
          border: Border(left: BorderSide(color: colors.accent, width: 3)),
        ),
        code: TextStyle(
          color: colors.ink,
          backgroundColor: colors.surfaceSoft,
          fontFamily: 'monospace',
        ),
      ),
    );
  }

  static String _replaceCitations(
    String source,
    Map<int, String> eventTitles,
    Map<String, String> storylineTitles,
  ) {
    final withEvents = source.replaceAllMapped(
      RegExp(r'\[Event\s*#(\d+)\]', caseSensitive: false),
      (match) {
        final id = int.parse(match.group(1)!);
        final title = eventTitles[id]?.trim();
        final label = title == null || title.isEmpty ? '查看记录' : title;
        return '[${_escapeMarkdownLabel(label)}](passingtrace://event/$id)';
      },
    );
    return withEvents.replaceAllMapped(
      RegExp(
        r'\[Storyline\s*#([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\]',
        caseSensitive: false,
      ),
      (match) {
        final id = match.group(1)!.toLowerCase();
        final title = storylineTitles[id]?.trim();
        final label = title == null || title.isEmpty ? '查看故事线' : title;
        return '[${_escapeMarkdownLabel(label)}](passingtrace://storyline/$id)';
      },
    );
  }

  static String _escapeMarkdownLabel(String label) => label
      .replaceAll(r'\', r'\\')
      .replaceAll('[', r'\[')
      .replaceAll(']', r'\]');
}
