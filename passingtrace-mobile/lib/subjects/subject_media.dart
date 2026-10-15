import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:http/http.dart' as http;
import 'package:model_viewer_plus/model_viewer_plus.dart';
import 'package:video_player/video_player.dart';
import 'package:webview_flutter/webview_flutter.dart';

import '../auth_service.dart';
import '../events/media_api.dart';
import '../storylines/storyline_id.dart';

/// Authenticated media stays private. Only the selected embedded GLB and bundled
/// decoders are served through a short lived, unguessable loopback route.
class SubjectMedia extends StatefulWidget {
  const SubjectMedia({
    super.key,
    required this.auth,
    required this.session,
    required this.id,
  });
  final AuthService auth;
  final AuthSession session;
  final String id;
  @override
  State<SubjectMedia> createState() => _SubjectMediaState();
}

class _SubjectMediaState extends State<SubjectMedia> {
  MediaApiClient? _api;
  final _http = http.Client();
  MediaAccessTarget? _target;
  VideoPlayerController? _video;
  WebViewController? _web;
  HttpServer? _server;
  Directory? _directory;
  String? _modelUrl, _error;
  int? _kind;
  int _generation = 0;
  bool _modelLoading = true;
  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didUpdateWidget(covariant SubjectMedia oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.id != widget.id) _load();
  }

  Future<void> _release() async {
    final video = _video, server = _server, directory = _directory;
    _video = null;
    _server = null;
    _directory = null;
    _web = null;
    await video?.dispose();
    await server?.close(force: true);
    if (directory != null && await directory.exists()) {
      await directory.delete(recursive: true);
    }
  }

  Future<void> _load() async {
    final generation = ++_generation;
    setState(() {
      _error = null;
      _kind = null;
      _modelUrl = null;
      _modelLoading = true;
    });
    try {
      await _release();
      final base = await widget.auth.getEventsApiBaseUrl();
      if (!mounted || generation != _generation) return;
      _api ??= MediaApiClient(auth: widget.auth, baseUrl: base);
      final meta = await _api!.metadata(widget.session, widget.id);
      final target = await _api!.access(widget.session, widget.id);
      final kind = (meta['kind'] as num).toInt();
      if (!mounted || generation != _generation) return;
      _target = target;
      if (kind == 2) {
        final controller = VideoPlayerController.networkUrl(
          target.url,
          httpHeaders: target.headers,
        );
        _video = controller;
        controller.addListener(() {
          if (mounted &&
              generation == _generation &&
              controller.value.hasError) {
            setState(() => _error = '视频加载失败，请重新加载附件。');
          }
        });
        await controller.initialize();
      } else if (kind == 4) {
        final directory = await Directory.systemTemp.createTemp(
          'passingtrace-model-',
        );
        if (!mounted || generation != _generation) {
          await directory.delete(recursive: true);
          return;
        }
        _directory = directory;
        final response = await _http.send(
          http.Request('GET', target.url)..headers.addAll(target.headers),
        );
        if (response.statusCode != 200) throw StateError('模型读取失败，请重新加载。');
        final file = File('${directory.path}/model.glb');
        await response.stream.pipe(file.openWrite());
        if (!mounted || generation != _generation) return;
        final nonce = newStorylineKey();
        final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
        if (!mounted || generation != _generation) {
          await server.close(force: true);
          return;
        }
        _server = server;
        const decoders = {
          'draco/draco_wasm_wrapper.js',
          'draco/draco_decoder.js',
          'draco/draco_decoder.wasm',
          'basis/basis_transcoder.js',
          'basis/basis_transcoder.wasm',
          'meshopt_decoder.js',
        };
        server.listen((request) async {
          try {
            request.response.headers.set('Access-Control-Allow-Origin', '*');
            request.response.headers.set('Cache-Control', 'no-store');
            final path = request.uri.path;
            if (request.method != 'GET') {
              request.response.statusCode = 405;
            } else if (path == '/$nonce/model.glb') {
              request.response.headers.set('Content-Type', 'model/gltf-binary');
              await request.response.addStream(file.openRead());
            } else if (path.startsWith('/$nonce/decoders/') &&
                decoders.contains(path.substring('/$nonce/decoders/'.length))) {
              final asset = path.substring('/$nonce/decoders/'.length);
              request.response.headers.set(
                'Content-Type',
                asset.endsWith('.wasm')
                    ? 'application/wasm'
                    : 'text/javascript',
              );
              final bytes = await rootBundle.load(
                'assets/model-decoders/$asset',
              );
              request.response.add(
                bytes.buffer.asUint8List(
                  bytes.offsetInBytes,
                  bytes.lengthInBytes,
                ),
              );
            } else {
              request.response.statusCode = 404;
            }
          } catch (_) {
            request.response.statusCode = 500;
          }
          await request.response.close();
        });
        _modelUrl = 'http://127.0.0.1:${server.port}/$nonce/model.glb';
      }
      if (mounted && generation == _generation) setState(() => _kind = kind);
    } catch (e) {
      if (mounted && generation == _generation) setState(() => _error = '$e');
    }
  }

  @override
  void dispose() {
    _generation++;
    _http.close();
    _api?.close();
    unawaited(_release());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final generation = _generation;
    if (_error != null) {
      return Column(
        children: [
          Semantics(liveRegion: true, child: Text(_error!)),
          TextButton(onPressed: _load, child: const Text('重新加载附件')),
        ],
      );
    }
    if (_kind == null) {
      return const SizedBox(
        height: 100,
        child: Center(child: CircularProgressIndicator()),
      );
    }
    if (_kind == 1) {
      return Image.network(
        _target!.url.toString(),
        headers: _target!.headers,
        fit: BoxFit.contain,
        height: 240,
        errorBuilder: (_, e, stack) =>
            TextButton(onPressed: _load, child: const Text('图片加载失败，重试')),
      );
    }
    if (_kind == 2) {
      return Column(
        children: [
          AspectRatio(
            aspectRatio: _video!.value.aspectRatio,
            child: VideoPlayer(_video!),
          ),
          IconButton(
            tooltip: '播放或暂停视频',
            onPressed: () {
              setState(() {
                _video!.value.isPlaying ? _video!.pause() : _video!.play();
              });
            },
            icon: Icon(
              _video!.value.isPlaying ? Icons.pause : Icons.play_arrow,
            ),
          ),
        ],
      );
    }
    if (_kind != 4) return const Text('此附件不支持预览。');
    final decoders = _modelUrl!.replaceFirst('model.glb', 'decoders/');
    final config = jsonEncode({
      'dracoDecoderLocation': '${decoders}draco/',
      'ktx2TranscoderLocation': '${decoders}basis/',
      'meshoptDecoderLocation': '${decoders}meshopt_decoder.js',
    });
    return Column(
      children: [
        SizedBox(
          height: 300,
          child: Stack(
            children: [
              ModelViewer(
                key: ValueKey(_modelUrl),
                src: _modelUrl!,
                alt: '人物三维模型，可旋转和缩放',
                ar: false,
                cameraControls: true,
                relatedJs:
                    'window.ModelViewerElement=$config; const m=document.querySelector("model-viewer"); m.addEventListener("load",()=>ModelStatus.postMessage("loaded")); m.addEventListener("error",()=>ModelStatus.postMessage("error"));',
                javascriptChannels: {
                  JavascriptChannel(
                    'ModelStatus',
                    onMessageReceived: (message) {
                      if (mounted && generation == _generation) {
                        setState(() {
                          _modelLoading = false;
                          if (message.message == 'error') {
                            _error = '三维模型加载失败，请重试。';
                          }
                        });
                      }
                    },
                  ),
                },
                onWebViewCreated: (controller) {
                  if (!mounted || generation != _generation) return;
                  _web = controller;
                  controller.setNavigationDelegate(
                    NavigationDelegate(
                      onNavigationRequest: (request) {
                        final uri = Uri.tryParse(request.url);
                        return uri?.scheme == 'http' &&
                                (uri?.host == '127.0.0.1' ||
                                    uri?.host == 'localhost')
                            ? NavigationDecision.navigate
                            : NavigationDecision.prevent;
                      },
                    ),
                  );
                },
              ),
              if (_modelLoading)
                const Center(child: CircularProgressIndicator()),
            ],
          ),
        ),
        TextButton(
          onPressed: () => _web?.runJavaScript(
            'const m=document.querySelector("model-viewer"); m.cameraOrbit="auto auto auto"; m.cameraTarget="auto auto auto"; m.fieldOfView="auto"; m.jumpCameraToGoal();',
          ),
          child: const Text('重置三维视角'),
        ),
      ],
    );
  }
}
