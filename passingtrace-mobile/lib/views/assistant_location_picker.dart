import 'dart:async';

import 'package:flutter/material.dart';

import '../events/location_service.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_icons.dart';

class AssistantLocationPicker extends StatefulWidget {
  const AssistantLocationPicker({
    super.key,
    required this.scopeKey,
    required this.disabled,
    required this.value,
    required this.onChanged,
    required this.onBusy,
  });

  final String scopeKey;
  final bool disabled;
  final DeviceLocation? value;
  final ValueChanged<DeviceLocation?> onChanged;
  final ValueChanged<bool> onBusy;

  @override
  State<AssistantLocationPicker> createState() =>
      _AssistantLocationPickerState();
}

class _AssistantLocationPickerState extends State<AssistantLocationPicker> {
  final _service = AmapLocationService();
  bool _busy = false;
  String? _error;
  int _request = 0;
  bool _readingLocation = false;

  void _stopLocation() {
    if (_readingLocation) {
      _readingLocation = false;
      unawaited(_service.dispose().catchError((Object _) {}));
    }
  }

  @override
  void dispose() {
    ++_request;
    _stopLocation();
    super.dispose();
  }

  @override
  void didUpdateWidget(AssistantLocationPicker oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.scopeKey != oldWidget.scopeKey) {
      final request = ++_request;
      _stopLocation();
      _busy = false;
      _error = null;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted && request == _request) widget.onBusy(false);
      });
    }
  }

  void _clear() {
    ++_request;
    _stopLocation();
    setState(() {
      _busy = false;
      _error = null;
    });
    widget.onChanged(null);
    widget.onBusy(false);
  }

  Future<void> _locate() async {
    if (_busy || widget.disabled) return;
    final request = ++_request;
    setState(() {
      _busy = true;
      _error = null;
    });
    widget.onChanged(null);
    widget.onBusy(true);
    try {
      final accepted = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('使用当前位置'),
          content: const Text(
            '星期八将获取一次前台位置，仅随下一条消息发送给 AI，并用于高德地点查询和路线规划。不会后台定位或自动保存为记录。',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('同意并定位'),
            ),
          ],
        ),
      );
      if (accepted != true || !mounted || request != _request) return;
      if (!await _service.requestPermission()) {
        throw StateError('未授予前台定位权限，请在系统设置中允许，或提供出发地。');
      }
      if (!mounted || request != _request) return;
      _readingLocation = true;
      final value = await _service.locateOnce(privacyAccepted: true);
      if (!mounted || request != _request) return;
      if (!value.isFresh ||
          !value.latitude.isFinite ||
          !value.longitude.isFinite ||
          value.latitude.abs() > 90 ||
          value.longitude.abs() > 180 ||
          !value.accuracyMeters.isFinite ||
          value.accuracyMeters <= 0 ||
          value.accuracyMeters > 100000 ||
          !const ['GCJ02', 'WGS84'].contains(value.coordinateSystem)) {
        throw StateError('定位结果无效或已过期，请重新定位。');
      }
      widget.onChanged(value);
    } catch (error) {
      if (mounted && request == _request) {
        setState(() => _error = friendlyLocationError(error));
      }
    } finally {
      if (mounted && request == _request) {
        _readingLocation = false;
        setState(() => _busy = false);
        widget.onBusy(false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final value = widget.value;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              TextButton.icon(
                onPressed: widget.disabled || _busy ? null : _locate,
                style: TextButton.styleFrom(
                  minimumSize: const Size(48, 48),
                  foregroundColor: colors.primaryStrong,
                ),
                icon: TraceIcon(
                  TraceGlyph.mapPin,
                  size: 18,
                  color: colors.primaryStrong,
                ),
                label: Text(
                  _busy
                      ? '正在定位…'
                      : value == null
                      ? '使用当前位置'
                      : '重新定位',
                ),
              ),
              if (value != null || _busy)
                TextButton(
                  onPressed: widget.disabled ? null : _clear,
                  style: TextButton.styleFrom(minimumSize: const Size(48, 48)),
                  child: Text(_busy ? '取消定位' : '移除位置'),
                ),
            ],
          ),
          Semantics(
            liveRegion: true,
            child: Text(
              _error ??
                  (value == null
                      ? '获取一次位置，发送后供 AI 和高德查询使用。'
                      : '已附加当前位置，精度约 ${value.accuracyMeters.ceil()} 米。仅随下一条消息发送。'),
              style: TextStyle(
                color: _error == null ? colors.inkSecondary : colors.danger,
                fontSize: 12,
                height: 1.5,
              ),
            ),
          ),
          const SizedBox(height: 6),
        ],
      ),
    );
  }
}
