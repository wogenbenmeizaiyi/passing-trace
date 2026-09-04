import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'passingtrace_theme.dart';

class PassingTraceMark extends StatelessWidget {
  const PassingTraceMark({super.key, required this.size});

  final double size;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return SizedBox.square(
      dimension: size,
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: colors.primary,
          borderRadius: BorderRadius.circular(size * 0.28),
        ),
        child: CustomPaint(
          painter: _PassingTraceMarkPainter(
            paperColor: colors.onPrimary,
            accentColor: colors.accent,
            softColor: colors.primarySoft,
          ),
        ),
      ),
    );
  }
}

class _PassingTraceMarkPainter extends CustomPainter {
  const _PassingTraceMarkPainter({
    required this.paperColor,
    required this.accentColor,
    required this.softColor,
  });

  final Color paperColor;
  final Color accentColor;
  final Color softColor;

  @override
  void paint(Canvas canvas, Size size) {
    final sx = size.width / 108;
    final sy = size.height / 108;
    Rect rect(double x, double y, double width, double height) =>
        Rect.fromLTWH(x * sx, y * sy, width * sx, height * sy);

    void drawCard({
      required double centerX,
      required double centerY,
      required double angle,
      required VoidCallback paint,
    }) {
      canvas
        ..save()
        ..translate(centerX * sx, centerY * sy)
        ..rotate(angle * math.pi / 180)
        ..translate(-centerX * sx, -centerY * sy);
      paint();
      canvas.restore();
    }

    drawCard(
      centerX: 42,
      centerY: 42,
      angle: -8,
      paint: () {
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            rect(28, 22, 28, 40),
            Radius.circular(5 * sx),
          ),
          Paint()..color = paperColor,
        );
        canvas.drawCircle(
          Offset(40 * sx, 34 * sy),
          6 * sx,
          Paint()..color = accentColor,
        );
        final photo = Path()
          ..moveTo(31 * sx, 51 * sy)
          ..lineTo(39 * sx, 42 * sy)
          ..lineTo(46 * sx, 48 * sy)
          ..lineTo(53 * sx, 40 * sy)
          ..lineTo(57 * sx, 58 * sy)
          ..lineTo(31 * sx, 58 * sy)
          ..close();
        canvas.drawPath(photo, Paint()..color = softColor);
      },
    );

    drawCard(
      centerX: 67,
      centerY: 45,
      angle: 7,
      paint: () {
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            rect(53, 25, 28, 40),
            Radius.circular(5 * sx),
          ),
          Paint()..color = accentColor,
        );
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            rect(60, 33, 14, 5),
            Radius.circular(2.5 * sx),
          ),
          Paint()..color = paperColor,
        );
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            rect(60, 43, 11, 5),
            Radius.circular(2.5 * sx),
          ),
          Paint()..color = paperColor,
        );
      },
    );

    final lid = Path()
      ..moveTo(19 * sx, 49 * sy)
      ..lineTo(89 * sx, 49 * sy)
      ..lineTo(82 * sx, 63 * sy)
      ..lineTo(26 * sx, 63 * sy)
      ..close();
    canvas.drawPath(lid, Paint()..color = accentColor);

    final box = Path()
      ..moveTo(25 * sx, 59 * sy)
      ..lineTo(83 * sx, 59 * sy)
      ..lineTo(78 * sx, 85 * sy)
      ..lineTo(30 * sx, 85 * sy)
      ..close();
    canvas.drawPath(box, Paint()..color = paperColor);
    canvas.drawRRect(
      RRect.fromRectAndRadius(
        rect(45, 65, 18, 8),
        Radius.circular(4 * sx),
      ),
      Paint()..color = accentColor,
    );
  }

  @override
  bool shouldRepaint(covariant _PassingTraceMarkPainter oldDelegate) =>
      paperColor != oldDelegate.paperColor ||
      accentColor != oldDelegate.accentColor ||
      softColor != oldDelegate.softColor;
}
