import 'dart:async';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:cross_file/cross_file.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/events/event_model.dart';
import 'package:passingtrace_mobile/events/media_api.dart';
import 'package:passingtrace_mobile/profile/avatar_crop_view.dart';
import 'package:passingtrace_mobile/subjects/subject_avatar.dart';
import 'package:passingtrace_mobile/subjects/subject_avatar_editor.dart';
import 'package:passingtrace_mobile/subjects/subject_form_view.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_canvas.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_controller.dart';
import 'package:passingtrace_mobile/subjects/subject_graph_node.dart';
import 'package:passingtrace_mobile/subjects/subject_list_view.dart';
import 'package:passingtrace_mobile/subjects/subject_model.dart';
import 'package:passingtrace_mobile/subjects/subjects_api.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

class _Auth extends AuthService {}

const _session = AuthSession(
  identityBaseUrl: 'https://identity.test',
  deviceId: 'test',
  deviceSecret: 'test',
  accessToken: 'test',
);

SubjectModel _subject({String? avatar = 'old-avatar', bool self = false}) =>
    SubjectModel({
      'id': self ? 'self' : 'car',
      'name': self ? '自己' : '小汽车',
      'isSelf': self,
      'kind': self ? 0 : 2,
      'itemType': 'vehicle',
      'state': 0,
      'version': 7,
      'timezone': 'Asia/Shanghai',
      'fields': [],
      'values': {},
      'mediaIds': ['other-media'],
      'coverMediaId': avatar,
    });

class _Subjects extends SubjectsApi {
  _Subjects() : super(auth: _Auth(), baseUrl: 'https://events.test');
  SubjectModel current = _subject();
  Map<String, dynamic>? saved;
  int? savedVersion;
  @override
  Future<List<SubjectModel>> list(AuthSession session) async => [
    _subject(self: true),
    current,
  ];
  @override
  Future<SubjectModel> get(AuthSession session, String id) async => current;
  @override
  Future<SubjectModel> update(
    AuthSession session,
    String id,
    Map<String, dynamic> body,
    int version,
  ) async {
    expect(id, 'car');
    saved = body;
    savedVersion = version;
    return current = SubjectModel({
      ...current.json,
      ...body,
      'coverMediaId': body['clearCover'] == true ? null : body['coverMediaId'],
      'version': version + 1,
    });
  }
}

class _Media extends MediaApiClient {
  _Media() : super(auth: _Auth(), baseUrl: 'https://events.test');
  int uploads = 0;
  bool fail = false;
  Uint8List? bytes;
  Completer<void>? gate;
  @override
  Future<MediaAccessTarget> access(AuthSession session, String id) async =>
      throw StateError('头像暂不可读取');
  @override
  Future<PendingMediaUpload> upload(
    AuthSession session,
    PlatformFile file, {
    UploadProgress? onProgress,
  }) async {
    uploads++;
    bytes = await file.readAsBytes();
    expect(file.name, 'subject-avatar.png');
    expect(await file.length(), bytes!.length);
    onProgress?.call(.5);
    await gate?.future;
    if (fail) throw StateError('模拟上传失败');
    return PendingMediaUpload(
      file: file,
      contentType: 'image/png',
      id: 'new-avatar',
      kind: MediaKind.image,
    );
  }
}

final class _File extends PlatformFile {
  _File(Uint8List bytes, {this.size})
    : xFile = XFile.fromData(bytes, name: 'photo.png');
  final int? size;
  @override
  final XFile xFile;
  @override
  String get name => 'photo.png';
  @override
  Uri get uri => Uri.parse(xFile.path);
  @override
  int? lengthSync() => size;
  @override
  Future<int> length() async => size ?? await xFile.length();
  @override
  Future<Uint8List> readAsBytes() => xFile.readAsBytes();
  @override
  Stream<Uint8List> readAsByteStream() => xFile.openRead();
}

Future<Uint8List> _png() async {
  final recorder = ui.PictureRecorder();
  ui.Canvas(recorder).drawRect(
    const ui.Rect.fromLTWH(0, 0, 64, 32),
    ui.Paint()..color = Colors.green,
  );
  final picture = recorder.endRecording();
  final image = await picture.toImage(64, 32);
  try {
    return (await image.toByteData(format: ui.ImageByteFormat.png))!.buffer
        .asUint8List();
  } finally {
    image.dispose();
    picture.dispose();
  }
}

Widget _app(Widget home, {double scale = 1}) => MaterialApp(
  theme: PassingTraceTheme.light(PassingTracePalette.pine),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(scale)),
    child: child!,
  ),
  home: home,
);

Future<void> _imageReady(WidgetTester tester) async {
  await tester.pump();
  await tester.runAsync(
    () => Future<void>.delayed(const Duration(milliseconds: 80)),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('头像裁剪后保存真实媒体 ID 与档案版本，移除附件不清除头像', (tester) async {
    tester.view.physicalSize = const Size(360, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final file = _File((await tester.runAsync(_png))!);
    final api = _Subjects(), media = _Media()..gate = Completer<void>();
    addTearDown(api.close);
    addTearDown(media.close);
    await tester.pumpWidget(
      _app(
        Builder(
          builder: (context) => Scaffold(
            body: FilledButton(
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) => SubjectFormView(
                    auth: _Auth(),
                    session: _session,
                    subject: api.current,
                    apiClient: api,
                    mediaApiClient: media,
                    avatarImagePicker: () async => file,
                  ),
                ),
              ),
              child: const Text('编辑'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('编辑'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('更换头像'));
    await _imageReady(tester);
    expect(find.byType(AvatarCropView), findsOneWidget);
    expect(media.uploads, 0);
    await tester.scrollUntilVisible(
      find.text('使用这张头像'),
      150,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.text('使用这张头像'));
    await _imageReady(tester);
    expect(media.uploads, 1);
    expect(find.text('正在设置头像…'), findsOneWidget);
    final save = find.widgetWithText(FilledButton, '保存档案');
    expect(tester.widget<FilledButton>(save).onPressed, isNull);
    expect(
      tester
          .widget<TextButton>(find.widgetWithText(TextButton, '更换头像'))
          .onPressed,
      isNull,
    );
    media.gate!.complete();
    await tester.pumpAndSettle();
    final preview = tester.widget<SubjectAvatar>(find.byType(SubjectAvatar));
    expect(preview.subject.coverMediaId, 'new-avatar');
    expect(preview.imageBytes, isNotNull);
    await tester.runAsync(() async {
      final codec = await ui.instantiateImageCodec(media.bytes!);
      final frame = await codec.getNextFrame();
      expect(frame.image.width, 512);
      expect(frame.image.height, 512);
      frame.image.dispose();
      codec.dispose();
    });
    expect(api.saved, isNull);
    await tester.scrollUntilVisible(
      find.byTooltip('移除附件'),
      300,
      scrollable: find.byType(Scrollable).first,
    );
    await Scrollable.ensureVisible(
      tester.element(find.widgetWithText(ListTile, '附件 1')),
      alignment: .5,
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('移除附件'));
    await tester.tap(save);
    await tester.pumpAndSettle();
    expect(api.savedVersion, 7);
    expect(api.saved!['coverMediaId'], 'new-avatar');
    expect(api.saved!['clearCover'], isFalse);
    expect(api.saved!['mediaIds'], isEmpty);
    expect(find.byType(SubjectFormView), findsNothing);
    await tester.tap(find.text('编辑'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('移除头像'));
    await tester.tap(save);
    await tester.pumpAndSettle();
    expect(api.savedVersion, 8);
    expect(api.saved!['coverMediaId'], isNull);
    expect(api.saved!['clearCover'], isTrue);
    expect(tester.takeException(), isNull);
  });

  testWidgets('取消选图、取消裁剪、超限和上传失败都保留原头像，可重试', (tester) async {
    final bytes = (await tester.runAsync(_png))!;
    final media = _Media();
    addTearDown(media.close);
    PlatformFile? picked;
    String? selected = 'old-avatar';
    final busy = <bool>[];
    var changes = 0;
    await tester.pumpWidget(
      _app(
        Scaffold(
          body: StatefulBuilder(
            builder: (context, update) => SubjectAvatarEditor(
              auth: _Auth(),
              session: _session,
              subject: _subject(avatar: selected),
              apiClient: media,
              imagePicker: () async => picked,
              onBusy: busy.add,
              onChanged: (id) => update(() {
                selected = id;
                changes++;
              }),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('更换头像'));
    await tester.pumpAndSettle();
    expect(selected, 'old-avatar');
    picked = _File(bytes);
    await tester.tap(find.text('更换头像'));
    await _imageReady(tester);
    await tester.tap(find.byTooltip('取消裁剪'));
    await tester.pumpAndSettle();
    expect(selected, 'old-avatar');
    expect(media.uploads, 0);
    picked = _File(bytes, size: 6 * 1024 * 1024);
    await tester.tap(find.text('更换头像'));
    await tester.pumpAndSettle();
    expect(find.textContaining('不能超过 5MB'), findsOneWidget);
    expect(find.byType(AvatarCropView), findsNothing);
    picked = _File(bytes);
    media.fail = true;
    await tester.tap(find.text('更换头像'));
    await _imageReady(tester);
    await tester.scrollUntilVisible(
      find.text('使用这张头像'),
      150,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.text('使用这张头像'));
    await _imageReady(tester);
    expect(selected, 'old-avatar');
    expect(changes, 0);
    expect(find.textContaining('原头像已保留'), findsOneWidget);
    expect(busy, [true, false, true, false, true, false, true, false]);
    expect(
      tester
          .widget<TextButton>(find.widgetWithText(TextButton, '更换头像'))
          .onPressed,
      isNotNull,
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('列表和关系图读取同一头像，自己沿用账号头像，缺失头像回退类型图标', (tester) async {
    final graph = SubjectGraph.fromJson({
      'rootId': 'self',
      'nodes': [
        _subject(self: true).json,
        _subject().json,
        {..._subject(avatar: null).json, 'id': 'cat', 'name': '猫', 'kind': 1},
      ],
      'relations': [
        for (final id in ['car', 'cat'])
          {
            'id': id,
            'fromSubjectId': 'self',
            'toSubjectId': id,
            'directed': false,
            'label': '拥有',
          },
      ],
    });
    final controller = SubjectGraphController(graph);
    addTearDown(controller.dispose);
    final loads = <String>[];
    final covers = <String, Future<MediaAccessTarget>>{};
    Future<MediaAccessTarget> load(String id) => covers.putIfAbsent(id, () {
      loads.add(id);
      return Future.error(StateError('不可读取'));
    });
    final ids = graph.nodes.map((subject) => subject.id).toSet();
    await tester.pumpWidget(
      _app(
        Scaffold(
          body: SubjectListView(
            graph: graph,
            matches: ids,
            kept: ids,
            nickname: '昵称',
            onOpen: (_) {},
            coverLoader: load,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(loads, ['old-avatar']);
    expect(find.byIcon(Icons.pets_outlined), findsOneWidget);
    expect(find.byIcon(Icons.directions_car_outlined), findsOneWidget);
    await tester.pumpWidget(
      _app(
        Scaffold(
          body: SubjectGraphCanvas(
            graph: graph,
            controller: controller,
            matches: ids,
            kept: ids,
            nickname: '昵称',
            onOpen: (_) {},
            coverLoader: load,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    final car = find.descendant(
      of: find.byKey(const ValueKey('subject-node-car')),
      matching: find.byType(SubjectGraphNode),
    );
    final cat = find.descendant(
      of: find.byKey(const ValueKey('subject-node-cat')),
      matching: find.byType(SubjectGraphNode),
    );
    expect(tester.widget<SubjectGraphNode>(car).cover, covers['old-avatar']);
    expect(tester.widget<SubjectGraphNode>(cat).cover, isNull);
    expect(loads, ['old-avatar']);
    expect(tester.takeException(), isNull);
  });

  testWidgets('头像编辑适配小屏、大字体和键盘，不出现横向溢出', (tester) async {
    tester.view.physicalSize = const Size(320, 568);
    tester.view.devicePixelRatio = 1;
    tester.view.viewInsets = const FakeViewPadding(bottom: 260);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetViewInsets);
    String? selected;
    await tester.pumpWidget(
      _app(
        Scaffold(
          body: ListView(
            children: [
              SubjectAvatarEditor(
                auth: _Auth(),
                session: _session,
                subject: _subject(avatar: null),
                imagePicker: () async => null,
                onChanged: (id) => selected = id,
              ),
            ],
          ),
        ),
        scale: 1.8,
      ),
    );
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('设置头像'));
    await tester.tap(find.text('设置头像'));
    await tester.pumpAndSettle();
    expect(selected, isNull);
    expect(tester.takeException(), isNull);
  });
}
