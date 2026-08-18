// Events 域模型。
//
// 与后端契约对齐：枚举在 JSON 中是数字，UI 在中英文案与数字之间互转；
// `kind` 创建后不可变，`version` 是乐观并发令牌，`sourceRevision` 是
// Source 修订版本。

/// 痕迹 / 计划。后端在 `kind` 字段中以下列数值出现。
enum EventKind {
  trace(0, '痕迹'),
  plan(1, '计划');

  const EventKind(this.value, this.label);
  final int value;
  final String label;

  static EventKind fromValue(int value) {
    for (final kind in EventKind.values) {
      if (kind.value == value) return kind;
    }
    throw FormatException('未知的 EventKind 数值：$value');
  }
}

/// 待执行 / 已完成 / 已取消。
enum EventStatus {
  planned(0, '待执行'),
  completed(1, '已完成'),
  cancelled(2, '已取消');

  const EventStatus(this.value, this.label);
  final int value;
  final String label;

  static EventStatus fromValue(int value) {
    for (final status in EventStatus.values) {
      if (status.value == value) return status;
    }
    throw FormatException('未知的 EventStatus 数值：$value');
  }
}

class EventModel {
  const EventModel({
    required this.id,
    required this.kind,
    required this.status,
    required this.title,
    required this.rawContent,
    required this.happenedAt,
    required this.plannedAt,
    required this.completedAt,
    required this.timezone,
    required this.sourceRevision,
    required this.version,
    required this.createdAt,
    required this.updatedAt,
  });

  final int id;
  final EventKind kind;
  final EventStatus status;
  final String? title;
  final String? rawContent;

  /// ISO 8601 带偏移字符串。
  final DateTime? happenedAt;
  final DateTime? plannedAt;
  final DateTime? completedAt;

  /// IANA 时区名。
  final String timezone;
  final int sourceRevision;

  /// 并发令牌，传给 `If-Match`。
  final int version;
  final DateTime createdAt;
  final DateTime updatedAt;

  EventModel copyWith({
    EventKind? kind,
    EventStatus? status,
    String? title,
    String? rawContent,
    DateTime? happenedAt,
    DateTime? plannedAt,
    DateTime? completedAt,
    String? timezone,
    int? sourceRevision,
    int? version,
    DateTime? createdAt,
    DateTime? updatedAt,
  }) => EventModel(
    id: id,
    kind: kind ?? this.kind,
    status: status ?? this.status,
    title: title ?? this.title,
    rawContent: rawContent ?? this.rawContent,
    happenedAt: happenedAt ?? this.happenedAt,
    plannedAt: plannedAt ?? this.plannedAt,
    completedAt: completedAt ?? this.completedAt,
    timezone: timezone ?? this.timezone,
    sourceRevision: sourceRevision ?? this.sourceRevision,
    version: version ?? this.version,
    createdAt: createdAt ?? this.createdAt,
    updatedAt: updatedAt ?? this.updatedAt,
  );

  factory EventModel.fromJson(Map<String, dynamic> json) => EventModel(
    id: (json['id'] as num).toInt(),
    kind: EventKind.fromValue((json['kind'] as num).toInt()),
    status: EventStatus.fromValue((json['status'] as num).toInt()),
    title: json['title'] as String?,
    rawContent: json['rawContent'] as String?,
    happenedAt: _parseDate(json['happenedAt']),
    plannedAt: _parseDate(json['plannedAt']),
    completedAt: _parseDate(json['completedAt']),
    timezone: json['timezone'] as String,
    sourceRevision: (json['sourceRevision'] as num).toInt(),
    version: (json['version'] as num).toInt(),
    createdAt: _parseDate(json['createdAt'])!,
    updatedAt: _parseDate(json['updatedAt'])!,
  );

  static DateTime? _parseDate(Object? value) {
    if (value == null) return null;
    if (value is String && value.isEmpty) return null;
    return DateTime.parse(value as String);
  }
}

/// 列表分页响应。`nextCursor` 为 `null` 表示没有下一页。
class EventPage {
  const EventPage({required this.items, required this.nextCursor});

  final List<EventModel> items;
  final int? nextCursor;

  factory EventPage.fromJson(Map<String, dynamic> json) => EventPage(
    items: (json['items'] as List<dynamic>)
        .map((raw) => EventModel.fromJson(raw as Map<String, dynamic>))
        .toList(growable: false),
    nextCursor: (json['nextCursor'] as num?)?.toInt(),
  );
}

class ProblemDetails {
  const ProblemDetails({
    required this.status,
    this.title,
    this.detail,
    this.type,
    this.instance,
  });

  final int status;
  final String? title;
  final String? detail;
  final String? type;
  final String? instance;

  factory ProblemDetails.fromJson(Map<String, dynamic> json) => ProblemDetails(
    status: (json['status'] as num?)?.toInt() ?? 0,
    title: json['title'] as String?,
    detail: json['detail'] as String?,
    type: json['type'] as String?,
    instance: json['instance'] as String?,
  );
}
