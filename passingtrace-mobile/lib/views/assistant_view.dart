import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/ai_api.dart';
import '../main.dart';
import 'event_detail_view.dart';

class AssistantView extends StatefulWidget {
  const AssistantView({super.key, required this.auth, required this.session});
  final AuthService auth;
  final AuthSession session;

  @override
  State<AssistantView> createState() => _AssistantViewState();
}

class _AssistantViewState extends State<AssistantView> {
  late AiApiClient _api;
  final _input = TextEditingController();
  final List<_ChatBubble> _messages = [];
  List<UserMemoryModel> _memories = const [];
  String? _conversationId;
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
      final conversation = await _api.createConversation(widget.session);
      final memories = await _api.listMemories(widget.session);
      if (mounted) {
        setState(() {
          _conversationId = conversation.id;
          _memories = memories;
        });
      }
    } catch (error) {
      if (mounted) setState(() => _error = error.toString());
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
            () => answer.eventIds = records
                .map(
                  (record) =>
                      ((record as Map<String, dynamic>)['eventId'] as num)
                          .toInt(),
                )
                .toSet()
                .toList(),
          );
        } else if (chunk.type == 'error') {
          throw StateError(
            (chunk.data as Map<String, dynamic>)['message'] as String,
          );
        }
      }
    } catch (error) {
      if (mounted) setState(() => _error = error.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _refreshMemories() async {
    final memories = await _api.listMemories(widget.session);
    if (mounted) setState(() => _memories = memories);
  }

  @override
  Widget build(BuildContext context) => DefaultTabController(
    length: 2,
    child: Scaffold(
      appBar: AppBar(
        title: const Text('生活洞察', style: TextStyle(fontFamily: 'serif')),
        bottom: const TabBar(
          tabs: [
            Tab(text: '问记录'),
            Tab(text: '我的记忆'),
          ],
        ),
      ),
      body: TabBarView(children: [_buildChat(), _buildMemories()]),
    ),
  );

  Widget _buildChat() => Column(
    children: [
      Expanded(
        child: _messages.isEmpty
            ? const Center(child: Text('可以问：我上个月去过哪些地方？'))
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
                      color: mine ? PassingTraceApp.coral : Colors.white,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            message.text.isEmpty ? '正在检索…' : message.text,
                            style: TextStyle(
                              color: mine ? Colors.white : PassingTraceApp.ink,
                            ),
                          ),
                          if (message.eventIds.isNotEmpty) ...[
                            const SizedBox(height: 8),
                            Wrap(
                              spacing: 6,
                              children: message.eventIds
                                  .map(
                                    (id) => ActionChip(
                                      label: Text('Event #$id'),
                                      onPressed: () => Navigator.push(
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
          child: Text(_error!, style: const TextStyle(color: Colors.redAccent)),
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
                onPressed: _busy ? null : _send,
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

  Widget _buildMemories() => RefreshIndicator(
    onRefresh: _refreshMemories,
    child: _memories.isEmpty
        ? ListView(
            children: const [
              SizedBox(height: 220),
              Center(child: Text('还没有有证据的长期记忆。')),
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
                    onSelected: (action) async {
                      if (action == 'confirm') {
                        await _api.confirmMemory(widget.session, memory.id);
                      } else {
                        await _api.forgetMemory(widget.session, memory.id);
                      }
                      await _refreshMemories();
                    },
                    itemBuilder: (_) => const [
                      PopupMenuItem(value: 'confirm', child: Text('确认')),
                      PopupMenuItem(value: 'forget', child: Text('忘记')),
                    ],
                  ),
                ),
              );
            },
          ),
  );
}

class _ChatBubble {
  _ChatBubble({required this.role, required this.text});
  final String role;
  String text;
  List<int> eventIds = [];
}
