import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../theme/passingtrace_theme.dart';

class AccountAvatar extends StatelessWidget {
  const AccountAvatar({super.key, this.bytes, this.size = 48});
  final Uint8List? bytes;
  final double size;
  @override
  Widget build(BuildContext context) => ExcludeSemantics(
    child: Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: context.traceColors.primarySoft,
        border: Border.all(color: context.traceColors.lineStrong),
      ),
      clipBehavior: Clip.antiAlias,
      child: bytes == null
          ? _fallback(context)
          : Image.memory(
              bytes!,
              fit: BoxFit.cover,
              gaplessPlayback: true,
              errorBuilder: (_, _, _) => _fallback(context),
            ),
    ),
  );
  Widget _fallback(BuildContext context) =>
      CustomPaint(painter: _AvatarPainter(context.traceColors.primaryStrong));
}

class _AvatarPainter extends CustomPainter {
  _AvatarPainter(this.color);
  final Color color;
  @override
  void paint(Canvas canvas, Size size) {
    canvas.scale(size.width / 48, size.height / 48);
    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2
      ..strokeCap = StrokeCap.round;
    canvas.drawCircle(const Offset(24, 18), 7, paint);
    canvas.drawPath(
      Path()
        ..moveTo(11, 39)
        ..cubicTo(11, 24, 37, 24, 37, 39),
      paint,
    );
  }

  @override
  bool shouldRepaint(_AvatarPainter oldDelegate) => oldDelegate.color != color;
}
