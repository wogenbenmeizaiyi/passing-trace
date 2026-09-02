import '../events/event_model.dart';

enum StorylineStatus {
  ongoing(1, '进行中'),
  completed(2, '已完成');

  const StorylineStatus(this.value, this.label);
  final int value;
  final String label;
  static StorylineStatus fromValue(int value) =>
      values.firstWhere((x) => x.value == value);
}

class StorylineSummary {
  const StorylineSummary({
    required this.id,
    required this.title,
    required this.description,
    required this.categoryKey,
    required this.categoryLabel,
    required this.status,
    required this.revision,
    required this.version,
    required this.coverMediaAssetId,
    required this.rangeStart,
    required this.rangeEnd,
    required this.nodeCount,
    required this.tags,
    required this.layoutState,
    required this.updatedAt,
  });
  final String id, title, categoryKey, categoryLabel;
  final String? description;
  final StorylineStatus status;
  final int revision, version, nodeCount, layoutState;
  final String? coverMediaAssetId;
  final DateTime? rangeStart, rangeEnd;
  final List<String> tags;
  final DateTime updatedAt;
  factory StorylineSummary.fromJson(Map<String, dynamic> json) =>
      StorylineSummary(
        id: json['id'] as String,
        title: json['title'] as String,
        description: json['description'] as String?,
        categoryKey: json['categoryKey'] as String,
        categoryLabel: json['categoryLabel'] as String,
        status: StorylineStatus.fromValue((json['status'] as num).toInt()),
        revision: (json['revision'] as num).toInt(),
        version: (json['version'] as num).toInt(),
        coverMediaAssetId: json['coverMediaAssetId'] as String?,
        rangeStart: _date(json['rangeStart']),
        rangeEnd: _date(json['rangeEnd']),
        nodeCount: (json['nodeCount'] as num).toInt(),
        tags: (json['tags'] as List<dynamic>? ?? const []).cast<String>(),
        layoutState: (json['layoutState'] as num).toInt(),
        updatedAt: _date(json['updatedAt'])!,
      );
}

class StorylineStageModel {
  const StorylineStageModel({
    required this.key,
    required this.title,
    required this.semanticOrder,
  });
  final String key, title;
  final int semanticOrder;
  factory StorylineStageModel.fromJson(Map<String, dynamic> json) =>
      StorylineStageModel(
        key: json['key'] as String,
        title: json['title'] as String,
        semanticOrder: (json['semanticOrder'] as num).toInt(),
      );
  Map<String, dynamic> toJson() => {
    'key': key,
    'title': title,
    'semanticOrder': semanticOrder,
  };
}

class StorylineNodeModel {
  const StorylineNodeModel({
    required this.key,
    required this.eventId,
    required this.sourceRevision,
    required this.currentSourceRevision,
    required this.revisionState,
    required this.kind,
    required this.status,
    required this.title,
    required this.rawContent,
    required this.occurredAt,
    required this.stageKey,
    required this.semanticOrder,
    required this.emphasis,
    required this.place,
    required this.tags,
    required this.imageMediaAssetId,
  });
  final String key, revisionState, title;
  final int eventId,
      sourceRevision,
      currentSourceRevision,
      semanticOrder,
      emphasis;
  final EventKind kind;
  final EventStatus status;
  final String? rawContent, stageKey, place, imageMediaAssetId;
  final DateTime? occurredAt;
  final List<String> tags;
  factory StorylineNodeModel.fromJson(Map<String, dynamic> json) =>
      StorylineNodeModel(
        key: json['key'] as String,
        eventId: (json['eventId'] as num).toInt(),
        sourceRevision: (json['sourceRevision'] as num).toInt(),
        currentSourceRevision: (json['currentSourceRevision'] as num).toInt(),
        revisionState: json['revisionState'] as String,
        kind: EventKind.fromValue((json['kind'] as num).toInt()),
        status: EventStatus.fromValue((json['status'] as num).toInt()),
        title: json['title'] as String,
        rawContent: json['rawContent'] as String?,
        occurredAt: _date(json['occurredAt']),
        stageKey: json['stageKey'] as String?,
        semanticOrder: (json['semanticOrder'] as num).toInt(),
        emphasis: (json['emphasis'] as num).toInt(),
        place: json['place'] as String?,
        tags: (json['tags'] as List<dynamic>? ?? const []).cast<String>(),
        imageMediaAssetId: json['imageMediaAssetId'] as String?,
      );
}

class StorylineOutlineNode {
  const StorylineOutlineNode({
    required this.nodeKey,
    required this.stageKey,
    required this.topologicalOrder,
    required this.depth,
    required this.incomingCount,
    required this.outgoingCount,
    required this.startsBranch,
    required this.isMerge,
  });
  final String nodeKey;
  final String? stageKey;
  final int topologicalOrder, depth, incomingCount, outgoingCount;
  final bool startsBranch, isMerge;
  factory StorylineOutlineNode.fromJson(Map<String, dynamic> json) =>
      StorylineOutlineNode(
        nodeKey: json['nodeKey'] as String,
        stageKey: json['stageKey'] as String?,
        topologicalOrder: (json['topologicalOrder'] as num).toInt(),
        depth: (json['depth'] as num).toInt(),
        incomingCount: (json['incomingCount'] as num).toInt(),
        outgoingCount: (json['outgoingCount'] as num).toInt(),
        startsBranch: json['startsBranch'] as bool,
        isMerge: json['isMerge'] as bool,
      );
}

class StorylineDetailModel {
  const StorylineDetailModel({
    required this.id,
    required this.title,
    required this.description,
    required this.categoryKey,
    required this.categoryLabel,
    required this.status,
    required this.revision,
    required this.version,
    required this.rangeStart,
    required this.rangeEnd,
    required this.layoutState,
    required this.tags,
    required this.stages,
    required this.nodes,
    required this.outline,
    required this.updatedAt,
  });
  final String id, title, categoryKey, categoryLabel;
  final String? description;
  final StorylineStatus status;
  final int revision, version, layoutState;
  final DateTime? rangeStart, rangeEnd;
  final List<String> tags;
  final List<StorylineStageModel> stages;
  final List<StorylineNodeModel> nodes;
  final List<StorylineOutlineNode> outline;
  final DateTime updatedAt;
  StorylineNodeModel node(String key) => nodes.firstWhere((x) => x.key == key);
  factory StorylineDetailModel.fromJson(Map<String, dynamic> json) =>
      StorylineDetailModel(
        id: json['id'] as String,
        title: json['title'] as String,
        description: json['description'] as String?,
        categoryKey: json['categoryKey'] as String,
        categoryLabel: json['categoryLabel'] as String,
        status: StorylineStatus.fromValue((json['status'] as num).toInt()),
        revision: (json['revision'] as num).toInt(),
        version: (json['version'] as num).toInt(),
        rangeStart: _date(json['rangeStart']),
        rangeEnd: _date(json['rangeEnd']),
        layoutState: (json['layoutState'] as num).toInt(),
        tags: (json['tags'] as List<dynamic>? ?? const []).cast<String>(),
        stages: (json['stages'] as List<dynamic>? ?? const [])
            .map((x) => StorylineStageModel.fromJson(x as Map<String, dynamic>))
            .toList(),
        nodes: (json['nodes'] as List<dynamic>? ?? const [])
            .map((x) => StorylineNodeModel.fromJson(x as Map<String, dynamic>))
            .toList(),
        outline: (json['outline'] as List<dynamic>? ?? const [])
            .map(
              (x) => StorylineOutlineNode.fromJson(x as Map<String, dynamic>),
            )
            .toList(),
        updatedAt: _date(json['updatedAt'])!,
      );
}

class StorylineSaveResult {
  const StorylineSaveResult({
    required this.storyline,
    required this.undoRevision,
  });
  final StorylineDetailModel storyline;
  final int? undoRevision;
  factory StorylineSaveResult.fromJson(Map<String, dynamic> json) =>
      StorylineSaveResult(
        storyline: StorylineDetailModel.fromJson(
          json['storyline'] as Map<String, dynamic>,
        ),
        undoRevision: (json['undoRevision'] as num?)?.toInt(),
      );
}

DateTime? _date(dynamic raw) => raw is String ? DateTime.tryParse(raw) : null;
