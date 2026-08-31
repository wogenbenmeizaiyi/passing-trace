import 'package:flutter/services.dart';

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
