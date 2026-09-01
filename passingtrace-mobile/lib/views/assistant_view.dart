import 'package:flutter/material.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';

import '../auth_service.dart';
import '../events/ai_api.dart';
import '../events/events_api.dart';
import '../theme/passingtrace_theme.dart';
import 'event_detail_view.dart';

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
          final records = raw['records'] as List<dynamic>? ?? const [];
          setState(
            () => answer.eventTitles = {
              for (final raw in records.whereType<Map<String, dynamic>>())
                (raw['eventId'] as num).toInt():
                    (raw['title'] as String?)?.trim() ?? '',
            },
          );
        } else if (chunk.type == 'error') {
          throw StateError(
            (chunk.data as Map<String, dynamic>)['message'] as String,
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
      builder: (sheetContext) => SafeArea(
        child: SizedBox(
          height: MediaQuery.sizeOf(context).height * 0.62,
          child: Column(
            children: [
              ListTile(
                title: const Text(
                  '聊天记录',
                  style: TextStyle(fontSize: 20, fontWeight: FontWeight.w700),
                ),
                trailing: FilledButton.icon(
                  onPressed: () {
                    Navigator.of(sheetContext).pop();
                    _newConversation();
                  },
                  icon: const Icon(Icons.add),
                  label: const Text('新对话'),
                ),
              ),
              const Divider(height: 1),
              Expanded(
                child: _conversations.isEmpty
                    ? const Center(child: Text('还没有聊天记录'))
                    : ListView.builder(
                        itemCount: _conversations.length,
                        itemBuilder: (_, index) {
                          final conversation = _conversations[index];
                          return ListTile(
                            selected: conversation.id == _conversationId,
                            leading: const Icon(Icons.chat_bubble_outline),
                            title: Text(conversation.title),
                            subtitle: Text(
                              _formatConversationTime(conversation.updatedAt),
                            ),
                            onTap: () => _openConversation(conversation),
                            trailing: IconButton(
                              tooltip: '删除对话',
                              onPressed: () async {
                                Navigator.of(sheetContext).pop();
                                await _deleteConversation(conversation);
                              },
                              icon: const Icon(Icons.delete_outline),
                            ),
                          );
                        },
                      ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  String _formatConversationTime(DateTime value) {
    final local = value.toLocal();
    return '${local.month}月${local.day}日 '
        '${local.hour.toString().padLeft(2, '0')}:'
        '${local.minute.toString().padLeft(2, '0')}';
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
    setState(() => _error = error.toString());
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    drawer: widget.drawer,
    bottomNavigationBar: widget.bottomNavigationBar,
    appBar: AppBar(
      title: const Text('问问记录'),
      actions: [
        IconButton(
          tooltip: '聊天记录',
          onPressed: _showConversations,
          icon: const Icon(Icons.history),
        ),
        IconButton(
          tooltip: '新对话',
          onPressed: _busy ? null : _newConversation,
          icon: const Icon(Icons.add_comment_outlined),
        ),
      ],
    ),
    body: _buildChat(),
  );

  Widget _buildChat() {
    final colors = context.traceColors;
    return Column(
      children: [
        Expanded(
          child: _initialLoading
              ? const Center(child: CircularProgressIndicator())
              : _messages.isEmpty
              ? _buildEmptyChat()
              : ListView.builder(
                  padding: const EdgeInsets.all(16),
                  itemCount: _messages.length,
                  itemBuilder: (_, index) {
                    final message = _messages[index];
                    final mine = message.role == 'user';
                    return Align(
                      alignment: mine
                          ? Alignment.centerRight
                          : Alignment.centerLeft,
                      child: Container(
                        constraints: const BoxConstraints(maxWidth: 330),
                        margin: const EdgeInsets.only(bottom: 12),
                        padding: const EdgeInsets.all(14),
                        decoration: BoxDecoration(
                          color: mine ? colors.primary : colors.surface,
                          borderRadius: BorderRadius.only(
                            topLeft: Radius.circular(mine ? 18 : 5),
                            topRight: const Radius.circular(18),
                            bottomLeft: const Radius.circular(18),
                            bottomRight: Radius.circular(mine ? 5 : 18),
                          ),
                          border: mine ? null : Border.all(color: colors.line),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            AssistantMessageContent(
                              text: message.text,
                              isUser: mine,
                              eventTitles: message.eventTitles,
                              onOpenEvent: (id) => Navigator.push(
                                context,
                                MaterialPageRoute(
                                  builder: (_) => EventDetailView(
                                    auth: widget.auth,
                                    session: widget.session,
                                    eventId: id,
                                  ),
                                ),
                              ),
                            ),
                            if (message.eventTitles.isNotEmpty) ...[
                              const SizedBox(height: 8),
                              Wrap(
                                spacing: 6,
                                runSpacing: 6,
                                children: message.eventTitles.entries
                                    .map(
                                      (record) => ActionChip(
                                        label: Text(
                                          record.value.isEmpty
                                              ? '记录 #${record.key}'
                                              : record.value,
                                        ),
                                        backgroundColor: colors.accentSoft,
                                        side: BorderSide(color: colors.accent),
                                        onPressed: () => Navigator.push(
                                          context,
                                          MaterialPageRoute(
                                            builder: (_) => EventDetailView(
                                              auth: widget.auth,
                                              session: widget.session,
                                              eventId: record.key,
                                            ),
                                          ),
                                        ),
                                      ),
                                    )
                                    .toList(),
                              ),
                            ],
                          ],
                        ),
                      ),
                    );
                  },
                ),
        ),
        if (_error != null)
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: Text(
              _error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ),
        SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _input,
                    minLines: 1,
                    maxLines: 4,
                    decoration: const InputDecoration(
                      hintText: '询问自己的记录…',
                      filled: true,
                    ),
                    onSubmitted: (_) => _send(),
                  ),
                ),
                const SizedBox(width: 8),
                IconButton.filled(
                  onPressed: _busy || _conversationId == null ? null : _send,
                  icon: _busy
                      ? const SizedBox.square(
                          dimension: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.send),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildEmptyChat() {
    final colors = context.traceColors;
    return ListView(
      padding: const EdgeInsets.fromLTRB(24, 56, 24, 24),
      children: [
        Center(
          child: Container(
            width: 64,
            height: 64,
            decoration: BoxDecoration(
              color: colors.primarySoft,
              borderRadius: BorderRadius.circular(18),
            ),
            child: Icon(
              Icons.auto_awesome,
              color: colors.primaryStrong,
              size: 30,
            ),
          ),
        ),
        const SizedBox(height: 20),
        Text(
          '问问你的记录',
          textAlign: TextAlign.center,
          style: TextStyle(
            color: colors.ink,
            fontSize: 25,
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 10),
        Text(
          '我会结合你的文字、图片分析和长期记忆来回答，并给出记录证据。',
          textAlign: TextAlign.center,
          style: TextStyle(height: 1.6, color: colors.inkSecondary),
        ),
        const SizedBox(height: 30),
        for (final suggestion in const [
          '我最近去过哪些地方？',
          '帮我总结这个月的生活。',
          '我记录过哪些值得回顾的事？',
        ])
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: OutlinedButton(
              onPressed: () {
                _input.text = suggestion;
                _send();
              },
              style: OutlinedButton.styleFrom(
                alignment: Alignment.centerLeft,
                padding: const EdgeInsets.symmetric(
                  horizontal: 18,
                  vertical: 15,
                ),
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(16),
                ),
              ),
              child: Row(
                children: [
                  const Icon(Icons.chat_bubble_outline, size: 18),
                  const SizedBox(width: 12),
                  Expanded(child: Text(suggestion)),
                  const Icon(Icons.arrow_forward_ios, size: 13),
                ],
              ),
            ),
          ),
      ],
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
        _error = error.toString();
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
  Widget build(BuildContext context) => Scaffold(
    drawer: widget.drawer,
    appBar: AppBar(title: const Text('我的记忆')),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : RefreshIndicator(
            onRefresh: _refresh,
            child: _memories.isEmpty
                ? ListView(
                    children: [
                      const SizedBox(height: 190),
                      Icon(
                        Icons.psychology_outlined,
                        size: 42,
                        color: context.traceColors.primary,
                      ),
                      const SizedBox(height: 14),
                      const Center(child: Text('还没有有证据的长期记忆。')),
                      if (_error != null) ...[
                        const SizedBox(height: 10),
                        Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 24),
                          child: Text(
                            _error!,
                            textAlign: TextAlign.center,
                            style: TextStyle(
                              color: Theme.of(context).colorScheme.error,
                            ),
                          ),
                        ),
                      ],
                    ],
                  )
                : ListView.builder(
                    padding: const EdgeInsets.all(16),
                    itemCount: _memories.length,
                    itemBuilder: (_, index) {
                      final memory = _memories[index];
                      return Card(
                        child: ListTile(
                          title: Text(memory.content),
                          subtitle: Text(
                            '${memory.type} · ${memory.status} · ${memory.evidenceEventIds.length} 条证据',
                          ),
                          trailing: PopupMenuButton<String>(
                            onSelected: (action) =>
                                _updateMemory(memory, action == 'confirm'),
                            itemBuilder: (_) => const [
                              PopupMenuItem(
                                value: 'confirm',
                                child: Text('确认'),
                              ),
                              PopupMenuItem(value: 'forget', child: Text('忘记')),
                            ],
                          ),
                        ),
                      );
                    },
                  ),
          ),
  );
}

class _ChatBubble {
  _ChatBubble({
    required this.role,
    required this.text,
    List<AiEvidenceRecord>? evidenceRecords,
  }) : eventTitles = {
         for (final record in evidenceRecords ?? const <AiEvidenceRecord>[])
           record.eventId: record.title ?? '',
       };
  final String role;
  String text;
  Map<int, String> eventTitles;
}

class AssistantMessageContent extends StatelessWidget {
  const AssistantMessageContent({
    super.key,
    required this.text,
    required this.isUser,
    this.eventTitles = const {},
    this.onOpenEvent,
  });

  final String text;
  final bool isUser;
  final Map<int, String> eventTitles;
  final ValueChanged<int>? onOpenEvent;

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
      data: _replaceEventCitations(text, eventTitles),
      selectable: true,
      onTapLink: (_, href, _) {
        final uri = Uri.tryParse(href ?? '');
        if (uri?.scheme != 'passingtrace' || uri?.host != 'event') return;
        final eventId = int.tryParse(uri!.pathSegments.firstOrNull ?? '');
        if (eventId != null) onOpenEvent?.call(eventId);
      },
      styleSheet: MarkdownStyleSheet(
        p: TextStyle(color: colors.ink, fontSize: 15, height: 1.55),
        h1: TextStyle(
          color: colors.ink,
          fontSize: 22,
          fontWeight: FontWeight.w700,
          height: 1.4,
        ),
        h2: TextStyle(
          color: colors.ink,
          fontSize: 19,
          fontWeight: FontWeight.w700,
          height: 1.4,
        ),
        h3: TextStyle(
          color: colors.ink,
          fontSize: 17,
          fontWeight: FontWeight.w700,
          height: 1.4,
        ),
        listBullet: TextStyle(color: colors.accent, fontSize: 15, height: 1.55),
        a: TextStyle(
          color: colors.accent,
          backgroundColor: colors.accentSoft,
          fontWeight: FontWeight.w700,
          decoration: TextDecoration.underline,
          decorationColor: colors.accent,
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

  static String _replaceEventCitations(
    String source,
    Map<int, String> titles,
  ) => source.replaceAllMapped(
    RegExp(r'\[Event\s*#(\d+)\]', caseSensitive: false),
    (match) {
      final id = int.parse(match.group(1)!);
      final title = titles[id]?.trim();
      final label = title == null || title.isEmpty ? '记录 #$id' : title;
      final escaped = label
          .replaceAll(r'\', r'\\')
          .replaceAll('[', r'\[')
          .replaceAll(']', r'\]');
      return '[$escaped](passingtrace://event/$id)';
    },
  );
}
