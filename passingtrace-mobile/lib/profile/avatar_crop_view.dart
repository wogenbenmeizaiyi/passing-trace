import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';

Rect avatarCropRect(int width, int height, double zoom, double x, double y) {
  final side = (width < height ? width : height) / zoom.clamp(1, 3);
  return Rect.fromLTWH(
    (width - side) * x.clamp(0, 1),
    (height - side) * y.clamp(0, 1),
    side,
    side,
  );
}

class AvatarCropView extends StatefulWidget {
  const AvatarCropView({super.key, required this.bytes});
  final Uint8List bytes;
  @override
  State<AvatarCropView> createState() => _AvatarCropViewState();
}

class _AvatarCropViewState extends State<AvatarCropView> {
  ui.Image? _image;
  double _zoom = 1, _x = .5, _y = .5;
  String? _error;
  bool _busy = false;
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    ui.ImmutableBuffer? buffer;
    ui.ImageDescriptor? descriptor;
    try {
      buffer = await ui.ImmutableBuffer.fromUint8List(widget.bytes);
      descriptor = await ui.ImageDescriptor.encoded(buffer);
      try {
        if (descriptor.width * descriptor.height > 32000000) {
          throw const FormatException();
        }
        final codec = await descriptor.instantiateCodec();
        try {
          final frame = await codec.getNextFrame();
          if (!mounted) {
            frame.image.dispose();
            return;
          }
          setState(() => _image = frame.image);
        } finally {
          codec.dispose();
        }
      } finally {
        descriptor.dispose();
        descriptor = null;
      }
    } catch (_) {
      if (mounted) setState(() => _error = '图片无法读取或尺寸过大，请重新选择。');
    } finally {
      descriptor?.dispose();
      buffer?.dispose();
    }
  }

  @override
  void dispose() {
    _image?.dispose();
    super.dispose();
  }

  Future<void> _confirm() async {
    if (_image == null || _busy) return;
    setState(() => _busy = true);
    try {
      final recorder = ui.PictureRecorder();
      final canvas = Canvas(recorder);
      canvas.drawImageRect(
        _image!,
        avatarCropRect(_image!.width, _image!.height, _zoom, _x, _y),
        const Rect.fromLTWH(0, 0, 512, 512),
        Paint()..filterQuality = FilterQuality.high,
      );
      final picture = recorder.endRecording();
      final image = await picture.toImage(512, 512);
      try {
        final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
        if (mounted && bytes != null) {
          Navigator.pop(context, bytes.buffer.asUint8List());
        }
      } finally {
        image.dispose();
        picture.dispose();
      }
    } catch (_) {
      if (mounted) setState(() => _error = '裁剪失败，请重新选择图片。');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: TraceAppBar(
      title: '调整头像',
      leading: TraceIconButton(
        glyph: TraceGlyph.chevronLeft,
        tooltip: '取消裁剪',
        onPressed: () => Navigator.pop(context),
      ),
    ),
    body: SafeArea(
      child: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          const Text('调整图片的位置和大小，圆形区域是头像展示效果。'),
          const SizedBox(height: 24),
          Center(
            child: SizedBox.square(
              dimension: 240,
              child: ClipOval(
                child: _image == null
                    ? Center(
                        child: _error == null
                            ? const CircularProgressIndicator()
                            : const TraceIcon(TraceGlyph.image),
                      )
                    : CustomPaint(
                        painter: _CropPainter(
                          _image!,
                          avatarCropRect(
                            _image!.width,
                            _image!.height,
                            _zoom,
                            _x,
                            _y,
                          ),
                        ),
                      ),
              ),
            ),
          ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 16),
              child: Text(_error!),
            ),
          const SizedBox(height: 24),
          _slider('缩放', _zoom, 1, 3, (value) => _zoom = value),
          _slider('左右位置', _x, 0, 1, (value) => _x = value),
          _slider('上下位置', _y, 0, 1, (value) => _y = value),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: _image == null || _busy ? null : _confirm,
            child: Text(_busy ? '正在处理…' : '使用这张头像'),
          ),
          TextButton(
            onPressed: _busy ? null : () => Navigator.pop(context),
            child: const Text('取消'),
          ),
        ],
      ),
    ),
  );
  Widget _slider(
    String label,
    double value,
    double min,
    double max,
    void Function(double) update,
  ) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(label),
      Slider(
        value: value,
        min: min,
        max: max,
        semanticFormatterCallback: (value) =>
            '$label ${(value * 100).round()}%',
        onChanged: _image == null || _busy
            ? null
            : (value) => setState(() => update(value)),
      ),
    ],
  );
}

class _CropPainter extends CustomPainter {
  _CropPainter(this.image, this.source);
  final ui.Image image;
  final Rect source;
  @override
  void paint(Canvas canvas, Size size) => canvas.drawImageRect(
    image,
    source,
    Offset.zero & size,
    Paint()..filterQuality = FilterQuality.high,
  );
  @override
  bool shouldRepaint(_CropPainter oldDelegate) =>
      oldDelegate.image != image || oldDelegate.source != source;
}
