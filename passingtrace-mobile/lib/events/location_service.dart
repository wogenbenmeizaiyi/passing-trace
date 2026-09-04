import 'package:flutter/services.dart';

String friendlyLocationError(Object error) {
  if (error is PlatformException) {
    final detail = '${error.message ?? ''} ${error.details ?? ''}';
    if (error.code == 'AMAP_8' ||
        detail.contains('INVALID_USER_SCODE') ||
        detail.contains('SHA1AndPackage')) {
      return '当前安装包与地图服务配置不匹配。请安装最新版“星期八”后重试。';
    }
    if (error.code == 'PERMISSION_DENIED') return '未授予前台定位权限。';
    if (error.code == 'PRIVACY_REQUIRED') return '需要先同意位置隐私说明。';
    if (error.code == 'EMULATOR_LOCATION_UNAVAILABLE') {
      return '模拟器尚未设置虚拟位置。';
    }
    return error.message?.trim().isNotEmpty == true
        ? error.message!.trim()
        : '地图服务暂时不可用，请稍后重试。';
  }
  if (error is StateError) return error.message;
  return '地图服务暂时不可用，请稍后重试。';
}

class DeviceLocation {
  const DeviceLocation({
    required this.latitude,
    required this.longitude,
    required this.accuracyMeters,
    required this.capturedAt,
  });
  final double latitude;
  final double longitude;
  final double accuracyMeters;
  final DateTime capturedAt;
}

class MapPoint {
  const MapPoint({required this.latitude, required this.longitude});

  final double latitude;
  final double longitude;
}

class AmapLocationService {
  static const _channel = MethodChannel('passingtrace/amap_location');

  Future<bool> requestPermission() async =>
      await _channel.invokeMethod<bool>('requestPermission') ?? false;

  Future<DeviceLocation> locateOnce({required bool privacyAccepted}) async {
    final raw = await _channel.invokeMapMethod<String, dynamic>('locateOnce', {
      'privacyAccepted': privacyAccepted,
    });
    if (raw == null) throw StateError('高德定位没有返回结果。');
    return DeviceLocation(
      latitude: (raw['latitude'] as num).toDouble(),
      longitude: (raw['longitude'] as num).toDouble(),
      accuracyMeters: (raw['accuracyMeters'] as num).toDouble(),
      capturedAt: DateTime.fromMillisecondsSinceEpoch(
        (raw['capturedAt'] as num).toInt(),
        isUtc: true,
      ),
    );
  }

  Future<MapPoint?> pickMapPoint({
    required double latitude,
    required double longitude,
    required bool privacyAccepted,
  }) async {
    final raw = await _channel.invokeMapMethod<String, dynamic>(
      'pickMapPoint',
      {
        'latitude': latitude,
        'longitude': longitude,
        'privacyAccepted': privacyAccepted,
      },
    );
    if (raw == null) return null;
    return MapPoint(
      latitude: (raw['latitude'] as num).toDouble(),
      longitude: (raw['longitude'] as num).toDouble(),
    );
  }

  Future<void> dispose() => _channel.invokeMethod<void>('dispose');
}
