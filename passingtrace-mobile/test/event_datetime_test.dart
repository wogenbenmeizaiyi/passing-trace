import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/events/event_datetime.dart';

void main() {
  group('offsetForTimezone', () {
    test('UTC 始终 +00:00', () {
      expect(
        offsetForTimezone(DateTime.utc(2026, 8, 18, 12, 0), 'UTC'),
        '+00:00',
      );
    });

    test('Asia/Tokyo 恒为 +09:00', () {
      expect(
        offsetForTimezone(DateTime.utc(2026, 1, 15, 12, 0), 'Asia/Tokyo'),
        '+09:00',
      );
      expect(
        offsetForTimezone(DateTime.utc(2026, 7, 15, 12, 0), 'Asia/Tokyo'),
        '+09:00',
      );
    });

    test('Asia/Shanghai 恒为 +08:00', () {
      expect(
        offsetForTimezone(DateTime.utc(2026, 8, 18, 12, 0), 'Asia/Shanghai'),
        '+08:00',
      );
    });

    test('America/New_York 冬令时 -05:00、夏令时 -04:00', () {
      expect(
        offsetForTimezone(DateTime.utc(2026, 1, 15, 12, 0), 'America/New_York'),
        '-05:00',
      );
      expect(
        offsetForTimezone(DateTime.utc(2026, 7, 15, 12, 0), 'America/New_York'),
        '-04:00',
      );
    });

    test('Europe/London 夏令时切换', () {
      // 1 月：GMT
      expect(
        offsetForTimezone(DateTime.utc(2026, 1, 15, 12, 0), 'Europe/London'),
        '+00:00',
      );
      // 7 月：BST
      expect(
        offsetForTimezone(DateTime.utc(2026, 7, 15, 12, 0), 'Europe/London'),
        '+01:00',
      );
    });

    test('未识别的时区回落到本机偏移', () {
      // 这里只断言返回值是合法偏移字符串，不锁死具体值。
      final result = offsetForTimezone(
        DateTime.utc(2026, 8, 18, 12, 0),
        'Mars/Olympus',
      );
      expect(result, matches(RegExp(r'^[+-]\d{2}:\d{2}$')));
    });
  });

  group('toIsoWithOffset', () {
    test('东京 +09:00', () {
      expect(
        toIsoWithOffset('2026-08-18T19:30', 'Asia/Tokyo'),
        '2026-08-18T19:30:00+09:00',
      );
    });

    test('上海 +08:00', () {
      expect(
        toIsoWithOffset('2026-08-18 19:30', 'Asia/Shanghai'),
        '2026-08-18T19:30:00+08:00',
      );
    });

    test('纽约冬令时 -05:00', () {
      expect(
        toIsoWithOffset('2026-01-15T09:00', 'America/New_York'),
        '2026-01-15T09:00:00-05:00',
      );
    });

    test('非法输入返回 null', () {
      expect(toIsoWithOffset('', 'Asia/Tokyo'), isNull);
      expect(toIsoWithOffset('not-a-time', 'Asia/Tokyo'), isNull);
    });
  });

  group('toWallClockLocal / formatLocal', () {
    test('null / 空值占位', () {
      expect(toWallClockLocal(null), '');
      expect(formatLocal(null), '—');
      expect(formatLocalDate(null), '—');
    });

    test('formatLocal 输出可读本地时间', () {
      final iso = DateTime.utc(2026, 8, 18, 10, 30);
      final out = formatLocal(iso);
      expect(out, matches(RegExp(r'^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$')));
      // 解析回 DateTime 应该是同一个 instant
      final parsed = DateTime.parse(out.replaceFirst(' ', 'T'));
      expect(parsed.toUtc().toIso8601String(), iso.toIso8601String());
    });

    test('toWallClockLocal 使用用户可读的空格分隔', () {
      final out = toWallClockLocal(DateTime.utc(2026, 8, 18, 10, 30));
      expect(out, matches(RegExp(r'^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$')));
    });
  });
}
