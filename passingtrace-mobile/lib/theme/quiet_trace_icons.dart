import 'dart:math' as math;

import 'package:flutter/material.dart';

enum TraceGlyph {
  menu,
  add,
  journal,
  sparkle,
  history,
  chevronRight,
  chevronLeft,
  chevronDown,
  chevronUp,
  send,
  mapPin,
  paperclip,
  scan,
  memory,
  settings,
  logout,
  note,
  search,
  close,
  target,
  calendar,
  palette,
  monitor,
  sun,
  moon,
  check,
  filter,
  image,
  video,
  file,
  refresh,
  edit,
  delete,
  directions,
  externalLink,
}

class TraceIcon extends StatelessWidget {
  const TraceIcon(
    this.glyph, {
    super.key,
    this.size = 22,
    this.color,
    this.semanticLabel,
    this.strokeWidth = 1.8,
  });

  final TraceGlyph glyph;
  final double size;
  final Color? color;
  final String? semanticLabel;
  final double strokeWidth;

  @override
  Widget build(BuildContext context) {
    final icon = CustomPaint(
      size: Size.square(size),
      painter: _TraceIconPainter(
        glyph: glyph,
        color: color ?? IconTheme.of(context).color ?? Colors.black,
        strokeWidth: strokeWidth,
      ),
    );
    final label = semanticLabel;
    if (label == null) return ExcludeSemantics(child: icon);
    return Semantics(label: label, image: true, child: icon);
  }
}

class _TraceIconPainter extends CustomPainter {
  const _TraceIconPainter({
    required this.glyph,
    required this.color,
    required this.strokeWidth,
  });

  final TraceGlyph glyph;
  final Color color;
  final double strokeWidth;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.save();
    canvas.scale(size.width / 24, size.height / 24);
    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = strokeWidth
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round;
    final fill = Paint()
      ..color = color
      ..style = PaintingStyle.fill;

    Path path() => Path();
    void draw(Path value) => canvas.drawPath(value, paint);
    void circle(double x, double y, double radius) =>
        canvas.drawCircle(Offset(x, y), radius, paint);
    void line(double x1, double y1, double x2, double y2) => canvas.drawLine(
      Offset(x1, y1),
      Offset(x2, y2),
      paint,
    );

    switch (glyph) {
      case TraceGlyph.menu:
        line(4, 7, 20, 7);
        line(4, 12, 20, 12);
        line(4, 17, 20, 17);
      case TraceGlyph.add:
        line(12, 5, 12, 19);
        line(5, 12, 19, 12);
      case TraceGlyph.journal:
        draw(
          path()
            ..moveTo(5, 4.5)
            ..lineTo(16, 4.5)
            ..quadraticBezierTo(18, 4.5, 18, 6.5)
            ..lineTo(18, 19.5)
            ..lineTo(7, 19.5)
            ..quadraticBezierTo(5, 19.5, 5, 17.5)
            ..close(),
        );
        line(8, 4.5, 8, 19.5);
        line(11, 9, 15, 9);
        line(11, 13, 15, 13);
      case TraceGlyph.sparkle:
        draw(
          path()
            ..moveTo(12, 3)
            ..cubicTo(12.8, 7.5, 14.5, 9.2, 19, 10)
            ..cubicTo(14.5, 10.8, 12.8, 12.5, 12, 17)
            ..cubicTo(11.2, 12.5, 9.5, 10.8, 5, 10)
            ..cubicTo(9.5, 9.2, 11.2, 7.5, 12, 3)
            ..close(),
        );
        draw(
          path()
            ..moveTo(19, 16)
            ..cubicTo(19.3, 17.8, 20.2, 18.7, 22, 19)
            ..cubicTo(20.2, 19.3, 19.3, 20.2, 19, 22)
            ..cubicTo(18.7, 20.2, 17.8, 19.3, 16, 19)
            ..cubicTo(17.8, 18.7, 18.7, 17.8, 19, 16)
            ..close(),
        );
      case TraceGlyph.history:
        draw(
          path()
            ..moveTo(4, 12)
            ..arcTo(
              const Rect.fromLTWH(4, 4, 16, 16),
              math.pi,
              math.pi * 1.55,
              false,
            ),
        );
        draw(
          path()
            ..moveTo(4, 4)
            ..lineTo(4, 8.6)
            ..lineTo(8.6, 8.6),
        );
        line(12, 8, 12, 12);
        line(12, 12, 14.7, 13.7);
      case TraceGlyph.chevronRight:
        draw(path()..moveTo(9, 6)..lineTo(15, 12)..lineTo(9, 18));
      case TraceGlyph.chevronLeft:
        draw(path()..moveTo(15, 6)..lineTo(9, 12)..lineTo(15, 18));
      case TraceGlyph.chevronDown:
        draw(path()..moveTo(6, 9)..lineTo(12, 15)..lineTo(18, 9));
      case TraceGlyph.chevronUp:
        draw(path()..moveTo(6, 15)..lineTo(12, 9)..lineTo(18, 15));
      case TraceGlyph.send:
        draw(
          path()
            ..moveTo(21, 3)
            ..lineTo(13.6, 21)
            ..lineTo(10.4, 13.6)
            ..lineTo(3, 10.4)
            ..close()
            ..moveTo(10.4, 13.6)
            ..lineTo(21, 3),
        );
      case TraceGlyph.mapPin:
        draw(
          path()
            ..moveTo(20, 10)
            ..cubicTo(20, 15, 12, 21, 12, 21)
            ..cubicTo(12, 21, 4, 15, 4, 10)
            ..arcTo(
              const Rect.fromLTWH(4, 2, 16, 16),
              math.pi,
              math.pi * 2,
              false,
            )
            ..close(),
        );
        circle(12, 10, 2.5);
      case TraceGlyph.paperclip:
        draw(
          path()
            ..moveTo(8.5, 12.5)
            ..lineTo(15.2, 5.8)
            ..cubicTo(18.2, 2.8, 22.4, 7, 19.4, 10)
            ..lineTo(10.7, 18.7)
            ..cubicTo(5.7, 23.7, -1.4, 16.6, 3.6, 11.6)
            ..lineTo(12, 3.2),
        );
      case TraceGlyph.scan:
        draw(
          path()
            ..moveTo(8, 3)
            ..lineTo(5, 3)
            ..quadraticBezierTo(3, 3, 3, 5)
            ..lineTo(3, 8)
            ..moveTo(16, 3)
            ..lineTo(19, 3)
            ..quadraticBezierTo(21, 3, 21, 5)
            ..lineTo(21, 8)
            ..moveTo(8, 21)
            ..lineTo(5, 21)
            ..quadraticBezierTo(3, 21, 3, 19)
            ..lineTo(3, 16)
            ..moveTo(16, 21)
            ..lineTo(19, 21)
            ..quadraticBezierTo(21, 21, 21, 19)
            ..lineTo(21, 16)
            ..moveTo(7, 12)
            ..lineTo(17, 12),
        );
      case TraceGlyph.memory:
        draw(
          path()
            ..moveTo(9, 4.5)
            ..cubicTo(7, 2.5, 4, 4, 4, 6.7)
            ..cubicTo(1.5, 8.5, 2, 12, 4.5, 13)
            ..cubicTo(3, 16, 6, 18.5, 9, 16)
            ..lineTo(9, 19.5)
            ..moveTo(15, 4.5)
            ..cubicTo(17, 2.5, 20, 4, 20, 6.7)
            ..cubicTo(22.5, 8.5, 22, 12, 19.5, 13)
            ..cubicTo(21, 16, 18, 18.5, 15, 16)
            ..lineTo(15, 19.5)
            ..moveTo(9, 6.5)
            ..cubicTo(10.5, 8, 13.5, 8, 15, 6.5)
            ..moveTo(9, 14)
            ..cubicTo(10.5, 12.5, 13.5, 12.5, 15, 14)
            ..moveTo(12, 7.7)
            ..lineTo(12, 12.8),
        );
      case TraceGlyph.settings:
        circle(12, 12, 3);
        circle(12, 12, 8);
        for (var i = 0; i < 8; i++) {
          final a = i * math.pi / 4;
          line(
            12 + math.cos(a) * 8,
            12 + math.sin(a) * 8,
            12 + math.cos(a) * 10,
            12 + math.sin(a) * 10,
          );
        }
      case TraceGlyph.logout:
        draw(path()..moveTo(10, 5)..lineTo(5, 5)..lineTo(5, 19)..lineTo(10, 19));
        draw(path()..moveTo(14, 8)..lineTo(18, 12)..lineTo(14, 16));
        line(9, 12, 18, 12);
      case TraceGlyph.note:
        draw(path()..moveTo(5, 3)..lineTo(16, 3)..lineTo(19, 6)..lineTo(19, 21)..lineTo(5, 21)..close());
        draw(path()..moveTo(15, 3)..lineTo(15, 7)..lineTo(19, 7));
        line(8, 11, 16, 11);
        line(8, 15, 14, 15);
      case TraceGlyph.search:
        circle(11, 11, 6.5);
        line(16, 16, 20.2, 20.2);
      case TraceGlyph.close:
        line(7, 7, 17, 17);
        line(17, 7, 7, 17);
      case TraceGlyph.target:
        circle(12, 12, 7);
        circle(12, 12, 2.5);
        line(12, 2, 12, 5);
        line(12, 19, 12, 22);
        line(2, 12, 5, 12);
        line(19, 12, 22, 12);
      case TraceGlyph.calendar:
        canvas.drawRRect(
          RRect.fromRectAndRadius(const Rect.fromLTWH(3, 5, 18, 16), const Radius.circular(2)),
          paint,
        );
        line(8, 3, 8, 7);
        line(16, 3, 16, 7);
        line(3, 10, 21, 10);
      case TraceGlyph.palette:
        draw(
          path()
            ..moveTo(12, 3)
            ..arcTo(const Rect.fromLTWH(3, 3, 18, 18), -math.pi / 2, math.pi * 1.75, false)
            ..cubicTo(13.5, 21, 15, 19.2, 13.8, 18)
            ..cubicTo(12.6, 16.8, 13.4, 15, 15.5, 15)
            ..lineTo(18, 15)
            ..cubicTo(22, 15, 22, 3, 12, 3)
            ..close(),
        );
        canvas.drawCircle(const Offset(7.5, 11.5), 1, fill);
        canvas.drawCircle(const Offset(9.5, 7.5), 1, fill);
        canvas.drawCircle(const Offset(14.5, 7.5), 1, fill);
      case TraceGlyph.monitor:
        canvas.drawRRect(
          RRect.fromRectAndRadius(const Rect.fromLTWH(3, 4, 18, 13), const Radius.circular(2)),
          paint,
        );
        line(8, 21, 16, 21);
        line(12, 17, 12, 21);
      case TraceGlyph.sun:
        circle(12, 12, 3.5);
        for (var i = 0; i < 8; i++) {
          final a = i * math.pi / 4;
          line(
            12 + math.cos(a) * 8,
            12 + math.sin(a) * 8,
            12 + math.cos(a) * 10,
            12 + math.sin(a) * 10,
          );
        }
      case TraceGlyph.moon:
        draw(
          path()
            ..moveTo(20.5, 14.2)
            ..cubicTo(14, 17, 7, 10, 9.8, 3.5)
            ..cubicTo(2, 6, 2.8, 18, 10.5, 20.5)
            ..cubicTo(15.2, 22, 19, 19.3, 20.5, 14.2)
            ..close(),
        );
      case TraceGlyph.check:
        draw(path()..moveTo(5, 12.5)..lineTo(9.2, 16.7)..lineTo(19, 7));
      case TraceGlyph.filter:
        line(4, 6, 20, 6);
        line(7, 12, 17, 12);
        line(10, 18, 14, 18);
      case TraceGlyph.image:
        canvas.drawRRect(
          RRect.fromRectAndRadius(const Rect.fromLTWH(3, 4, 18, 16), const Radius.circular(2)),
          paint,
        );
        circle(16, 9, 2);
        draw(path()..moveTo(5, 18)..lineTo(10, 12)..lineTo(13, 15)..lineTo(16, 12)..lineTo(21, 18));
      case TraceGlyph.video:
        canvas.drawRRect(
          RRect.fromRectAndRadius(const Rect.fromLTWH(3, 5, 14, 14), const Radius.circular(2)),
          paint,
        );
        draw(path()..moveTo(17, 10)..lineTo(21, 7.5)..lineTo(21, 16.5)..lineTo(17, 14));
      case TraceGlyph.file:
        draw(path()..moveTo(6, 3)..lineTo(15, 3)..lineTo(19, 7)..lineTo(19, 21)..lineTo(6, 21)..close());
        draw(path()..moveTo(15, 3)..lineTo(15, 7)..lineTo(19, 7));
      case TraceGlyph.refresh:
        draw(path()..moveTo(19, 8)..lineTo(19, 4)..lineTo(15, 4));
        draw(path()..moveTo(19, 4)..arcTo(const Rect.fromLTWH(4, 4, 16, 16), -math.pi / 2, math.pi * 1.65, false));
      case TraceGlyph.edit:
        draw(path()..moveTo(4, 20)..lineTo(8, 19)..lineTo(19, 8)..lineTo(16, 5)..lineTo(5, 16)..close());
        line(14.5, 6.5, 17.5, 9.5);
      case TraceGlyph.delete:
        draw(path()..moveTo(5, 7)..lineTo(19, 7)..moveTo(9, 7)..lineTo(9, 4)..lineTo(15, 4)..lineTo(15, 7)..moveTo(7, 7)..lineTo(8, 21)..lineTo(16, 21)..lineTo(17, 7));
        line(11, 11, 11, 17);
        line(14, 11, 14, 17);
      case TraceGlyph.directions:
        draw(path()..moveTo(12, 3)..lineTo(21, 12)..lineTo(12, 21)..lineTo(3, 12)..close());
        draw(path()..moveTo(9, 13)..lineTo(9, 10)..lineTo(15, 10)..moveTo(13, 8)..lineTo(15, 10)..lineTo(13, 12));
      case TraceGlyph.externalLink:
        draw(path()..moveTo(13, 4)..lineTo(20, 4)..lineTo(20, 11)..moveTo(20, 4)..lineTo(11, 13));
        draw(path()..moveTo(18, 14)..lineTo(18, 20)..lineTo(4, 20)..lineTo(4, 6)..lineTo(10, 6));
    }
    canvas.restore();
  }

  @override
  bool shouldRepaint(covariant _TraceIconPainter oldDelegate) =>
      glyph != oldDelegate.glyph ||
      color != oldDelegate.color ||
      strokeWidth != oldDelegate.strokeWidth;
}
