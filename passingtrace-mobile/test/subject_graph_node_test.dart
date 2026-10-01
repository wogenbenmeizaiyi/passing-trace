import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/events/media_api.dart';
import 'package:passingtrace_mobile/profile/account_avatar.dart';
import 'package:passingtrace_mobile/subjects/subject_avatar.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_canvas.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_controller.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_layout.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_node.dart';
import 'package:passingtrace_mobile/subjects/subject_model.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

class _Headers implements HttpHeaders {
  final values = <String, Object>{};
  @override
  void add(String name, Object value, {bool preserveHeaderCase = false}) =>
      values[name] = value;
  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _Response extends Stream<List<int>> implements HttpClientResponse {
  _Response(this.bytes, this.statusCode, {this.chunks});
  final Uint8List bytes;
  final Stream<List<int>>? chunks;
  @override
  final int statusCode;
  @override
  int get contentLength => bytes.length;
  @override
  HttpClientResponseCompressionState get compressionState =>
      HttpClientResponseCompressionState.notCompressed;
  @override
  StreamSubscription<List<int>> listen(
    void Function(List<int>)? onData, {
    Function? onError,
    void Function()? onDone,
    bool? cancelOnError,
  }) => (chunks ?? Stream<List<int>>.value(bytes)).listen(
    onData,
    onError: onError,
    onDone: onDone,
    cancelOnError: cancelOnError,
  );
  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _Request implements HttpClientRequest {
  _Request(this.bytes, this.status, {this.chunks});
  final Uint8List bytes;
  final Stream<List<int>>? chunks;
  final int status;
  @override
  final _Headers headers = _Headers();
  @override
  Future<HttpClientResponse> close() async =>
      _Response(bytes, status, chunks: chunks);
  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _Http implements HttpClient {
  _Http(this.bytes);
  Uint8List bytes;
  Stream<List<int>>? chunks;
  int status = 200;
  Uri? url;
  _Request? request;
  @override
  Future<HttpClientRequest> getUrl(Uri url) async {
    this.url = url;
    return request = _Request(bytes, status, chunks: chunks);
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

Future<Uint8List> _png() async {
  final recorder = ui.PictureRecorder();
  ui.Canvas(recorder).drawRect(
    const ui.Rect.fromLTWH(0, 0, 64, 32),
    ui.Paint()..color = Colors.blue,
  );
  final picture = recorder.endRecording(),
      image = await picture.toImage(64, 32);
  try {
    return (await image.toByteData(format: ui.ImageByteFormat.png))!.buffer
        .asUint8List();
  } finally {
    image.dispose();
    picture.dispose();
  }
}

SubjectModel _cat({String id = 'cat'}) => SubjectModel({
  'id': id,
  'name': '奶糖 · 演示母猫',
  'kind': 1,
  'state': 1,
  'coverMediaId': 'photo',
});

Future<MediaAccessTarget> _cover(String path) async => MediaAccessTarget(
  url: Uri.parse('https://media.test/$path'),
  headers: const {'Authorization': 'Bearer test'},
);

Widget _app(Widget home, {double scale = 1}) => MaterialApp(
  theme: PassingTraceTheme.light(PassingTracePalette.pine),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(scale)),
    child: child!,
  ),
  home: Scaffold(body: home),
);

Widget _node(SubjectModel subject, {Future<MediaAccessTarget>? cover}) =>
    Center(
      child: SizedBox.square(
        dimension: subjectNodeDiameter,
        child: Material(
          shape: const CircleBorder(),
          clipBehavior: Clip.antiAlias,
          child: SubjectGraphNode(
            subject: subject,
            nickname: '我',
            cover: cover,
          ),
        ),
      ),
    );

Future<void> _imageReady(WidgetTester tester) async {
  await tester.pump();
  await tester.runAsync(
    () => Future<void>.delayed(const Duration(milliseconds: 80)),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('头像地址请求、首字节等待和分块下载期间持续显示占位，解码后才显示照片', (tester) async {
    final bytes = (await tester.runAsync(_png))!;
    for (final graph in [false, true]) {
      final chunks = StreamController<List<int>>();
      final http = _Http(bytes)..chunks = chunks.stream;
      final cover = Completer<MediaAccessTarget>();
      debugNetworkImageHttpClientProvider = () => http;
      try {
        await tester.pumpWidget(
          _app(
            graph
                ? _node(_cat(), cover: cover.future)
                : Center(
                    child: SubjectAvatar(subject: _cat(), cover: cover.future),
                  ),
          ),
        );
        expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
        cover.complete(
          MediaAccessTarget(
            url: Uri.parse('https://media.test/slow-$graph'),
            headers: const {'Authorization': 'Bearer test'},
          ),
        );
        await tester.pump();
        await tester.pump();
        expect(http.request, isNotNull);
        expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
        expect(find.byType(RawImage), findsNothing);
        await tester.pump(const Duration(seconds: 2));
        expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
        chunks.add(bytes.sublist(0, bytes.length ~/ 2));
        await tester.pump();
        expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
        chunks.add(bytes.sublist(bytes.length ~/ 2));
        final finished = chunks.close();
        await tester.pump();
        await finished;
        // Download completion still leaves the asynchronous decoder to finish.
        expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
        await _imageReady(tester);
        expect(find.byIcon(Icons.pets_outlined), findsNothing);
        expect(tester.widget<RawImage>(find.byType(RawImage)).image, isNotNull);
        expect(tester.takeException(), isNull);
      } finally {
        debugNetworkImageHttpClientProvider = null;
        if (!chunks.isClosed) {
          final closed = chunks.close();
          await tester.pumpWidget(const SizedBox());
          await closed;
        }
      }
    }
  });

  testWidgets('本地预览和账号头像解码前显示默认头像，解码失败也不留空', (tester) async {
    final bytes = (await tester.runAsync(_png))!;
    await tester.pumpWidget(
      _app(
        Row(
          children: [
            SubjectAvatar(subject: _cat(), imageBytes: bytes),
            AccountAvatar(bytes: bytes),
          ],
        ),
      ),
    );
    expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
    final account = find.descendant(
      of: find.byType(AccountAvatar),
      matching: find.byType(CustomPaint),
    );
    expect(account, findsOneWidget);
    expect(find.byType(RawImage), findsNothing);
    await _imageReady(tester);
    expect(find.byIcon(Icons.pets_outlined), findsNothing);
    expect(account, findsNothing);
    expect(find.byType(RawImage), findsNWidgets(2));
    await tester.pumpWidget(
      _app(
        Row(
          children: [
            SubjectAvatar(subject: _cat(), imageBytes: Uint8List.fromList([0])),
            AccountAvatar(bytes: Uint8List.fromList([0])),
          ],
        ),
      ),
    );
    await _imageReady(tester);
    expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
    expect(account, findsOneWidget);
    expect(find.byType(RawImage), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('普通头像下载失败或图片损坏持续显示类型占位', (tester) async {
    final http = _Http(Uint8List.fromList([0, 1, 2]));
    debugNetworkImageHttpClientProvider = () => http;
    try {
      for (final status in [200, 404]) {
        http.status = status;
        await tester.pumpWidget(
          _app(
            SubjectAvatar(
              subject: _cat(),
              cover: _cover('avatar-error-$status'),
            ),
          ),
        );
        await _imageReady(tester);
        expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
        expect(find.byType(RawImage), findsNothing);
        expect(tester.takeException(), isNull);
      }
    } finally {
      debugNetworkImageHttpClientProvider = null;
    }
  });

  testWidgets('私有头像铺满节点，保留认证头，名称状态叠在照片底部', (tester) async {
    final http = _Http((await tester.runAsync(_png))!);
    debugNetworkImageHttpClientProvider = () => http;
    try {
      await tester.pumpWidget(_app(_node(_cat(), cover: _cover('full'))));
      await _imageReady(tester);
      expect(http.url, Uri.parse('https://media.test/full'));
      expect(http.request!.headers.values['Authorization'], 'Bearer test');
      final photo = find.byKey(const ValueKey('subject-node-photo-cat'));
      expect(tester.getSize(photo), const Size.square(subjectNodeDiameter));
      expect(tester.getRect(find.byType(RawImage)), tester.getRect(photo));
      expect(tester.widget<Image>(find.byType(Image)).fit, BoxFit.cover);
      expect(find.byType(SubjectAvatar), findsNothing);
      final name = find.text('奶糖 · 演示母猫'), state = find.text('已结束');
      expect(
        tester.getCenter(name).dy,
        greaterThan(tester.getCenter(photo).dy),
      );
      expect(
        tester.getBottomLeft(state).dy,
        lessThan(tester.getBottomLeft(photo).dy),
      );
      expect(tester.widget<Text>(name).style!.color, Colors.white);
      expect(tester.widget<Text>(state).style!.color, Colors.white);
      expect(tester.takeException(), isNull);
    } finally {
      debugNetworkImageHttpClientProvider = null;
    }
  });

  testWidgets('未设置、加载中、接口失败及损坏图片均回退完整图标节点', (tester) async {
    final http = _Http(Uint8List.fromList([0, 1, 2]));
    debugNetworkImageHttpClientProvider = () => http;
    try {
      final pending = Completer<MediaAccessTarget>();
      for (final cover in <Future<MediaAccessTarget>? Function()>[
        () => null,
        () => pending.future,
        () => _cover('invalid'),
      ]) {
        await tester.pumpWidget(_app(_node(_cat(), cover: cover())));
        await _imageReady(tester);
        expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
        expect(find.text('奶糖 · 演示母猫'), findsOneWidget);
        expect(find.text('已结束'), findsOneWidget);
        expect(
          find.byKey(const ValueKey('subject-node-photo-cat')),
          findsNothing,
        );
        expect(tester.takeException(), isNull);
      }
      final failed = Completer<MediaAccessTarget>();
      await tester.pumpWidget(_app(_node(_cat(), cover: failed.future)));
      failed.completeError(StateError('无法读取'));
      await tester.pumpAndSettle();
      expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
      expect(tester.takeException(), isNull);
      http.status = 404;
      await tester.pumpWidget(_app(_node(_cat(), cover: _cover('expired'))));
      await _imageReady(tester);
      expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
      expect(tester.takeException(), isNull);
    } finally {
      debugNetworkImageHttpClientProvider = null;
    }
  });

  testWidgets('自身头像同样铺满圆形节点，大字体下保留点击与拖动', (tester) async {
    tester.view.physicalSize = const Size(360, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final graph = SubjectGraph.fromJson({
      'rootId': 'self',
      'nodes': [
        {'id': 'self', 'isSelf': true, 'name': '自己', 'kind': 0, 'state': 0},
      ],
      'relations': [],
    });
    final controller = SubjectGraphController(graph), opened = <String>[];
    final bytes = (await tester.runAsync(_png))!;
    await tester.pumpWidget(
      _app(
        SubjectGraphCanvas(
          graph: graph,
          controller: controller,
          matches: const {'self'},
          kept: const {'self'},
          nickname: '很长的昵称用于测试换行',
          avatar: bytes,
          onOpen: opened.add,
        ),
        scale: 1.8,
      ),
    );
    await _imageReady(tester);
    final node = find.byKey(const ValueKey('subject-node-self'));
    final photo = find.byKey(const ValueKey('subject-node-photo-self'));
    expect(tester.getRect(photo), tester.getRect(node));
    final material = tester.widget<Material>(
      find.descendant(of: node, matching: find.byType(Material)),
    );
    expect(material.shape, isA<CircleBorder>());
    expect(material.clipBehavior, Clip.antiAlias);
    expect(find.text('进行中'), findsOneWidget);
    await tester.drag(node, const Offset(35, 20));
    await tester.pumpAndSettle();
    expect(controller.positions['self']!.distance, greaterThan(0));
    expect(opened, isEmpty);
    await tester.tap(node);
    await tester.pumpAndSettle();
    expect(opened, ['self']);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    controller.dispose();
  });
}
