// 与 Events API 时间约定对齐：
//   - `happenedAt` / `plannedAt` 为 ISO 8601 字符串（带偏移，例如 `2026-08-18T19:30:00+09:00`）。
//   - `timezone` 为 IANA 时区名（例如 `Asia/Tokyo`），与表单里"用户选定的时区"一致。
// 表单使用 `YYYY-MM-DDTHH:mm` 这样的"墙上时间"输入，
// 我们把它当作"用户在该时区的本地时间"来编码，附加该时区在那一刻的偏移后发送。
//
// 注意：本实现使用内置的常见 IANA 时区偏移表，不引入 `tz` 包。
// 这是 v1 的折中：默认时区（设备本地）通过 `DateTime.timeZoneOffset` 精确处理，
// 用户在表单里切换到非本地时区时通过查表得到偏移。未在表内的时区会回落到本机偏移。

/// 设备时区名（IANA），失败时回落到 `UTC`。
String defaultTimezone() {
  try {
    final name = DateTime.now().timeZoneName;
    if (name.isEmpty) return 'UTC';
    // 部分平台返回如 "China Standard Time" 这种 Windows 名而非 IANA 名；
    // 这里尝试把它映射到常见 IANA 名，找不到就返回原值。
    return _windowsNameToIana(name) ?? name;
  } catch (_) {
    return 'UTC';
  }
}

String? _windowsNameToIana(String name) {
  switch (name) {
    case 'China Standard Time':
      return 'Asia/Shanghai';
    case 'Tokyo Standard Time':
      return 'Asia/Tokyo';
    case 'Korea Standard Time':
      return 'Asia/Seoul';
    case 'Singapore Standard Time':
      return 'Asia/Singapore';
    case 'India Standard Time':
      return 'Asia/Kolkata';
    case 'Pacific Standard Time':
      return 'America/Los_Angeles';
    case 'Eastern Standard Time':
      return 'America/New_York';
    case 'GMT Standard Time':
      return 'Europe/London';
    case 'Central European Standard Time':
      return 'Europe/Berlin';
  }
  return null;
}

/// 在指定 IANA 时区下，给定一个"墙上时间"Date，输出对应偏移字符串。
String offsetForTimezone(DateTime probe, String timezone) {
  final minutes = _offsetMinutes(timezone, probe.toUtc());
  return _formatOffset(minutes);
}

/// 把 `YYYY-MM-DDTHH:mm` 形式的墙上时间 + 用户选定的 IANA 时区，
/// 转成带偏移的 ISO 8601 字符串发送给后端。
String? toIsoWithOffset(String wallClock, String timezone) {
  final parts = _parseWallClock(wallClock);
  if (parts == null) return null;
  final probe = DateTime.utc(
    parts.year,
    parts.month,
    parts.day,
    parts.hour,
    parts.minute,
  );
  final offset = offsetForTimezone(probe, timezone);
  String two(int n) => n.toString().padLeft(2, '0');
  final y = parts.year.toString().padLeft(4, '0');
  return '$y-${two(parts.month)}-${two(parts.day)}'
      'T${two(parts.hour)}:${two(parts.minute)}:00$offset';
}

/// 把后端返回的 ISO 8601 字符串转换为 `YYYY-MM-DDTHH:mm`（按本机本地时区）。
String toWallClockLocal(DateTime? value) {
  if (value == null) return '';
  final local = value.toLocal();
  String two(int n) => n.toString().padLeft(2, '0');
  return '${local.year.toString().padLeft(4, '0')}'
      '-${two(local.month)}-${two(local.day)}'
      'T${two(local.hour)}:${two(local.minute)}';
}

/// 友好展示：按本机本地时区显示日期 + 时间。空值显示占位符。
String formatLocal(DateTime? value, {String fallback = '—'}) {
  if (value == null) return fallback;
  final local = value.toLocal();
  String two(int n) => n.toString().padLeft(2, '0');
  return '${local.year}-${two(local.month)}-${two(local.day)} '
      '${two(local.hour)}:${two(local.minute)}';
}

/// 仅显示日期。
String formatLocalDate(DateTime? value, {String fallback = '—'}) {
  if (value == null) return fallback;
  final local = value.toLocal();
  String two(int n) => n.toString().padLeft(2, '0');
  return '${local.year}-${two(local.month)}-${two(local.day)}';
}

class _WallClock {
  const _WallClock(this.year, this.month, this.day, this.hour, this.minute);
  final int year;
  final int month;
  final int day;
  final int hour;
  final int minute;
}

final _wallClockRegExp = RegExp(r'^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2})?$');

_WallClock? _parseWallClock(String value) {
  if (!_wallClockRegExp.hasMatch(value)) return null;
  final parts = value.split('T');
  final date = parts[0].split('-');
  final time = parts[1].split(':');
  final year = int.tryParse(date[0]);
  final month = int.tryParse(date[1]);
  final day = int.tryParse(date[2]);
  final hour = int.tryParse(time[0]);
  final minute = int.tryParse(time[1]);
  if (year == null ||
      month == null ||
      day == null ||
      hour == null ||
      minute == null) {
    return null;
  }
  return _WallClock(year, month, day, hour, minute);
}

String _formatOffset(int totalMinutes) {
  if (totalMinutes == 0) return '+00:00';
  final sign = totalMinutes >= 0 ? '+' : '-';
  final abs = totalMinutes.abs();
  final hours = (abs ~/ 60).toString().padLeft(2, '0');
  final minutes = (abs % 60).toString().padLeft(2, '0');
  return '$sign$hours:$minutes';
}

/// 已知 IANA 时区在指定 UTC 时刻的偏移（分钟）。覆盖文档示例中的时区，
/// 其它未识别时区回落到设备本机偏移。
int _offsetMinutes(String timezone, DateTime instantUtc) {
  switch (timezone) {
    case 'UTC':
    case 'Etc/UTC':
    case 'Etc/GMT':
    case 'GMT':
      return 0;
    case 'Asia/Shanghai':
    case 'Asia/Hong_Kong':
    case 'Asia/Taipei':
    case 'Asia/Singapore':
    case 'Asia/Kuala_Lumpur':
    case 'Asia/Macau':
      return 8 * 60;
    case 'Asia/Tokyo':
    case 'Asia/Seoul':
      return 9 * 60;
    case 'Asia/Bangkok':
    case 'Asia/Jakarta':
    case 'Asia/Ho_Chi_Minh':
      return 7 * 60;
    case 'Asia/Kolkata':
    case 'Asia/Calcutta':
      return 5 * 60 + 30;
    case 'Europe/London':
      return _isDstUk(instantUtc) ? 60 : 0;
    case 'Europe/Berlin':
    case 'Europe/Paris':
    case 'Europe/Madrid':
    case 'Europe/Rome':
      return _isDstEuropean(instantUtc) ? 2 * 60 : 60;
    case 'America/New_York':
      return _isDstUs(instantUtc) ? -4 * 60 : -5 * 60;
    case 'America/Chicago':
      return _isDstUs(instantUtc) ? -5 * 60 : -6 * 60;
    case 'America/Denver':
      return _isDstUs(instantUtc) ? -6 * 60 : -7 * 60;
    case 'America/Los_Angeles':
      return _isDstUs(instantUtc) ? -7 * 60 : -8 * 60;
    case 'Australia/Sydney':
      return _isDstAet(instantUtc) ? 11 * 60 : 10 * 60;
  }
  return DateTime.now().timeZoneOffset.inMinutes;
}

bool _isDstUk(DateTime instantUtc) {
  // 英国夏令时：3 月最后一个周日 ~ 10 月最后一个周日（UTC 视角）。
  final year = instantUtc.year;
  return instantUtc.isAfter(_lastSundayUtc(year, 3, 1)) &&
      instantUtc.isBefore(_lastSundayUtc(year, 10, 1));
}

bool _isDstEuropean(DateTime instantUtc) =>
    instantUtc.month > 3 && instantUtc.month < 10;

bool _isDstUs(DateTime instantUtc) {
  // 美国夏令时：3 月第二个周日 ~ 11 月第一个周日（UTC 视角）。
  final year = instantUtc.year;
  return instantUtc.isAfter(_secondSundayUtc(year, 3)) &&
      instantUtc.isBefore(_firstSundayUtc(year, 11));
}

bool _isDstAet(DateTime instantUtc) {
  // 澳大利亚东部夏令时：10 月 ~ 4 月（南半球）。粗略按月份判断。
  final m = instantUtc.month;
  return m >= 10 || m <= 4;
}

DateTime _lastSundayUtc(int year, int month, int hour) {
  final lastDay = DateTime(year, month + 1, 0).day;
  for (var d = lastDay; d > lastDay - 7; d--) {
    final candidate = DateTime.utc(year, month, d);
    if (candidate.weekday == DateTime.sunday) {
      return DateTime.utc(year, month, d, hour);
    }
  }
  return DateTime.utc(year, month, lastDay, hour);
}

DateTime _firstSundayUtc(int year, int month) {
  for (var d = 1; d <= 7; d++) {
    final candidate = DateTime.utc(year, month, d);
    if (candidate.weekday == DateTime.sunday) return candidate;
  }
  return DateTime.utc(year, month, 1);
}

DateTime _secondSundayUtc(int year, int month) =>
    _firstSundayUtc(year, month).add(const Duration(days: 7));
