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
      helpText: _kind == EventKind.plan ? '选择计划日期' : '选择发生日期',
      cancelText: '取消',
      confirmText: '下一步',
    );
    if (date == null || !mounted) return;

    final time = await showTimePicker(
      context: context,
      initialTime: TimeOfDay.fromDateTime(initial),
      helpText: _kind == EventKind.plan ? '选择计划时间' : '选择发生时间',
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
          'PassingTrace 将获取一次前台位置并打开高德地图。你可以拖动地图选点，再从附近地点中选择。不会后台定位或保存轨迹。',
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
    showDragHandle: true,
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
    return Scaffold(
      appBar: AppBar(
        title: Text(
          _isEdit ? '编辑记录' : '记一笔',
          style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 19),
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
          _EventKindSelector(
            value: _kind,
            enabled: !_isEdit,
            onChanged: (value) => setState(() => _kind = value),
          ),
          const SizedBox(height: 18),
          TextFormField(
            controller: _title,
            maxLength: 200,
            decoration: _decoration('标题', '一句话标题（可与正文同时为空则非法）'),
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
          const SizedBox(height: 6),
          TextFormField(
            controller: _content,
            minLines: 5,
            maxLines: 12,
            textAlignVertical: TextAlignVertical.top,
            decoration: _decoration(
              '正文',
              '把当下想到的、看到的、吃到的写下来…',
            ).copyWith(alignLabelWithHint: true),
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
          const SizedBox(height: 18),
          TextFormField(
            controller: _when,
            readOnly: true,
            onTap: _submitting ? null : _pickWhen,
            decoration:
                _decoration(
                  _kind == EventKind.plan ? '计划时间' : '发生时间',
                  '点击选择日期和时间',
                ).copyWith(
                  suffixIcon: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      if (_when.text.isNotEmpty)
                        IconButton(
                          tooltip: '清除时间',
                          onPressed: _submitting
                              ? null
                              : () => setState(_when.clear),
                          icon: const Icon(Icons.close),
                        ),
                      const Padding(
                        padding: EdgeInsets.only(right: 12),
                        child: Icon(Icons.calendar_month_outlined),
                      ),
                    ],
                  ),
                ),
            validator: (value) {
              if (value == null || value.trim().isEmpty) return null;
              return _parseWhen() == null ? '时间格式不正确。' : null;
            },
          ),
          const SizedBox(height: 12),
          _buildLocationSection(),
          const SizedBox(height: 8),
          ExpansionTile(
            tilePadding: EdgeInsets.zero,
            title: const Text(
              '分类与标签（可选）',
              style: TextStyle(fontWeight: FontWeight.w600),
            ),
            subtitle: Text(
              _primaryCategoryKey == null && _manualTags.isEmpty
                  ? '不填写则由 AI 自动整理'
                  : '${_categoryLabel()} · ${_manualTags.length} 个标签',
            ),
            children: [
              if (_taxonomy != null) ...[
                const Align(
                  alignment: Alignment.centerLeft,
                  child: Text(
                    '主分类',
                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.bold),
                  ),
                ),
                const SizedBox(height: 8),
                Wrap(
                  spacing: 6,
                  runSpacing: 6,
                  children: _taxonomy!.categories
                      .map(
                        (item) => ChoiceChip(
                          label: Text(item.label),
                          selected: _primaryCategoryKey == item.key,
                          onSelected: (selected) => setState(
                            () => _primaryCategoryKey = selected
                                ? item.key
                                : null,
                          ),
                        ),
                      )
                      .toList(),
                ),
                const SizedBox(height: 14),
                const Align(
                  alignment: Alignment.centerLeft,
                  child: Text(
                    '行为标签',
                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.bold),
                  ),
                ),
                const SizedBox(height: 8),
                Wrap(
                  spacing: 6,
                  runSpacing: 6,
                  children: _taxonomy!.behaviorTags.map((item) {
                    final selected = _manualTags.any(
                      (x) => x.taxonomyKey == item.key,
                    );
                    return FilterChip(
                      label: Text(item.label),
                      selected: selected,
                      onSelected: (value) => setState(() {
                        if (value && _manualTags.length < 10) {
                          _manualTags.add(
                            ManualTagModel(taxonomyKey: item.key),
                          );
                        }
                        if (!value) {
                          _manualTags.removeWhere(
                            (x) => x.taxonomyKey == item.key,
                          );
                        }
                      }),
                    );
                  }).toList(),
                ),
              ],
              const SizedBox(height: 10),
              Row(
                children: [
                  Expanded(
                    child: TextField(
                      controller: _customTag,
                      maxLength: 24,
                      decoration: const InputDecoration(
                        labelText: '自定义标签',
                        counterText: '',
                        isDense: true,
                      ),
                    ),
                  ),
                  IconButton(
                    tooltip: '添加标签',
                    onPressed: _addCustomTag,
                    icon: const Icon(Icons.add_circle_outline),
                  ),
                ],
              ),
              if (_manualTags.any((x) => x.name != null))
                Wrap(
                  spacing: 6,
                  children: _manualTags
                      .where((x) => x.name != null)
                      .map(
                        (x) => InputChip(
                          label: Text(x.name!),
                          onDeleted: () =>
                              setState(() => _manualTags.remove(x)),
                        ),
                      )
                      .toList(),
                ),
              const SizedBox(height: 8),
            ],
          ),
          const SizedBox(height: 18),
          Row(
            children: [
              const Expanded(
                child: Text(
                  '附件',
                  style: TextStyle(fontWeight: FontWeight.w700, fontSize: 15),
                ),
              ),
              Text('${_media.length}/10'),
              const SizedBox(width: 8),
              OutlinedButton.icon(
                onPressed: _submitting || _media.length >= 10
                    ? null
                    : _pickMedia,
                icon: const Icon(Icons.attach_file, size: 18),
                label: const Text('选择'),
              ),
            ],
          ),
          if (_media.isNotEmpty) ...[
            const SizedBox(height: 8),
            for (var index = 0; index < _media.length; index++)
              _buildMediaRow(_media[index], index),
          ],
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
          const SizedBox(height: 24),
          Row(
            children: [
              Expanded(
                child: OutlinedButton(
                  onPressed: _submitting
                      ? null
                      : () => Navigator.of(context).pop(false),
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
                  ),
                  child: _submitting
                      ? SizedBox.square(
                          dimension: 18,
                          child: CircularProgressIndicator(
                            color: Theme.of(context).colorScheme.onPrimary,
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

  Widget _buildLocationSection() => Column(
    children: [
      Row(
        children: [
          const Expanded(
            child: Text(
              '地点（可选）',
              style: TextStyle(fontWeight: FontWeight.w700, fontSize: 15),
            ),
          ),
          TextButton.icon(
            onPressed: _locating ? null : _pickLocation,
            icon: _locating
                ? const SizedBox.square(
                    dimension: 14,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.map_outlined, size: 17),
            label: Text(_location == null ? '选择地点' : '重新选择'),
          ),
        ],
      ),
      if (_location != null)
        Card(
          elevation: 0,
          child: ListTile(
            dense: true,
            leading: const Icon(Icons.place_outlined),
            title: Text(_location!.name),
            subtitle: Text(_location!.address ?? '仅保存地点名称'),
            trailing: IconButton(
              tooltip: '清除地点',
              onPressed: () => setState(() => _location = null),
              icon: const Icon(Icons.close),
            ),
          ),
        ),
    ],
  );

  Widget _buildMediaRow(_FormMediaItem item, int index) => Card(
    elevation: 0,
    color: context.traceColors.surfaceSoft,
    child: Padding(
      padding: const EdgeInsets.fromLTRB(12, 8, 4, 8),
      child: Row(
        children: [
          Icon(
            item.kind == MediaKind.image
                ? Icons.image_outlined
                : item.kind == MediaKind.video
                ? Icons.movie_outlined
                : Icons.description_outlined,
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
            IconButton(
              tooltip: '重试',
              onPressed: () => _upload(item),
              icon: const Icon(Icons.refresh),
            ),
          IconButton(
            tooltip: '上移',
            onPressed: index == 0 ? null : () => _moveMedia(index, -1),
            icon: const Icon(Icons.keyboard_arrow_up),
          ),
          IconButton(
            tooltip: '下移',
            onPressed: index == _media.length - 1
                ? null
                : () => _moveMedia(index, 1),
            icon: const Icon(Icons.keyboard_arrow_down),
          ),
          IconButton(
            tooltip: '移除',
            onPressed: item.uploading ? null : () => _removeMedia(index),
            icon: const Icon(Icons.close),
          ),
        ],
      ),
    ),
  );

  InputDecoration _decoration(String label, String? hint) => InputDecoration(
    labelText: label,
    hintText: hint,
    counterText: label == '标题' ? null : '',
  );
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
          _item(context, EventKind.trace, '痕迹', Icons.history),
          _item(context, EventKind.plan, '计划', Icons.flag_outlined),
        ],
      ),
    );
  }

  Widget _item(
    BuildContext context,
    EventKind kind,
    String label,
    IconData icon,
  ) {
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
              Icon(icon, size: 18, color: color),
              const SizedBox(width: 7),
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
