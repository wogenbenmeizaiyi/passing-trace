// Event 表单页：创建与编辑共用，模式由 `eventId` 是否为 null 决定。
//
//   - `kind` 在编辑模式下锁定为已存值。
//   - 创建时生成 Idempotency-Key 并在本次会话内复用（成功后丢弃）。
//   - PATCH 携带 `If-Match: version`；遇到 409 重新拉取详情让用户改。

import 'dart:math';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/event_datetime.dart';
import '../events/event_model.dart';
import '../events/events_api.dart';
import '../events/media_api.dart';
import '../events/location_service.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';
import 'nearby_place_sheet.dart';

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
  late MediaApiClient _mediaApi;
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
  final List<_FormMediaItem> _media = [];
  final AmapLocationService _locationService = AmapLocationService();
  EventTaxonomyModel? _taxonomy;
  String? _primaryCategoryKey;
  final List<ManualTagModel> _manualTags = [];
  final Set<String> _suppressedAiTags = {};
  EventLocationModel? _location;
  bool _locating = false;
  bool _classificationExpanded = false;
  final _customTag = TextEditingController();

  bool get _isEdit => widget.eventId != null;

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
      _mediaApi = MediaApiClient(auth: widget.auth, baseUrl: baseUrl);
    });
    try {
      final taxonomy = await _api.taxonomy(widget.session);
      if (mounted) setState(() => _taxonomy = taxonomy);
    } catch (_) {
      // 分类词表加载失败不阻止记录正文与附件编辑。
    }
    if (_isEdit) {
      await _loadDetail();
    }
  }

  @override
  void dispose() {
    _api.close();
    _mediaApi.close();
    _title.dispose();
    _content.dispose();
    _when.dispose();
    _timezone.dispose();
    _customTag.dispose();
    _locationService.dispose();
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
        _media
          ..clear()
          ..addAll(
            event.media.map(
              (asset) => _FormMediaItem(
                id: asset.id,
                name: asset.fileName,
                kind: asset.kind,
                progress: 1,
              ),
            ),
          );
        _primaryCategoryKey = event.manualClassification.primaryCategoryKey;
        _manualTags
          ..clear()
          ..addAll(event.manualClassification.tags);
        _suppressedAiTags
          ..clear()
          ..addAll(event.manualClassification.suppressedAiTagKeys);
        _location = event.locations.isEmpty ? null : event.locations.first;
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
      return values.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
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

  Future<void> _pickWhen() async {
    FocusScope.of(context).unfocus();
    final existing = DateTime.tryParse(
      _when.text.trim().replaceFirst(' ', 'T'),
    );
    final initial = existing ?? DateTime.now();
    final date = await showDatePicker(
      context: context,
      initialDate: initial,
      firstDate: DateTime(1900),
      lastDate: DateTime(2100, 12, 31),
      helpText: _kind == EventKind.plan ? '选择预定日期' : '选择发生日期',
      cancelText: '取消',
      confirmText: '下一步',
    );
    if (date == null || !mounted) return;

    final time = await showTimePicker(
      context: context,
      initialTime: TimeOfDay.fromDateTime(initial),
      helpText: _kind == EventKind.plan ? '选择预定时间' : '选择发生时间',
      cancelText: '取消',
      confirmText: '确定',
    );
    if (time == null || !mounted) return;

    String two(int value) => value.toString().padLeft(2, '0');
    setState(() {
      _when.text =
          '${date.year.toString().padLeft(4, '0')}-'
          '${two(date.month)}-${two(date.day)} '
          '${two(time.hour)}:${two(time.minute)}';
    });
  }

  Future<void> _pickMedia() async {
    if (_media.length >= 10) {
      setState(() => _error = '每条记录最多添加 10 个附件。');
      return;
    }
    final files = await FilePicker.pickFiles();
    if (files.isEmpty || !mounted) return;
    final available = 10 - _media.length;
    for (final file in files.take(available)) {
      final item = _FormMediaItem(name: file.name, source: file);
      setState(() {
        _media.add(item);
        _error = null;
      });
      await _upload(item);
    }
  }

  Future<void> _upload(_FormMediaItem item) async {
    final source = item.source;
    if (source == null) return;
    setState(() {
      item.error = null;
      item.progress = 0;
      item.uploading = true;
    });
    try {
      final uploaded = await _mediaApi.upload(
        widget.session,
        source,
        onProgress: (value) {
          if (mounted) setState(() => item.progress = value);
        },
      );
      if (!mounted) return;
      setState(() {
        item.id = uploaded.id;
        item.kind = uploaded.kind;
        item.progress = 1;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() => item.error = error.toString());
    } finally {
      if (mounted) setState(() => item.uploading = false);
    }
  }

  Future<void> _removeMedia(int index) async {
    final item = _media.removeAt(index);
    setState(() {});
    if (item.source != null && item.id != null) {
      try {
        await _mediaApi.delete(widget.session, item.id!);
      } catch (_) {
        // 未关联上传会由服务端 24 小时清理任务兜底。
      }
    }
  }

  void _moveMedia(int index, int delta) {
    final target = index + delta;
    if (target < 0 || target >= _media.length) return;
    setState(() {
      final item = _media.removeAt(index);
      _media.insert(target, item);
    });
  }

  Future<void> _submit() async {
    if (_submitting) return;
    if (!_formKey.currentState!.validate()) return;
    if (_media.any((item) => item.uploading || item.id == null)) {
      setState(() => _error = '请等待所有附件上传完成，或移除上传失败的附件。');
      return;
    }
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
          rawContent: _content.text.trim().isEmpty
              ? null
              : _content.text.trim(),
          happenedAt: _kind == EventKind.trace ? when : null,
          plannedAt: _kind == EventKind.plan ? when : null,
          timezone: tz,
          version: _loadedVersion!,
          mediaIds: _media.map((item) => item.id!).toList(growable: false),
          classification: _classification(),
          locations: _location == null ? <EventLocationModel>[] : [_location!],
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
          rawContent: _content.text.trim().isEmpty
              ? null
              : _content.text.trim(),
          happenedAt: _kind == EventKind.trace ? when : null,
          plannedAt: _kind == EventKind.plan ? when : null,
          timezone: tz,
          idempotencyKey: key,
          mediaIds: _media.map((item) => item.id!).toList(growable: false),
          classification: _classification(),
          locations: _location == null ? <EventLocationModel>[] : [_location!],
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

  ManualClassification _classification() => ManualClassification(
    primaryCategoryKey: _primaryCategoryKey,
    tags: List.unmodifiable(_manualTags),
    suppressedAiTagKeys: _suppressedAiTags.toList(growable: false),
  );

  String _categoryLabel() {
    for (final item in _taxonomy?.categories ?? const <TaxonomyItem>[]) {
      if (item.key == _primaryCategoryKey) return item.label;
    }
    return '未分类';
  }

  Future<void> _pickLocation() async {
    final accepted = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('选择地点'),
        content: const Text(
          '星期八将获取一次前台位置并打开高德地图。你可以拖动地图选点，再从附近地点中选择。不会后台定位或保存轨迹。',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('同意并选择'),
          ),
        ],
      ),
    );
    if (accepted != true || !mounted) return;
    setState(() => _locating = true);
    try {
      if (!await _locationService.requestPermission()) {
        throw StateError('未授予前台定位权限。');
      }
      final current = await _locationService.locateOnce(privacyAccepted: true);
      final point = await _locationService.pickMapPoint(
        latitude: current.latitude,
        longitude: current.longitude,
        privacyAccepted: true,
      );
      if (point == null || !mounted) return;
      final candidates = await _api.searchPlaces(
        widget.session,
        mode: 'nearby',
        latitude: point.latitude,
        longitude: point.longitude,
      );
      if (!mounted) return;
      final selected = await _choosePlace(
        candidates,
        title: '选择附近地点',
        center: point,
      );
      if (selected != null) {
        setState(
          () => _location = EventLocationModel(
            name: selected.name,
            address: selected.address,
            province: selected.province,
            city: selected.city,
            district: selected.district,
            adCode: selected.adCode,
            providerPoiId: selected.providerPoiId,
            poiType: selected.poiType,
            latitude: selected.latitude,
            longitude: selected.longitude,
            accuracyMeters: null,
            coordinateSystem: 'GCJ02',
            source: 2,
            capturedAt: DateTime.now(),
          ),
        );
      }
    } catch (e) {
      if (mounted) setState(() => _error = '地点选择失败：$e');
    } finally {
      if (mounted) setState(() => _locating = false);
    }
  }

  Future<PlaceCandidateModel?> _choosePlace(
    List<PlaceCandidateModel> places, {
    required String title,
    required MapPoint center,
  }) => showModalBottomSheet<PlaceCandidateModel>(
    context: context,
    isScrollControlled: true,
    backgroundColor: context.traceColors.surface,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
    ),
    builder: (context) => NearbyPlaceSheet(
      title: title,
      initialPlaces: places,
      center: center,
      onSearch: (query) => _api.searchPlaces(
        widget.session,
        mode: 'nearby',
        query: query,
        latitude: center.latitude,
        longitude: center.longitude,
      ),
    ),
  );

  void _addCustomTag() {
    final value = _customTag.text.trim();
    if (value.isEmpty) return;
    if (value.characters.length > 24) {
      setState(() => _error = '自定义标签最多 24 个字符。');
      return;
    }
    if (_manualTags.length >= 10) {
      setState(() => _error = '最多 10 个行为标签。');
      return;
    }
    if (!_manualTags.any(
      (x) => (x.name ?? '').toLowerCase() == value.toLowerCase(),
    )) {
      setState(() => _manualTags.add(ManualTagModel(name: value)));
    }
    _customTag.clear();
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Scaffold(
      appBar: TraceAppBar(
        title: _isEdit ? '编辑记录' : '记一笔',
        leading: TraceIconButton(
          glyph: TraceGlyph.chevronLeft,
          tooltip: '返回',
          onPressed: _submitting ? null : () => Navigator.of(context).pop(),
        ),
      ),
      bottomNavigationBar: TracePrimaryActionBar(
        label: _isEdit ? '保存修改' : '创建记录',
        loading: _submitting,
        onPressed: _submitting ? null : _submit,
      ),
      body: _loading
          ? Center(child: CircularProgressIndicator(color: colors.primary))
          : _buildForm(),
    );
  }

  Widget _buildForm() {
    return Form(
      key: _formKey,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(18, 16, 18, 32),
        children: [
          _EventKindSelector(
            value: _kind,
            enabled: !_isEdit,
            onChanged: (value) => setState(() => _kind = value),
          ),
          const SizedBox(height: 22),
          const TraceFieldLabel('标题（可选）'),
          TextFormField(
            controller: _title,
            maxLength: 200,
            decoration: _fieldDecoration('用一句话概括这条记录'),
            validator: (_) {
              if (_title.text.trim().isEmpty &&
                  _content.text.trim().isEmpty &&
                  _media.where((item) => item.id != null).isEmpty) {
                return '标题、正文与附件至少需要一项。';
              }
              return null;
            },
            textInputAction: TextInputAction.next,
          ),
          const SizedBox(height: 18),
          const TraceFieldLabel('正文（可选）'),
          TextFormField(
            controller: _content,
            minLines: 7,
            maxLines: 14,
            textAlignVertical: TextAlignVertical.top,
            decoration: _fieldDecoration('把当下想到的、看到的、吃到的写下来…'),
            validator: (_) {
              if (_title.text.trim().isEmpty &&
                  _content.text.trim().isEmpty &&
                  _media.where((item) => item.id != null).isEmpty) {
                return '标题、正文与附件至少需要一项。';
              }
              return null;
            },
            textInputAction: TextInputAction.newline,
          ),
          const SizedBox(height: 20),
          TraceFieldLabel(_kind == EventKind.plan ? '预定时间（可选）' : '发生时间（可选）'),
          TraceRowButton(
            glyph: TraceGlyph.calendar,
            title: _when.text.isEmpty ? '选择日期和时间' : _when.text,
            subtitle: _when.text.isEmpty ? '可以精确到某一天的某个时刻' : '点击可重新选择',
            onTap: _submitting ? null : _pickWhen,
          ),
          if (_when.text.isNotEmpty)
            Align(
              alignment: Alignment.centerRight,
              child: TextButton(
                onPressed: _submitting ? null : () => setState(_when.clear),
                child: const Text('清除时间'),
              ),
            ),
          const SizedBox(height: 14),
          _buildLocationSection(),
          const SizedBox(height: 22),
          Row(
            children: [
              Expanded(
                child: Text(
                  '附件  ${_media.length}/10',
                  style: TextStyle(
                    color: context.traceColors.inkSecondary,
                    fontWeight: FontWeight.w700,
                    fontSize: 12,
                  ),
                ),
              ),
              TextButton(
                onPressed: _submitting || _media.length >= 10
                    ? null
                    : _pickMedia,
                style: TextButton.styleFrom(
                  minimumSize: const Size(48, 48),
                  foregroundColor: context.traceColors.primary,
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    TraceIcon(
                      TraceGlyph.paperclip,
                      size: 18,
                      color: context.traceColors.primary,
                    ),
                    const SizedBox(width: 6),
                    const Text('选择附件'),
                  ],
                ),
              ),
            ],
          ),
          if (_media.isNotEmpty) ...[
            const SizedBox(height: 8),
            for (var index = 0; index < _media.length; index++)
              _buildMediaRow(_media[index], index),
          ],
          const SizedBox(height: 18),
          _buildClassificationSection(),
          if (_error != null) ...[
            const SizedBox(height: 14),
            Text(
              _error!,
              style: TextStyle(
                color: Theme.of(context).colorScheme.error,
                fontSize: 12,
              ),
            ),
          ],
          const SizedBox(height: 8),
        ],
      ),
    );
  }

  Widget _buildClassificationSection() {
    final colors = context.traceColors;
    final summary = _primaryCategoryKey == null && _manualTags.isEmpty
        ? '不填写则由 AI 自动整理'
        : '${_categoryLabel()} · ${_manualTags.length} 个标签';
    return DecoratedBox(
      decoration: BoxDecoration(
        border: Border.symmetric(horizontal: BorderSide(color: colors.line)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Material(
            color: Colors.transparent,
            child: InkWell(
              onTap: () => setState(
                () => _classificationExpanded = !_classificationExpanded,
              ),
              child: ConstrainedBox(
                constraints: const BoxConstraints(minHeight: 64),
                child: Row(
                  children: [
                    Expanded(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '分类与标签（可选）',
                            style: TextStyle(
                              color: colors.ink,
                              fontSize: 13,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          const SizedBox(height: 3),
                          Text(
                            summary,
                            style: TextStyle(
                              color: colors.inkTertiary,
                              fontSize: 11,
                            ),
                          ),
                        ],
                      ),
                    ),
                    TraceIcon(
                      _classificationExpanded
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
          if (_classificationExpanded)
            Padding(
              padding: const EdgeInsets.only(bottom: 16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  if (_taxonomy != null) ...[
                    const TraceFieldLabel('主分类'),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: _taxonomy!.categories.map((item) {
                        final selected = _primaryCategoryKey == item.key;
                        return _SelectableTag(
                          label: item.label,
                          selected: selected,
                          onTap: () => setState(
                            () => _primaryCategoryKey = selected
                                ? null
                                : item.key,
                          ),
                        );
                      }).toList(),
                    ),
                    const SizedBox(height: 18),
                    const TraceFieldLabel('行为标签'),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: _taxonomy!.behaviorTags.map((item) {
                        final selected = _manualTags.any(
                          (tag) => tag.taxonomyKey == item.key,
                        );
                        return _SelectableTag(
                          label: item.label,
                          selected: selected,
                          onTap: () => setState(() {
                            if (selected) {
                              _manualTags.removeWhere(
                                (tag) => tag.taxonomyKey == item.key,
                              );
                            } else if (_manualTags.length < 10) {
                              _manualTags.add(
                                ManualTagModel(taxonomyKey: item.key),
                              );
                            }
                          }),
                        );
                      }).toList(),
                    ),
                  ],
                  const SizedBox(height: 14),
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        child: TextField(
                          controller: _customTag,
                          maxLength: 24,
                          decoration: _fieldDecoration('输入自定义标签'),
                          onSubmitted: (_) => _addCustomTag(),
                        ),
                      ),
                      const SizedBox(width: 8),
                      TraceIconButton(
                        glyph: TraceGlyph.add,
                        tooltip: '添加标签',
                        onPressed: _addCustomTag,
                        backgroundColor: colors.primarySoft,
                        color: colors.primaryStrong,
                      ),
                    ],
                  ),
                  if (_manualTags.any((tag) => tag.name != null)) ...[
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: _manualTags
                          .where((tag) => tag.name != null)
                          .map(
                            (tag) => _SelectableTag(
                              label: tag.name!,
                              selected: true,
                              removable: true,
                              onTap: () =>
                                  setState(() => _manualTags.remove(tag)),
                            ),
                          )
                          .toList(),
                    ),
                  ],
                ],
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildLocationSection() {
    final colors = context.traceColors;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const TraceFieldLabel('地点（可选）'),
        if (_location == null)
          TraceRowButton(
            glyph: TraceGlyph.mapPin,
            title: _locating ? '正在获取位置…' : '选择地点',
            subtitle: '拖动定位点，再从附近地点中选择',
            onTap: _locating ? null : _pickLocation,
          )
        else
          Material(
            color: colors.surface,
            shape: RoundedRectangleBorder(
              side: BorderSide(color: colors.line),
              borderRadius: BorderRadius.circular(12),
            ),
            clipBehavior: Clip.antiAlias,
            child: Row(
              children: [
                Expanded(
                  child: InkWell(
                    onTap: _locating ? null : _pickLocation,
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(minHeight: 64),
                      child: Padding(
                        padding: const EdgeInsets.fromLTRB(10, 8, 6, 8),
                        child: Row(
                          children: [
                            Container(
                              width: 36,
                              height: 36,
                              decoration: BoxDecoration(
                                color: colors.primarySoft,
                                borderRadius: BorderRadius.circular(10),
                              ),
                              child: Center(
                                child: TraceIcon(
                                  TraceGlyph.mapPin,
                                  size: 18,
                                  color: colors.primaryStrong,
                                ),
                              ),
                            ),
                            const SizedBox(width: 10),
                            Expanded(
                              child: Column(
                                mainAxisAlignment: MainAxisAlignment.center,
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    _location!.name,
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
                                    _location!.address ?? '仅保存地点名称',
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                    style: TextStyle(
                                      color: colors.inkTertiary,
                                      fontSize: 11,
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
                ),
                TraceIconButton(
                  glyph: TraceGlyph.close,
                  tooltip: '清除地点',
                  color: colors.inkMuted,
                  onPressed: () => setState(() => _location = null),
                ),
              ],
            ),
          ),
      ],
    );
  }

  Widget _buildMediaRow(_FormMediaItem item, int index) => Container(
    margin: const EdgeInsets.only(bottom: 8),
    decoration: BoxDecoration(
      color: context.traceColors.surface,
      border: Border.all(color: context.traceColors.line),
      borderRadius: BorderRadius.circular(12),
    ),
    child: Padding(
      padding: const EdgeInsets.fromLTRB(12, 8, 4, 8),
      child: Row(
        children: [
          TraceIcon(
            item.kind == MediaKind.image
                ? TraceGlyph.image
                : item.kind == MediaKind.video
                ? TraceGlyph.video
                : TraceGlyph.file,
            color: context.traceColors.primaryStrong,
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(item.name, maxLines: 1, overflow: TextOverflow.ellipsis),
                if (item.uploading)
                  LinearProgressIndicator(value: item.progress)
                else if (item.error != null)
                  Text(
                    item.error!,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                      fontSize: 11,
                    ),
                  )
                else
                  Text(
                    '已上传',
                    style: TextStyle(
                      fontSize: 11,
                      color: context.traceColors.inkTertiary,
                    ),
                  ),
              ],
            ),
          ),
          if (item.error != null)
            TraceIconButton(
              glyph: TraceGlyph.refresh,
              tooltip: '重试',
              onPressed: () => _upload(item),
            ),
          TraceIconButton(
            glyph: TraceGlyph.chevronUp,
            tooltip: '上移',
            onPressed: index == 0 ? null : () => _moveMedia(index, -1),
          ),
          TraceIconButton(
            glyph: TraceGlyph.chevronDown,
            tooltip: '下移',
            onPressed: index == _media.length - 1
                ? null
                : () => _moveMedia(index, 1),
          ),
          TraceIconButton(
            glyph: TraceGlyph.close,
            tooltip: '移除',
            onPressed: item.uploading ? null : () => _removeMedia(index),
          ),
        ],
      ),
    ),
  );

  InputDecoration _fieldDecoration(String hint) =>
      InputDecoration(hintText: hint, counterText: '');
}

class _SelectableTag extends StatelessWidget {
  const _SelectableTag({
    required this.label,
    required this.selected,
    required this.onTap,
    this.removable = false,
  });

  final String label;
  final bool selected;
  final VoidCallback onTap;
  final bool removable;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Material(
      color: selected ? colors.primarySoft : colors.surface,
      borderRadius: BorderRadius.circular(99),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(99),
        child: Container(
          constraints: const BoxConstraints(minHeight: 36),
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
          decoration: BoxDecoration(
            border: Border.all(
              color: selected ? colors.primary : colors.lineStrong,
            ),
            borderRadius: BorderRadius.circular(99),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                label,
                style: TextStyle(
                  color: selected ? colors.primaryStrong : colors.inkSecondary,
                  fontSize: 12,
                  fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
                ),
              ),
              if (removable) ...[
                const SizedBox(width: 5),
                TraceIcon(
                  TraceGlyph.close,
                  size: 13,
                  color: colors.primaryStrong,
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _EventKindSelector extends StatelessWidget {
  const _EventKindSelector({
    required this.value,
    required this.enabled,
    required this.onChanged,
  });

  final EventKind value;
  final bool enabled;
  final ValueChanged<EventKind> onChanged;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
        border: Border(bottom: BorderSide(color: context.traceColors.line)),
      ),
      child: Row(
        children: [
          _item(context, EventKind.trace, '记录当下'),
          _item(context, EventKind.plan, '写下计划'),
        ],
      ),
    );
  }

  Widget _item(BuildContext context, EventKind kind, String label) {
    final selected = value == kind;
    final color = selected
        ? Theme.of(context).colorScheme.primary
        : enabled
        ? context.traceColors.inkSecondary
        : context.traceColors.inkTertiary;
    return Expanded(
      child: InkWell(
        onTap: enabled ? () => onChanged(kind) : null,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 160),
          padding: const EdgeInsets.symmetric(vertical: 12),
          decoration: BoxDecoration(
            border: Border(
              bottom: BorderSide(
                color: selected ? color : Colors.transparent,
                width: 2,
              ),
            ),
          ),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Text(
                label,
                style: TextStyle(
                  color: color,
                  fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _FormMediaItem {
  _FormMediaItem({
    required this.name,
    this.id,
    this.kind = MediaKind.file,
    this.source,
    this.progress = 0,
  });

  String? id;
  final String name;
  MediaKind kind;
  final PlatformFile? source;
  double progress;
  bool uploading = false;
  String? error;
}
