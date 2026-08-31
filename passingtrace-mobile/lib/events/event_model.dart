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
    this.media = const [],
    this.semanticStatus,
    this.semanticSummary,
    this.manualClassification = const ManualClassification(),
    this.effectiveClassification = const EffectiveClassification(),
    this.locations = const [],
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
  final List<MediaAssetModel> media;
  final String? semanticStatus;
  final String? semanticSummary;
  final ManualClassification manualClassification;
  final EffectiveClassification effectiveClassification;
  final List<EventLocationModel> locations;

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
    List<MediaAssetModel>? media,
    String? semanticStatus,
    String? semanticSummary,
    ManualClassification? manualClassification,
    EffectiveClassification? effectiveClassification,
    List<EventLocationModel>? locations,
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
    media: media ?? this.media,
    semanticStatus: semanticStatus ?? this.semanticStatus,
    semanticSummary: semanticSummary ?? this.semanticSummary,
    manualClassification: manualClassification ?? this.manualClassification,
    effectiveClassification:
        effectiveClassification ?? this.effectiveClassification,
    locations: locations ?? this.locations,
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
    media: (json['media'] as List<dynamic>? ?? const [])
        .map((raw) => MediaAssetModel.fromJson(raw as Map<String, dynamic>))
        .toList(growable: false),
    semanticStatus: json['semanticStatus'] as String?,
    semanticSummary: json['semanticSummary'] as String?,
    manualClassification: ManualClassification.fromJson(
      json['manualClassification'] as Map<String, dynamic>?,
    ),
    effectiveClassification: EffectiveClassification.fromJson(
      json['effectiveClassification'] as Map<String, dynamic>?,
    ),
    locations: (json['locations'] as List<dynamic>? ?? const [])
        .map((x) => EventLocationModel.fromJson(x as Map<String, dynamic>))
        .toList(),
  );

  static DateTime? _parseDate(Object? value) {
    if (value == null) return null;
    if (value is String && value.isEmpty) return null;
    return DateTime.parse(value as String);
  }
}

class EventLabelModel {
  const EventLabelModel({
    this.taxonomyKey,
    required this.displayName,
    required this.origin,
    this.confidence,
  });
  final String? taxonomyKey;
  final String displayName;
  final String origin;
  final double? confidence;
  bool get isAi => origin == 'ai';
  factory EventLabelModel.fromJson(Map<String, dynamic> json) =>
      EventLabelModel(
        taxonomyKey: json['taxonomyKey'] as String?,
        displayName: json['displayName'] as String,
        origin: json['origin'] as String,
        confidence: (json['confidence'] as num?)?.toDouble(),
      );
}

class ManualTagModel {
  const ManualTagModel({this.taxonomyKey, this.name});
  final String? taxonomyKey;
  final String? name;
  Map<String, dynamic> toJson() => {'taxonomyKey': taxonomyKey, 'name': name};
  factory ManualTagModel.fromJson(Map<String, dynamic> json) => ManualTagModel(
    taxonomyKey: json['taxonomyKey'] as String?,
    name: json['name'] as String?,
  );
}

class ManualClassification {
  const ManualClassification({
    this.primaryCategoryKey,
    this.tags = const [],
    this.suppressedAiTagKeys = const [],
  });
  final String? primaryCategoryKey;
  final List<ManualTagModel> tags;
  final List<String> suppressedAiTagKeys;
  Map<String, dynamic> toJson() => {
    'primaryCategoryKey': primaryCategoryKey,
    'tags': tags.map((x) => x.toJson()).toList(),
    'suppressedAiTagKeys': suppressedAiTagKeys,
  };
  factory ManualClassification.fromJson(Map<String, dynamic>? json) =>
      json == null
      ? const ManualClassification()
      : ManualClassification(
          primaryCategoryKey: json['primaryCategoryKey'] as String?,
          tags: (json['tags'] as List<dynamic>? ?? const [])
              .map((x) => ManualTagModel.fromJson(x as Map<String, dynamic>))
              .toList(),
          suppressedAiTagKeys:
              (json['suppressedAiTagKeys'] as List<dynamic>? ?? const [])
                  .cast<String>(),
        );
}

class EffectiveClassification {
  const EffectiveClassification({
    this.primaryCategory,
    this.tags = const [],
    this.taxonomyVersion = 'life-v1',
  });
  final EventLabelModel? primaryCategory;
  final List<EventLabelModel> tags;
  final String taxonomyVersion;
  factory EffectiveClassification.fromJson(Map<String, dynamic>? json) =>
      json == null
      ? const EffectiveClassification()
      : EffectiveClassification(
          primaryCategory: json['primaryCategory'] == null
              ? null
              : EventLabelModel.fromJson(
                  json['primaryCategory'] as Map<String, dynamic>,
                ),
          tags: (json['tags'] as List<dynamic>? ?? const [])
              .map((x) => EventLabelModel.fromJson(x as Map<String, dynamic>))
              .toList(),
          taxonomyVersion: json['taxonomyVersion'] as String? ?? 'life-v1',
        );
}

class EventLocationModel {
  const EventLocationModel({
    this.id,
    required this.name,
    this.address,
    this.province,
    this.city,
    this.district,
    this.adCode,
    this.providerPoiId,
    this.poiType,
    this.latitude,
    this.longitude,
    this.accuracyMeters,
    this.coordinateSystem = 'UNKNOWN',
    this.source = 4,
    this.capturedAt,
  });
  final int? id;
  final String name;
  final String? address;
  final String? province;
  final String? city;
  final String? district;
  final String? adCode;
  final String? providerPoiId;
  final String? poiType;
  final double? latitude;
  final double? longitude;
  final double? accuracyMeters;
  final String coordinateSystem;
  final int source;
  final DateTime? capturedAt;
  bool get canNavigate =>
      id != null &&
      latitude != null &&
      longitude != null &&
      coordinateSystem == 'GCJ02';
  Map<String, dynamic> toJson() => {
    'name': name,
    'address': address,
    'province': province,
    'city': city,
    'district': district,
    'adCode': adCode,
    'providerPoiId': providerPoiId,
    'poiType': poiType,
    'latitude': latitude,
    'longitude': longitude,
    'accuracyMeters': accuracyMeters,
    'coordinateSystem': coordinateSystem,
    'source': source,
    'capturedAt': capturedAt?.toUtc().toIso8601String(),
  };
  factory EventLocationModel.fromJson(Map<String, dynamic> json) =>
      EventLocationModel(
        id: (json['id'] as num?)?.toInt(),
        name: json['name'] as String,
        address: json['address'] as String?,
        province: json['province'] as String?,
        city: json['city'] as String?,
        district: json['district'] as String?,
        adCode: json['adCode'] as String?,
        providerPoiId: json['providerPoiId'] as String?,
        poiType: json['poiType'] as String?,
        latitude: (json['latitude'] as num?)?.toDouble(),
        longitude: (json['longitude'] as num?)?.toDouble(),
        accuracyMeters: (json['accuracyMeters'] as num?)?.toDouble(),
        coordinateSystem: json['coordinateSystem'] as String? ?? 'UNKNOWN',
        source: (json['source'] as num?)?.toInt() ?? 4,
        capturedAt: json['capturedAt'] == null
            ? null
            : DateTime.parse(json['capturedAt'] as String),
      );
}

class TaxonomyItem {
  const TaxonomyItem(this.key, this.label);
  final String key;
  final String label;
}

class EventTaxonomyModel {
  const EventTaxonomyModel({
    required this.version,
    required this.categories,
    required this.behaviorTags,
  });
  final String version;
  final List<TaxonomyItem> categories;
  final List<TaxonomyItem> behaviorTags;
  factory EventTaxonomyModel.fromJson(Map<String, dynamic> json) =>
      EventTaxonomyModel(
        version: json['version'] as String,
        categories: (json['categories'] as List)
            .map((x) => TaxonomyItem(x['key'] as String, x['label'] as String))
            .toList(),
        behaviorTags: (json['behaviorTags'] as List)
            .map((x) => TaxonomyItem(x['key'] as String, x['label'] as String))
            .toList(),
      );
}

class PlaceCandidateModel extends EventLocationModel {
  const PlaceCandidateModel({
    required super.name,
    super.address,
    super.province,
    super.city,
    super.district,
    super.adCode,
    super.providerPoiId,
    super.poiType,
    required super.latitude,
    required super.longitude,
    super.coordinateSystem = 'GCJ02',
    this.distanceMeters,
  }) : super(source: 3);
  final int? distanceMeters;
  factory PlaceCandidateModel.fromJson(Map<String, dynamic> json) =>
      PlaceCandidateModel(
        name: json['name'] as String,
        address: json['address'] as String?,
        province: json['province'] as String?,
        city: json['city'] as String?,
        district: json['district'] as String?,
        adCode: json['adCode'] as String?,
        providerPoiId: json['poiId'] as String?,
        poiType: json['poiType'] as String?,
        latitude: (json['latitude'] as num).toDouble(),
        longitude: (json['longitude'] as num).toDouble(),
        coordinateSystem: json['coordinateSystem'] as String? ?? 'GCJ02',
        distanceMeters: (json['distanceMeters'] as num?)?.toInt(),
      );
}

enum MediaKind {
  image(1),
  video(2),
  file(3);

  const MediaKind(this.value);
  final int value;

  static MediaKind fromValue(int value) => MediaKind.values.firstWhere(
    (kind) => kind.value == value,
    orElse: () => MediaKind.file,
  );
}

class MediaAssetModel {
  const MediaAssetModel({
    required this.id,
    required this.fileName,
    required this.kind,
    required this.contentType,
    required this.size,
    required this.status,
    required this.sortOrder,
  });

  final String id;
  final String fileName;
  final MediaKind kind;
  final String contentType;
  final int size;
  final int status;
  final int sortOrder;

  factory MediaAssetModel.fromJson(Map<String, dynamic> json) =>
      MediaAssetModel(
        id: json['id'] as String,
        fileName: json['fileName'] as String,
        kind: MediaKind.fromValue((json['kind'] as num).toInt()),
        contentType: json['contentType'] as String,
        size: (json['size'] as num).toInt(),
        status: (json['status'] as num).toInt(),
        sortOrder: (json['sortOrder'] as num).toInt(),
      );
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
