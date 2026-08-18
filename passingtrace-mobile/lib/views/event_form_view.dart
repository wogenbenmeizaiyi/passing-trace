// Event 表单页：创建与编辑共用，模式由 `eventId` 是否为 null 决定。
//
//   - `kind` 在编辑模式下锁定为已存值。
//   - 创建时生成 Idempotency-Key 并在本次会话内复用（成功后丢弃）。
//   - PATCH 携带 `If-Match: version`；遇到 409 重新拉取详情让用户改。

import 'dart:math';

import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/event_datetime.dart';
import '../events/event_model.dart';
import '../events/events_api.dart';
import '../main.dart';

class EventFormView extends StatefulWidget {
  const EventFormView({
    super.key,
    required this.auth,
    required this.session,
    this.eventId,
  });

  final AuthService auth;
  final AuthSession session;
  final int? eventId;

  @override
  State<EventFormView> createState() => _EventFormViewState();
}

class _EventFormViewState extends State<EventFormView> {
  late EventApiClient _api;
  EventModel? _loaded;
  bool _loading = false;
  bool _submitting = false;
  String? _error;
  String? _idempotencyKey;

  final _formKey = GlobalKey<FormState>();
  EventKind _kind = EventKind.trace;
  final _title = TextEditingController();
  final _content = TextEditingController();
  final _when = TextEditingController();
  final _timezone = TextEditingController(text: defaultTimezone());
  int? _loadedVersion;

  bool get _isEdit => widget.eventId != null;

  @override
  void initState() {
    super.initState();
    _initApi();
  }

  Future<void> _initApi() async {
    final baseUrl = await widget.auth.getEventsApiBaseUrl();
    if (!mounted) return;
    setState(() => _api = EventApiClient(auth: widget.auth, baseUrl: baseUrl));
    if (_isEdit) {
      await _loadDetail();
    }
  }

  @override
  void dispose() {
    _api.close();
    _title.dispose();
    _content.dispose();
    _when.dispose();
    _timezone.dispose();
    super.dispose();
  }

  Future<void> _loadDetail() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final event = await _api.get(widget.session, widget.eventId!);
      if (!mounted) return;
      setState(() {
        _loaded = event;
        _loadedVersion = event.version;
        _kind = event.kind;
        _title.text = event.title ?? '';
        _content.text = event.rawContent ?? '';
        _when.text = toWallClockLocal(
          event.kind == EventKind.plan ? event.plannedAt : event.happenedAt,
        );
        _timezone.text = event.timezone;
      });
    } on EventApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = '加载失败：$e');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  String _ensureIdempotencyKey() {
    final existing = _idempotencyKey;
    if (existing != null && existing.isNotEmpty) return existing;
    final fresh = _newIdempotencyKey();
    _idempotencyKey = fresh;
    return fresh;
  }

  static String _newIdempotencyKey() {
    // 用 `Random.secure` 拼一个 v4 风格 UUID。无需严格 RFC 4122，只保证唯一性。
    final random = Random.secure();
    String hex(int bytes) {
      final values = List<int>.generate(bytes, (_) => random.nextInt(256));
      return values
          .map((b) => b.toRadixString(16).padLeft(2, '0'))
          .join();
    }
    final h = hex(16);
    return '${h.substring(0, 8)}-${h.substring(8, 12)}-'
        '${h.substring(12, 16)}-${h.substring(16, 20)}-${h.substring(20, 32)}';
  }

  DateTime? _parseWhen() {
    final raw = _when.text.trim();
    if (raw.isEmpty) return null;
    // `YYYY-MM-DDTHH:mm` → ISO 8601 + IANA offset → DateTime.
    final iso = toIsoWithOffset(raw, _timezone.text.trim());
    if (iso == null) return null;
    return DateTime.parse(iso).toLocal();
  }

  Future<void> _submit() async {
    if (_submitting) return;
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final tz = _timezone.text.trim();
      final when = _parseWhen();
      if (_isEdit) {
        final loaded = _loaded;
        if (loaded == null || _loadedVersion == null) {
          throw StateError('编辑模式下缺少已加载的详情');
        }
        final updated = await _api.update(
          widget.session,
          loaded.id,
          title: _title.text.trim().isEmpty ? null : _title.text.trim(),
          rawContent: _content.text.trim().isEmpty ? null : _content.text.trim(),
          happenedAt:
              _kind == EventKind.trace ? when : null,
          plannedAt: _kind == EventKind.plan ? when : null,
          timezone: tz,
          version: _loadedVersion!,
        );
        if (!mounted) return;
        Navigator.of(context).pop(true);
        // 避免 lint 警告 unused
        updated.id;
      } else {
        final key = _ensureIdempotencyKey();
        final created = await _api.create(
          widget.session,
          kind: _kind,
          title: _title.text.trim().isEmpty ? null : _title.text.trim(),
          rawContent:
              _content.text.trim().isEmpty ? null : _content.text.trim(),
          happenedAt: _kind == EventKind.trace ? when : null,
          plannedAt: _kind == EventKind.plan ? when : null,
          timezone: tz,
          idempotencyKey: key,
        );
        _idempotencyKey = null;
        if (!mounted) return;
        Navigator.of(context).pop(true);
        // 关闭后通知列表
        created.id;
      }
    } on EventApiException catch (e) {
      if (!mounted) return;
      String message = e.message;
      if (e.status == 409) {
        if (_isEdit) {
          message = '内容已被他人修改，请核对最新版本后再保存。';
          await _loadDetail();
        } else {
          // 409 创建场景是幂等键冲突，丢弃 key 让用户重试。
          _idempotencyKey = null;
          message = '同一条请求被重试了多次但内容不一致，请重新提交。';
        }
      } else if (e.status == 428) {
        if (_isEdit) {
          message = '本地版本信息缺失，正在重新加载…';
          await _loadDetail();
        } else {
          message = '本地版本信息缺失，请重新提交。';
        }
      } else if (e.status == 401) {
        message = '登录状态已失效，请重新登录。';
      } else if (e.status == 400) {
        message = e.problem?.detail ?? '请求格式不合法，请检查表单。';
      }
      setState(() => _error = message);
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = '保存失败：$e');
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(
          _isEdit ? '编辑记录' : '记一笔',
          style: const TextStyle(
            fontFamily: 'serif',
            fontWeight: FontWeight.w600,
            fontSize: 19,
          ),
        ),
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _buildForm(),
    );
  }

  Widget _buildForm() {
    return Form(
      key: _formKey,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(20, 18, 20, 24),
        children: [
          SegmentedButton<EventKind>(
            segments: const [
              ButtonSegment(
                value: EventKind.trace,
                label: Text('痕迹'),
                icon: Icon(Icons.history),
              ),
              ButtonSegment(
                value: EventKind.plan,
                label: Text('计划'),
                icon: Icon(Icons.flag_outlined),
              ),
            ],
            selected: {_kind},
            onSelectionChanged: _isEdit
                ? null
                : (selection) {
                    setState(() => _kind = selection.first);
                  },
            showSelectedIcon: false,
          ),
          const SizedBox(height: 18),
          TextFormField(
            controller: _title,
            maxLength: 200,
            decoration: _decoration('标题', '一句话标题（可与正文同时为空则非法）'),
            validator: (_) {
              if (_title.text.trim().isEmpty && _content.text.trim().isEmpty) {
                return '标题与正文至少需要填写一项。';
              }
              return null;
            },
            textInputAction: TextInputAction.next,
          ),
          const SizedBox(height: 6),
          TextFormField(
            controller: _content,
            minLines: 5,
            maxLines: 12,
            decoration: _decoration('正文', '把当下想到的、看到的、吃到的写下来…'),
            validator: (_) {
              if (_title.text.trim().isEmpty && _content.text.trim().isEmpty) {
                return '标题与正文至少需要填写一项。';
              }
              return null;
            },
            textInputAction: TextInputAction.newline,
          ),
          const SizedBox(height: 18),
          TextFormField(
            controller: _when,
            decoration: _decoration(
              _kind == EventKind.plan ? '计划时间' : '发生时间',
              'YYYY-MM-DDTHH:mm',
            ),
            validator: (value) {
              if (value == null || value.trim().isEmpty) return null;
              return _parseWhen() == null ? '时间格式不正确。' : null;
            },
          ),
          const SizedBox(height: 16),
          TextFormField(
            controller: _timezone,
            decoration: _decoration('时区 (IANA)', 'Asia/Tokyo'),
            validator: (value) {
              if (value == null || value.trim().isEmpty) {
                return '请填写 IANA 时区名，例如 Asia/Tokyo。';
              }
              return null;
            },
          ),
          if (_error != null) ...[
            const SizedBox(height: 14),
            Text(
              _error!,
              style: const TextStyle(color: Colors.redAccent, fontSize: 12),
            ),
          ],
          const SizedBox(height: 24),
          Row(
            children: [
              Expanded(
                child: OutlinedButton(
                  onPressed: _submitting ? null : () => Navigator.of(context).pop(false),
                  style: OutlinedButton.styleFrom(
                    minimumSize: const Size.fromHeight(50),
                    shape: const RoundedRectangleBorder(),
                  ),
                  child: const Text('取消'),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: FilledButton(
                  onPressed: _submitting ? null : _submit,
                  style: FilledButton.styleFrom(
                    minimumSize: const Size.fromHeight(50),
                    backgroundColor: PassingTraceApp.coral,
                    shape: const RoundedRectangleBorder(),
                  ),
                  child: _submitting
                      ? const SizedBox.square(
                          dimension: 18,
                          child: CircularProgressIndicator(
                            color: Colors.white,
                            strokeWidth: 2,
                          ),
                        )
                      : Text(_isEdit ? '保存修改' : '创建记录'),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  InputDecoration _decoration(String label, String? hint) => InputDecoration(
    labelText: label,
    hintText: hint,
    filled: true,
    fillColor: Colors.white.withValues(alpha: 0.6),
    enabledBorder: OutlineInputBorder(
      borderSide: BorderSide(color: PassingTraceApp.ink.withValues(alpha: 0.18)),
    ),
    counterText: label == '标题' ? null : '',
  );
}
