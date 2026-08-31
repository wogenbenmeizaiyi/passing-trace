import 'dart:async';

import 'package:flutter/material.dart';

import '../events/event_model.dart';
import '../events/location_service.dart';

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
    return SafeArea(
      child: AnimatedPadding(
        duration: const Duration(milliseconds: 160),
        padding: EdgeInsets.only(bottom: bottomInset),
        child: SizedBox(
          height: MediaQuery.sizeOf(context).height * 0.72,
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 4, 20, 12),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      widget.title,
                      style: const TextStyle(
                        fontSize: 18,
                        fontWeight: FontWeight.bold,
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
                        prefixIcon: const Icon(Icons.search),
                        suffixIcon: _query.text.isEmpty
                            ? null
                            : IconButton(
                                tooltip: '清空',
                                onPressed: () {
                                  _query.clear();
                                  _onQueryChanged('');
                                },
                                icon: const Icon(Icons.close),
                              ),
                        border: const OutlineInputBorder(),
                        isDense: true,
                      ),
                    ),
                    if (_searching) const LinearProgressIndicator(minHeight: 2),
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
                  keyboardDismissBehavior:
                      ScrollViewKeyboardDismissBehavior.onDrag,
                  itemCount: _places.length + 1,
                  itemBuilder: (context, index) {
                    if (index == 0) {
                      return ListTile(
                        leading: const Icon(Icons.my_location),
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
                    return ListTile(
                      title: Text(place.name),
                      subtitle: Text(place.address ?? ''),
                      trailing: place.distanceMeters == null
                          ? null
                          : Text('${place.distanceMeters}m'),
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
