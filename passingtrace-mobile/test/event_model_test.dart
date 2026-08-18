import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/events/event_model.dart';

void main() {
  group('EventKind', () {
    test('数值与文案', () {
      expect(EventKind.trace.value, 0);
      expect(EventKind.plan.value, 1);
      expect(EventKind.trace.label, '痕迹');
      expect(EventKind.plan.label, '计划');
    });

    test('fromValue 正常解析', () {
      expect(EventKind.fromValue(0), EventKind.trace);
      expect(EventKind.fromValue(1), EventKind.plan);
    });

    test('fromValue 非法值抛错', () {
      expect(() => EventKind.fromValue(99), throwsFormatException);
    });
  });

  group('EventStatus', () {
    test('数值与文案', () {
      expect(EventStatus.planned.value, 0);
      expect(EventStatus.completed.value, 1);
      expect(EventStatus.cancelled.value, 2);
      expect(EventStatus.planned.label, '待执行');
      expect(EventStatus.completed.label, '已完成');
      expect(EventStatus.cancelled.label, '已取消');
    });

    test('fromValue 正常解析', () {
      expect(EventStatus.fromValue(0), EventStatus.planned);
      expect(EventStatus.fromValue(1), EventStatus.completed);
      expect(EventStatus.fromValue(2), EventStatus.cancelled);
    });

    test('fromValue 非法值抛错', () {
      expect(() => EventStatus.fromValue(-1), throwsFormatException);
    });
  });

  group('EventModel.fromJson', () {
    test('完整字段解析', () {
      final json = {
        'id': 42,
        'kind': 0,
        'status': 1,
        'title': '涩谷烤肉',
        'rawContent': '今天和朋友去涩谷吃烤肉。',
        'happenedAt': '2026-08-18T19:30:00+09:00',
        'plannedAt': null,
        'completedAt': null,
        'timezone': 'Asia/Tokyo',
        'visibility': 0,
        'sourceRevision': 1,
        'version': 1284,
        'createdAt': '2026-08-18T19:32:10+09:00',
        'updatedAt': '2026-08-18T19:32:10+09:00',
      };
      final event = EventModel.fromJson(json);
      expect(event.id, 42);
      expect(event.kind, EventKind.trace);
      expect(event.status, EventStatus.completed);
      expect(event.title, '涩谷烤肉');
      expect(event.rawContent, '今天和朋友去涩谷吃烤肉。');
      expect(event.happenedAt, DateTime.parse('2026-08-18T19:30:00+09:00'));
      expect(event.plannedAt, isNull);
      expect(event.timezone, 'Asia/Tokyo');
      expect(event.sourceRevision, 1);
      expect(event.version, 1284);
    });

    test('可选字段为 null 时按 null 处理', () {
      final event = EventModel.fromJson({
        'id': 1,
        'kind': 1,
        'status': 0,
        'title': null,
        'rawContent': null,
        'happenedAt': null,
        'plannedAt': null,
        'completedAt': null,
        'timezone': 'UTC',
        'visibility': 0,
        'sourceRevision': 0,
        'version': 1,
        'createdAt': '2026-08-18T10:00:00+00:00',
        'updatedAt': '2026-08-18T10:00:00+00:00',
      });
      expect(event.title, isNull);
      expect(event.rawContent, isNull);
      expect(event.plannedAt, isNull);
      expect(event.happenedAt, isNull);
    });
  });

  group('EventPage.fromJson', () {
    test('带 nextCursor', () {
      final page = EventPage.fromJson({
        'items': [
          {
            'id': 2,
            'kind': 0,
            'status': 1,
            'title': null,
            'rawContent': 'a',
            'happenedAt': null,
            'plannedAt': null,
            'completedAt': null,
            'timezone': 'UTC',
            'visibility': 0,
            'sourceRevision': 0,
            'version': 1,
            'createdAt': '2026-08-18T10:00:00+00:00',
            'updatedAt': '2026-08-18T10:00:00+00:00',
          },
        ],
        'nextCursor': 1,
      });
      expect(page.items.length, 1);
      expect(page.nextCursor, 1);
    });

    test('nextCursor 为 null 时不报错', () {
      final page = EventPage.fromJson({
        'items': <Map<String, dynamic>>[],
        'nextCursor': null,
      });
      expect(page.items, isEmpty);
      expect(page.nextCursor, isNull);
    });
  });

  group('ProblemDetails.fromJson', () {
    test('基本字段', () {
      final p = ProblemDetails.fromJson({
        'status': 409,
        'title': '资源不存在',
        'detail': '未找到用户 1 的事件 42。',
      });
      expect(p.status, 409);
      expect(p.title, '资源不存在');
      expect(p.detail, '未找到用户 1 的事件 42。');
    });
  });
}
