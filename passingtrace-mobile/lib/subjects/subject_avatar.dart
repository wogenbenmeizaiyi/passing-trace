import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../events/media_api.dart';
import '../profile/account_avatar.dart';
import '../theme/passingtrace_theme.dart';
import 'subject_model.dart';

String subjectTypeLabel(SubjectModel subject) => switch (subject.kind) {
  0 => '人',
  1 => '宠物',
  _ => switch (subject.itemType) {
    'vehicle' => '车辆',
    'property' => '房屋',
    'bicycle' => '自行车',
    'collectible' => '收藏品',
    _ => '物品',
  },
};

IconData subjectTypeIcon(SubjectModel subject) => switch (subject.kind) {
  0 => Icons.person_outline,
  1 => Icons.pets_outlined,
  _ => switch (subject.itemType) {
    'vehicle' => Icons.directions_car_outlined,
    'property' => Icons.home_outlined,
    'bicycle' => Icons.pedal_bike_outlined,
    'collectible' => Icons.diamond_outlined,
    _ => Icons.inventory_2_outlined,
  },
};

class SubjectAvatar extends StatelessWidget {
  const SubjectAvatar({
    super.key,
    required this.subject,
    this.size = 56,
    this.accountAvatar,
    this.imageBytes,
    this.cover,
  });
  final SubjectModel subject;
  final double size;
  final Uint8List? accountAvatar;
  final Uint8List? imageBytes;
  final Future<MediaAccessTarget>? cover;

  @override
  Widget build(BuildContext context) {
    if (subject.isSelf) return AccountAvatar(bytes: accountAvatar, size: size);
    final colors = context.traceColors;
    Widget fallback() => Center(
      child: Icon(
        subjectTypeIcon(subject),
        color: colors.primaryStrong,
        size: size * .48,
      ),
    );
    Widget frame(BuildContext context, Widget child, int? frame, bool sync) =>
        sync || frame != null ? child : fallback();
    return ExcludeSemantics(
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          color: subject.state == 1 ? colors.canvas : colors.primarySoft,
          border: Border.all(color: colors.line),
        ),
        clipBehavior: Clip.antiAlias,
        child: imageBytes != null
            ? Image.memory(
                imageBytes!,
                fit: BoxFit.cover,
                frameBuilder: frame,
                errorBuilder: (_, _, _) => fallback(),
              )
            : cover == null
            ? fallback()
            : FutureBuilder<MediaAccessTarget>(
                future: cover,
                builder: (context, snapshot) => snapshot.hasData
                    ? Image.network(
                        snapshot.data!.url.toString(),
                        headers: snapshot.data!.headers,
                        fit: BoxFit.cover,
                        cacheWidth:
                            (size * MediaQuery.devicePixelRatioOf(context))
                                .ceil(),
                        frameBuilder: frame,
                        errorBuilder: (_, _, _) => fallback(),
                      )
                    : fallback(),
              ),
      ),
    );
  }
}
