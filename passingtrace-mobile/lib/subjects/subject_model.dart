class SubjectField {
  const SubjectField({
    required this.id,
    required this.name,
    required this.type,
    this.unit,
    this.options = const [],
    this.key,
    this.removed = false,
  });
  final String id, name, type;
  final String? unit, key;
  final List<String> options;
  final bool removed;
  factory SubjectField.fromJson(Map<String, dynamic> json) => SubjectField(
    id: json['id'] as String,
    name: json['name'] as String,
    type: json['type'] as String,
    unit: json['unit'] as String?,
    key: json['key'] as String?,
    options: (json['options'] as List? ?? []).cast<String>(),
    removed: json['removed'] == true,
  );
  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'type': type,
    'unit': unit,
    'key': key,
    'options': options,
    'removed': removed,
  };
  SubjectField copyWith({
    String? name,
    String? type,
    String? unit,
    List<String>? options,
    bool? removed,
  }) => SubjectField(
    id: id,
    name: name ?? this.name,
    type: type ?? this.type,
    unit: unit ?? this.unit,
    options: options ?? this.options,
    key: key,
    removed: removed ?? this.removed,
  );
}

class SubjectModel {
  SubjectModel(this.json);
  final Map<String, dynamic> json;
  String get id => json['id'] as String;
  String get name => json['name'] as String;
  String? get description => json['description'] as String?;
  String? get itemType => json['itemType'] as String?;
  int get kind => (json['kind'] as num).toInt();
  int get state => (json['state'] as num).toInt();
  int get version => (json['version'] as num).toInt();
  bool get isSelf => json['isSelf'] == true;
  String get timezone => json['timezone'] as String;
  String? get startedAt => json['startedAt'] as String?;
  String? get endedAt => json['endedAt'] as String?;
  String? get endReason => json['endReason'] as String?;
  int? get ageDays => (json['ageDays'] as num?)?.toInt();
  List<SubjectField> get fields => (json['fields'] as List)
      .map((x) => SubjectField.fromJson(Map<String, dynamic>.from(x as Map)))
      .toList();
  Map<String, dynamic> get values =>
      Map<String, dynamic>.from(json['values'] as Map);
  List<String> get mediaIds => (json['mediaIds'] as List).cast<String>();
  String? get coverMediaId => json['coverMediaId'] as String?;
}

class SubjectRelation {
  SubjectRelation(this.json);
  final Map<String, dynamic> json;
  String get id => json['id'] as String;
  String get from => json['fromSubjectId'] as String;
  String get to => json['toSubjectId'] as String;
  String get label => json['label'] as String;
  bool get directed => json['directed'] == true;
  String? get endedAt => json['endedAt'] as String?;
  int get version => (json['revision'] as num).toInt();
}

class SubjectGraph {
  SubjectGraph.fromJson(Map<String, dynamic> json)
    : rootId = json['rootId'] as String,
      nodes = (json['nodes'] as List)
          .map((x) => SubjectModel(Map<String, dynamic>.from(x as Map)))
          .toList(),
      relations = (json['relations'] as List)
          .map((x) => SubjectRelation(Map<String, dynamic>.from(x as Map)))
          .toList();
  final String rootId;
  final List<SubjectModel> nodes;
  final List<SubjectRelation> relations;
}

class SubjectEntry {
  SubjectEntry(this.json);
  final Map<String, dynamic> json;
  String get id => json['id'] as String;
  String get subjectId => json['subjectId'] as String;
  String get subjectName => json['subjectName'] as String;
  String get title => json['title'] as String;
  String? get content => json['content'] as String?;
  int get kind => (json['kind'] as num).toInt();
  int get state => (json['state'] as num).toInt();
  int get version => (json['version'] as num).toInt();
  String get timezone => json['timezone'] as String;
  bool get sourceSubjectDeleted => json['sourceSubjectDeleted'] == true;
  String? get happenedAt => json['happenedAt'] as String?;
  String? get plannedAt => json['plannedAt'] as String?;
  Map<String, dynamic> get fieldChanges =>
      Map<String, dynamic>.from(json['fieldChanges'] as Map);
  Map<String, dynamic> get actualFieldChanges =>
      Map<String, dynamic>.from(json['actualFieldChanges'] as Map);
  List<SubjectField> get fields => (json['fieldDefinitions'] as List)
      .map((x) => SubjectField.fromJson(Map<String, dynamic>.from(x as Map)))
      .toList();
  List<String> get markedSubjectIds =>
      (json['markedSubjectIds'] as List).cast<String>();
  List<String> get mediaIds => (json['mediaIds'] as List).cast<String>();
}

const subjectReasons = {
  'deceased': '离世',
  'relationship-ended': '关系结束',
  'sold': '出售',
  'gifted': '转赠',
  'lost': '丢失',
  'scrapped': '报废',
  'other': '其他',
};
