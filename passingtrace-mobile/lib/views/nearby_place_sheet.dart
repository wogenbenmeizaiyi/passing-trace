import 'dart:async';

import 'package:flutter/material.dart';

import '../events/event_model.dart';
import '../events/location_service.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';

typedef NearbyPlaceSearch = Future<List<PlaceCandidateModel>> Function(
  String query,
);

/// 地图选点后的附近地点选择器。
///
/// 名称搜索仍以地图中心点为圆心，避免搜到同名但距离很远的地点。
class NearbyPlaceSheet extends StatefulWidget {
  const NearbyPlaceSheet({
    super.key,
    required this.title,
    required this.initialPlaces,
    required this.center,
    required this.onSearch,
    this.debounceDuration = const Duration(milliseconds: 400),
  });

  final String title;
  final List<PlaceCandidateModel> initialPlaces;
  final MapPoint center;
  final NearbyPlaceSearch onSearch;
  final Duration debounceDuration;

  @override
  State<NearbyPlaceSheet> createState() => _NearbyPlaceSheetState();
}

class _NearbyPlaceSheetState extends State<NearbyPlaceSheet> {
  final _query = TextEditingController();
  Timer? _debounce;
  late List<PlaceCandidateModel> _places;
  bool _searching = false;
  String? _error;
  int _searchVersion = 0;

  @override
  void initState() {
    super.initState();
    _places = widget.initialPlaces;
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _query.dispose();
    super.dispose();
  }

  void _onQueryChanged(String value) {
    _debounce?.cancel();
    final query = value.trim();
    if (query.isEmpty) {
      _searchVersion++;
      setState(() {
        _places = widget.initialPlaces;
        _searching = false;
        _error = null;
      });
      return;
    }
    _debounce = Timer(widget.debounceDuration, () => _search(query));
  }

  Future<void> _search(String query) async {
    _debounce?.cancel();
    if (query.isEmpty) return;
    final version = ++_searchVersion;
    setState(() {
      _searching = true;
      _error = null;
    });
    try {
      final places = await widget.onSearch(query);
      if (!mounted || version != _searchVersion) return;
      setState(() => _places = places);
    } catch (_) {
      if (!mounted || version != _searchVersion) return;
      setState(() => _error = '搜索失败，请稍后重试');
    } finally {
      if (mounted && version == _searchVersion) {
        setState(() => _searching = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final bottomInset = MediaQuery.viewInsetsOf(context).bottom;
    final colors = context.traceColors;
    return SafeArea(
      child: AnimatedPadding(
        duration: const Duration(milliseconds: 160),
        padding: EdgeInsets.only(bottom: bottomInset),
        child: SizedBox(
          height: MediaQuery.sizeOf(context).height * 0.72,
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 10, 20, 12),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Center(
                      child: Container(
                        width: 36,
                        height: 4,
                        margin: const EdgeInsets.only(bottom: 18),
                        decoration: BoxDecoration(
                          color: colors.lineStrong,
                          borderRadius: BorderRadius.circular(2),
                        ),
                      ),
                    ),
                    Text(
                      widget.title,
                      style: TextStyle(
                        color: colors.ink,
                        fontSize: 18,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 5),
                    Text(
                      '输入名称可继续缩小地图中心附近的范围',
                      style: TextStyle(
                        color: colors.inkSecondary,
                        fontSize: 12,
                      ),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      key: const Key('nearby-place-query'),
                      controller: _query,
                      autofocus: false,
                      textInputAction: TextInputAction.search,
                      onChanged: _onQueryChanged,
                      onSubmitted: (value) => _search(value.trim()),
                      decoration: InputDecoration(
                        hintText: '输入店名、建筑或地点',
                        prefixIcon: Center(
                          child: TraceIcon(
                            TraceGlyph.search,
                            size: 19,
                            color: colors.inkMuted,
                          ),
                        ),
                        suffixIcon: _query.text.isEmpty
                            ? null
                            : TraceIconButton(
                                glyph: TraceGlyph.close,
                                tooltip: '清空',
                                onPressed: () {
                                  _query.clear();
                                  _onQueryChanged('');
                                },
                              ),
                        isDense: true,
                      ),
                    ),
                    if (_searching)
                      LinearProgressIndicator(
                        minHeight: 2,
                        color: colors.primary,
                        backgroundColor: colors.primarySoft,
                      ),
                    if (_error != null)
                      Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: Text(
                          _error!,
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
                        ),
                      ),
                  ],
                ),
              ),
              Expanded(
                child: ListView.builder(
                  padding: const EdgeInsets.fromLTRB(12, 4, 12, 24),
                  keyboardDismissBehavior:
                      ScrollViewKeyboardDismissBehavior.onDrag,
                  itemCount: _places.length + 1,
                  itemBuilder: (context, index) {
                    if (index == 0) {
                      return _PlaceRow(
                        glyph: TraceGlyph.target,
                        title: const Text('使用地图中心位置'),
                        subtitle: Text(
                          _places.isEmpty ? '没有匹配地点，直接保存这个位置' : '不选择具体商户或地点',
                        ),
                        onTap: () => Navigator.pop(
                          context,
                          PlaceCandidateModel(
                            name: '地图选点',
                            latitude: widget.center.latitude,
                            longitude: widget.center.longitude,
                          ),
                        ),
                      );
                    }
                    final place = _places[index - 1];
                    return _PlaceRow(
                      key: Key('nearby-place-result-${index - 1}'),
                      glyph: TraceGlyph.mapPin,
                      title: Text(place.name),
                      subtitle: Text(place.address ?? ''),
                      meta: place.distanceMeters == null
                          ? null
                          : '${place.distanceMeters}m',
                      onTap: () => Navigator.pop(context, place),
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
}

class _PlaceRow extends StatelessWidget {
  const _PlaceRow({
    super.key,
    required this.glyph,
    required this.title,
    required this.subtitle,
    required this.onTap,
    this.meta,
  });

  final TraceGlyph glyph;
  final Widget title;
  final Widget subtitle;
  final VoidCallback onTap;
  final String? meta;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Material(
        color: colors.surface,
        borderRadius: BorderRadius.circular(12),
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(12),
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 62),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              child: Row(
                children: [
                  TraceIcon(glyph, size: 20, color: colors.primaryStrong),
                  const SizedBox(width: 12),
                  Expanded(
                    child: DefaultTextStyle(
                      style: TextStyle(
                        color: colors.ink,
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                      ),
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          title,
                          const SizedBox(height: 3),
                          DefaultTextStyle(
                            style: TextStyle(
                              color: colors.inkMuted,
                              fontSize: 11,
                              fontWeight: FontWeight.w400,
                            ),
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            child: subtitle,
                          ),
                        ],
                      ),
                    ),
                  ),
                  if (meta != null)
                    Text(
                      meta!,
                      style: TextStyle(color: colors.inkMuted, fontSize: 11),
                    ),
                  const SizedBox(width: 4),
                  TraceIcon(
                    TraceGlyph.chevronRight,
                    size: 17,
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
}
