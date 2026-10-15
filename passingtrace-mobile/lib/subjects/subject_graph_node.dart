import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../events/media_api.dart';
import '../theme/passingtrace_theme.dart';
import 'subject_avatar.dart';
import 'subject_model.dart';

/// Content of a circular graph node; the canvas owns its border and gestures.
class SubjectGraphNode extends StatelessWidget {
  const SubjectGraphNode({
    super.key,
    required this.subject,
    required this.nickname,
    this.accountAvatar,
    this.cover,
  });

  final SubjectModel subject;
  final String nickname;
  final Uint8List? accountAvatar;
  final Future<MediaAccessTarget>? cover;

  String get _name => subject.isSelf ? '$nickname（自己）' : subject.name;
  String get _state => subject.state == 1 ? '已结束' : '进行中';

  Widget _fallback(BuildContext context) => Padding(
    padding: const EdgeInsets.all(20),
    child: Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        SubjectAvatar(subject: subject, size: 40),
        const SizedBox(height: 4),
        Flexible(
          child: Text(
            _name,
            textAlign: TextAlign.center,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontWeight: FontWeight.bold),
          ),
        ),
        const SizedBox(height: 4),
        Text(
          _state,
          style: TextStyle(
            fontSize: 12,
            color: context.traceColors.inkSecondary,
          ),
        ),
      ],
    ),
  );

  Widget _photo(Widget image) => Stack(
    key: ValueKey('subject-node-photo-${subject.id}'),
    fit: StackFit.expand,
    children: [
      ExcludeSemantics(child: image),
      const DecoratedBox(
        decoration: BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [Colors.transparent, Colors.transparent, Color(0xDD000000)],
            stops: [0, .35, 1],
          ),
        ),
      ),
      Align(
        alignment: Alignment.bottomCenter,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(22, 0, 22, 22),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                _name,
                textAlign: TextAlign.center,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                  color: Colors.white,
                  fontWeight: FontWeight.bold,
                ),
              ),
              const SizedBox(height: 4),
              Text(
                _state,
                style: const TextStyle(fontSize: 12, color: Colors.white),
              ),
            ],
          ),
        ),
      ),
    ],
  );

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final cacheWidth =
          (constraints.maxWidth * MediaQuery.devicePixelRatioOf(context))
              .ceil();
      Widget frame(BuildContext context, Widget child, int? frame, bool sync) =>
          sync || frame != null ? _photo(child) : _fallback(context);
      Widget error(BuildContext context, Object error, StackTrace? stack) =>
          _fallback(context);
      if (subject.isSelf) {
        return accountAvatar == null
            ? _fallback(context)
            : Image.memory(
                accountAvatar!,
                fit: BoxFit.cover,
                cacheWidth: cacheWidth,
                frameBuilder: frame,
                errorBuilder: error,
              );
      }
      if (cover == null) return _fallback(context);
      return FutureBuilder<MediaAccessTarget>(
        future: cover,
        builder: (context, snapshot) => snapshot.hasData
            ? Image.network(
                snapshot.data!.url.toString(),
                headers: snapshot.data!.headers,
                fit: BoxFit.cover,
                cacheWidth: cacheWidth,
                frameBuilder: frame,
                errorBuilder: error,
              )
            : _fallback(context),
      );
    },
  );
}
