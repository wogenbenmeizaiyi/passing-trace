import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/events/location_service.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('passingtrace/amap_location');

  tearDown(() async {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, null);
  });

  test('地图选点传入初始位置并解析拖动后的中心点', () async {
    MethodCall? received;
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (call) async {
          received = call;
          return <String, dynamic>{'latitude': 30.123, 'longitude': 120.456};
        });

    final point = await AmapLocationService().pickMapPoint(
      latitude: 30.1,
      longitude: 120.2,
      privacyAccepted: true,
    );

    expect(received?.method, 'pickMapPoint');
    expect(received?.arguments, <String, dynamic>{
      'latitude': 30.1,
      'longitude': 120.2,
      'privacyAccepted': true,
    });
    expect(point?.latitude, 30.123);
    expect(point?.longitude, 120.456);
  });

  test('用户取消地图选点时返回空', () async {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(channel, (_) async => null);

    final point = await AmapLocationService().pickMapPoint(
      latitude: 30,
      longitude: 120,
      privacyAccepted: true,
    );

    expect(point, isNull);
  });

  test('高德签名不匹配时隐藏底层 SHA1 错误细节', () {
    final error = PlatformException(
      code: 'AMAP_8',
      message: 'auth fail! INVALID_USER_SCODE#SHA1AndPackage#secret-detail',
    );

    final message = friendlyLocationError(error);

    expect(message, contains('安装包与地图服务配置不匹配'));
    expect(message, isNot(contains('SHA1')));
    expect(message, isNot(contains('secret-detail')));
  });
}
