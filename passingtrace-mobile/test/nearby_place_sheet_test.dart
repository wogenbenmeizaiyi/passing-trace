import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/events/event_model.dart';
import 'package:passingtrace_mobile/events/location_service.dart';
import 'package:passingtrace_mobile/views/nearby_place_sheet.dart';

void main() {
  testWidgets('输入名称后搜索地图中心附近的精确地点', (tester) async {
    final queries = <String>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NearbyPlaceSheet(
            title: '选择附近地点',
            initialPlaces: const [],
            center: const MapPoint(latitude: 30.2, longitude: 120.1),
            debounceDuration: Duration.zero,
            onSearch: (query) async {
              queries.add(query);
              return const [
                PlaceCandidateModel(
                  name: '早阳肉包',
                  latitude: 30.201,
                  longitude: 120.101,
                ),
              ];
            },
          ),
        ),
      ),
    );

    await tester.enterText(find.byKey(const Key('nearby-place-query')), '早阳肉包');
    await tester.pump();
    await tester.pump();

    expect(queries, ['早阳肉包']);
    expect(find.widgetWithText(ListTile, '早阳肉包'), findsOneWidget);
    expect(find.text('使用地图中心位置'), findsOneWidget);
  });

  testWidgets('清空名称后恢复最初的附近地点', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NearbyPlaceSheet(
            title: '选择附近地点',
            initialPlaces: const [
              PlaceCandidateModel(
                name: '附近咖啡店',
                latitude: 30.2,
                longitude: 120.1,
              ),
            ],
            center: const MapPoint(latitude: 30.2, longitude: 120.1),
            debounceDuration: Duration.zero,
            onSearch: (_) async => const [],
          ),
        ),
      ),
    );

    final field = find.byKey(const Key('nearby-place-query'));
    await tester.enterText(field, '不存在的地点');
    await tester.pump();
    await tester.pump();
    expect(find.text('附近咖啡店'), findsNothing);

    await tester.enterText(field, '');
    await tester.pump();
    expect(find.text('附近咖啡店'), findsOneWidget);
  });
}
