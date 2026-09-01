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
            routeColor: colors.onPrimary,
            startColor: colors.accent,
            backgroundColor: colors.primary,
          ),
        ),
      ),
    );
  }
}

class _PassingTraceMarkPainter extends CustomPainter {
  const _PassingTraceMarkPainter({
    required this.routeColor,
    required this.startColor,
    required this.backgroundColor,
  });

  final Color routeColor;
  final Color startColor;
  final Color backgroundColor;

  @override
  void paint(Canvas canvas, Size size) {
    Offset point(double x, double y) => Offset(size.width * x, size.height * y);
    final route = Path()
      ..moveTo(size.width * 0.296, size.height * 0.324)
      ..cubicTo(
        size.width * 0.389,
        size.height * 0.25,
        size.width * 0.519,
        size.height * 0.259,
        size.width * 0.593,
        size.height * 0.333,
      )
      ..cubicTo(
        size.width * 0.667,
        size.height * 0.407,
        size.width * 0.63,
        size.height * 0.5,
        size.width * 0.519,
        size.height * 0.519,
      )
      ..cubicTo(
        size.width * 0.398,
        size.height * 0.537,
        size.width * 0.352,
        size.height * 0.602,
        size.width * 0.417,
        size.height * 0.676,
      )
      ..cubicTo(
        size.width * 0.481,
        size.height * 0.741,
        size.width * 0.602,
        size.height * 0.731,
        size.width * 0.704,
        size.height * 0.639,
      );

    canvas.drawPath(
      route,
      Paint()
        ..color = routeColor
        ..style = PaintingStyle.stroke
        ..strokeWidth = size.width * 0.065
        ..strokeCap = StrokeCap.round
        ..strokeJoin = StrokeJoin.round,
    );

    final start = point(0.296, 0.324);
    final end = point(0.704, 0.639);
    final nodeRadius = size.width * 0.069;
    final coreRadius = size.width * 0.022;
    canvas
      ..drawCircle(start, nodeRadius, Paint()..color = startColor)
      ..drawCircle(start, coreRadius, Paint()..color = routeColor)
      ..drawCircle(end, nodeRadius, Paint()..color = routeColor)
      ..drawCircle(end, coreRadius, Paint()..color = backgroundColor);
  }

  @override
  bool shouldRepaint(covariant _PassingTraceMarkPainter oldDelegate) =>
      routeColor != oldDelegate.routeColor ||
      startColor != oldDelegate.startColor ||
      backgroundColor != oldDelegate.backgroundColor;
}
