import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:passingtrace_mobile/auth_service.dart';
import 'package:passingtrace_mobile/profile/account_center_view.dart';
import 'package:passingtrace_mobile/profile/avatar_crop_view.dart';
import 'package:passingtrace_mobile/profile/profile_api.dart';
import 'package:passingtrace_mobile/profile/profile_controller.dart';
import 'package:passingtrace_mobile/theme/passingtrace_theme.dart';

class _Auth extends AuthService {
  @override
  Future<AuthSession> ensureFreshToken(
    AuthSession current, {
    bool forceRefresh = false,
  }) async => current;
}

const _session = AuthSession(
  identityBaseUrl: 'https://identity.test',
  deviceId: 'one',
  deviceSecret: 'test',
  accessToken: 'test-token',
);
AccountProfile _profile({String nickname = '我的星期八', String version = 'v1'}) =>
    AccountProfile(
      username: 'login_user',
      nickname: nickname,
      bio: '喜欢散步，也喜欢记录日常。',
      createdAt: DateTime.utc(2026, 9, 1),
      version: version,
      hasAvatar: false,
    );

class _Api extends ProfileApi {
  _Api() : super(_Auth());
  AccountProfile current = _profile();
  int saves = 0;
  bool conflict = false;
  @override
  Future<AccountProfile> get(AuthSession session) async => current;
  @override
  Future<AccountProfile> save(
    AuthSession session, {
    required String nickname,
    required String bio,
    required String version,
    Uint8List? avatar,
    bool removeAvatar = false,
  }) async {
    saves++;
    if (conflict) throw const ProfileException(409);
    current = _profile(nickname: nickname, version: 'v2');
    return current;
  }
}

class _PendingApi extends _Api {
  final requests = <Completer<AccountProfile>>[];
  @override
  Future<AccountProfile> get(AuthSession session) {
    final pending = Completer<AccountProfile>();
    requests.add(pending);
    return pending.future;
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUpAll(() async {
    const font = String.fromEnvironment('PROFILE_SCREENSHOT_FONT');
    if (const bool.fromEnvironment('PROFILE_SCREENSHOTS') && font.isNotEmpty) {
      final bytes = ByteData.sublistView(await File(font).readAsBytes());
      // Load only for local screenshots; no system font is copied into the app/repo.
      await (FontLoader('ProfilePreview')..addFont(Future.value(bytes))).load();
    }
  });
  test(
    'profile protocol uses Identity, bearer authentication and multipart',
    () async {
      final client = MockClient((request) async {
        expect(
          request.url.toString(),
          'https://identity.test/api/v1/account/profile',
        );
        expect(request.headers['Authorization'], 'Bearer test-token');
        expect(
          request.headers['content-type'],
          contains('multipart/form-data'),
        );
        expect(request.body, contains('name="version"'));
        expect(request.body, contains('name="avatar"'));
        return http.Response(
          jsonEncode({
            'username': 'user',
            'nickname': '昵称',
            'bio': '',
            'createdAt': '2026-09-01T00:00:00Z',
            'version': 'v2',
            'hasAvatar': true,
          }),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      });
      final api = ProfileApi(_Auth(), client: client);
      final result = await api.save(
        _session,
        nickname: '昵称',
        bio: '',
        version: 'v1',
        avatar: Uint8List.fromList([1, 2]),
      );
      expect(result.nickname, '昵称');
      api.dispose();
    },
  );
  test(
    'profile errors are Chinese and never expose implementation details',
    () async {
      final api = ProfileApi(
        _Auth(),
        client: MockClient(
          (_) async => http.Response('SQL password private', 503),
        ),
      );
      await expectLater(
        api.get(_session),
        throwsA(
          isA<ProfileException>().having(
            (error) => error.message,
            'message',
            contains('暂时无法连接'),
          ),
        ),
      );
      api.dispose();
    },
  );
  test('crop rectangle handles landscape, portrait and clamped positions', () {
    expect(
      avatarCropRect(800, 400, 1, .5, .5),
      const Rect.fromLTWH(200, 0, 400, 400),
    );
    expect(
      avatarCropRect(400, 800, 2, 1, 0),
      const Rect.fromLTWH(200, 0, 200, 200),
    );
  });
  test(
    'a stale background response cannot overwrite a saved profile',
    () async {
      final api = _PendingApi();
      final controller = ProfileController(api: api, session: () => _session);
      final refresh = controller.refresh();
      await controller.save(nickname: '已经保存', bio: '', version: 'v1');
      api.requests.single.complete(_profile(nickname: '旧昵称'));
      await refresh;
      expect(controller.nickname, '已经保存');
      controller.dispose();
    },
  );
  testWidgets(
    'user center opens editor, saves nickname and returns to original profile',
    (tester) async {
      final api = _Api();
      final controller = ProfileController(api: api, session: () => _session);
      await _mount(tester, controller);
      await tester.tap(find.text('编辑个人资料'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextFormField).first, '新昵称');
      await tester.scrollUntilVisible(
        find.text('保存修改'),
        250,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.text('保存修改'));
      await tester.pumpAndSettle();
      expect(api.saves, 1);
      expect(find.text('新昵称'), findsOneWidget);
      expect(find.text('用户中心'), findsOneWidget);
      expect(find.text('login_user'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      controller.dispose();
    },
  );
  testWidgets(
    'conflicts keep unsaved values and cancel requires confirmation',
    (tester) async {
      final api = _Api()..conflict = true;
      final controller = ProfileController(api: api, session: () => _session);
      await _mount(tester, controller);
      await tester.tap(find.text('编辑个人资料'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextFormField).first, '未保存的昵称');
      await tester.scrollUntilVisible(
        find.text('保存修改'),
        250,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.text('保存修改'));
      await tester.pumpAndSettle();
      expect(find.textContaining('资料已在另一处更新'), findsWidgets);
      expect(find.text('未保存的昵称'), findsOneWidget);
      await tester.pump(const Duration(seconds: 5));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('取消'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.text('取消'));
      await tester.pumpAndSettle();
      expect(find.text('放弃修改？'), findsOneWidget);
      await tester.tap(find.text('继续编辑'));
      await tester.pumpAndSettle();
      expect(find.text('未保存的昵称'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      controller.dispose();
    },
  );
  for (final dark in [false, true]) {
    testWidgets(
      'profile supports small screens and enlarged text in ${dark ? 'dark' : 'light'} theme',
      (tester) async {
        tester.view.physicalSize = const Size(375, 900);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final controller = ProfileController(
          api: _Api(),
          session: () => _session,
        );
        await _mount(tester, controller, dark: dark, scale: 1.5);
        expect(tester.takeException(), isNull);
        if (const bool.fromEnvironment('PROFILE_SCREENSHOTS')) {
          await expectLater(
            find.byType(MaterialApp),
            matchesGoldenFile(
              '../../artifacts/profile-mobile-${dark ? 'dark' : 'light'}.png',
            ),
          );
        }
        await tester.tap(find.text('编辑个人资料'));
        await tester.pumpAndSettle();
        expect(tester.takeException(), isNull);
        tester.view.physicalSize = const Size(900, 375);
        await tester.pumpAndSettle();
        await tester.scrollUntilVisible(
          find.text('保存修改'),
          250,
          scrollable: find.byType(Scrollable).first,
        );
        expect(tester.takeException(), isNull);
        await tester.pumpWidget(const SizedBox());
        controller.dispose();
      },
    );
  }
}

Future<void> _mount(
  WidgetTester tester,
  ProfileController controller, {
  bool dark = false,
  double scale = 1,
}) async {
  var theme = dark
      ? PassingTraceTheme.dark(PassingTracePalette.pine)
      : PassingTraceTheme.light(PassingTracePalette.pine);
  if (const bool.fromEnvironment('PROFILE_SCREENSHOTS') &&
      const String.fromEnvironment('PROFILE_SCREENSHOT_FONT') != '') {
    theme = theme.copyWith(
      textTheme: theme.textTheme.apply(fontFamily: 'ProfilePreview'),
      primaryTextTheme: theme.primaryTextTheme.apply(
        fontFamily: 'ProfilePreview',
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: theme.filledButtonTheme.style?.merge(
          FilledButton.styleFrom(
            textStyle: theme.textTheme.labelLarge?.copyWith(
              fontFamily: 'ProfilePreview',
            ),
          ),
        ),
      ),
    );
  }
  await tester.pumpWidget(
    MaterialApp(
      debugShowCheckedModeBanner: false,
      theme: theme,
      builder: (context, child) => MediaQuery(
        data: MediaQuery.of(context)
            .copyWith(textScaler: TextScaler.linear(scale)),
        child: child!,
      ),
      home: AccountCenterView(controller: controller, onSignOut: () async {}),
    ),
  );
  await tester.pumpAndSettle();
}
