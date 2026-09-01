import 'package:flutter/material.dart';

enum PassingTracePalette { pine, tide, plum, dune }

extension PassingTracePaletteInfo on PassingTracePalette {
  String get storageValue => name;

  String get label => switch (this) {
    PassingTracePalette.pine => '松间',
    PassingTracePalette.tide => '潮汐',
    PassingTracePalette.plum => '暮紫',
    PassingTracePalette.dune => '沙丘',
  };

  String get description => switch (this) {
    PassingTracePalette.pine => '常青绿 · 温和自然',
    PassingTracePalette.tide => '雾蓝色 · 清醒安静',
    PassingTracePalette.plum => '灰紫色 · 柔和内敛',
    PassingTracePalette.dune => '沙金色 · 温暖沉静',
  };

  static PassingTracePalette? fromStorageValue(String? value) {
    for (final palette in PassingTracePalette.values) {
      if (palette.storageValue == value) return palette;
    }
    return null;
  }
}

@immutable
class PassingTraceThemeColors extends ThemeExtension<PassingTraceThemeColors> {
  const PassingTraceThemeColors({
    required this.canvas,
    required this.surface,
    required this.surfaceRaised,
    required this.surfaceSoft,
    required this.surfaceTint,
    required this.ink,
    required this.inkSecondary,
    required this.inkTertiary,
    required this.line,
    required this.lineStrong,
    required this.primary,
    required this.primaryStrong,
    required this.onPrimary,
    required this.primarySoft,
    required this.accent,
    required this.accentSoft,
    required this.warning,
    required this.danger,
    required this.success,
  });

  final Color canvas;
  final Color surface;
  final Color surfaceRaised;
  final Color surfaceSoft;
  final Color surfaceTint;
  final Color ink;
  final Color inkSecondary;
  final Color inkTertiary;
  Color get inkMuted => inkTertiary;
  final Color line;
  final Color lineStrong;
  final Color primary;
  final Color primaryStrong;
  final Color onPrimary;
  final Color primarySoft;
  final Color accent;
  final Color accentSoft;
  final Color warning;
  final Color danger;
  final Color success;

  @override
  PassingTraceThemeColors copyWith({
    Color? canvas,
    Color? surface,
    Color? surfaceRaised,
    Color? surfaceSoft,
    Color? surfaceTint,
    Color? ink,
    Color? inkSecondary,
    Color? inkTertiary,
    Color? line,
    Color? lineStrong,
    Color? primary,
    Color? primaryStrong,
    Color? onPrimary,
    Color? primarySoft,
    Color? accent,
    Color? accentSoft,
    Color? warning,
    Color? danger,
    Color? success,
  }) => PassingTraceThemeColors(
    canvas: canvas ?? this.canvas,
    surface: surface ?? this.surface,
    surfaceRaised: surfaceRaised ?? this.surfaceRaised,
    surfaceSoft: surfaceSoft ?? this.surfaceSoft,
    surfaceTint: surfaceTint ?? this.surfaceTint,
    ink: ink ?? this.ink,
    inkSecondary: inkSecondary ?? this.inkSecondary,
    inkTertiary: inkTertiary ?? this.inkTertiary,
    line: line ?? this.line,
    lineStrong: lineStrong ?? this.lineStrong,
    primary: primary ?? this.primary,
    primaryStrong: primaryStrong ?? this.primaryStrong,
    onPrimary: onPrimary ?? this.onPrimary,
    primarySoft: primarySoft ?? this.primarySoft,
    accent: accent ?? this.accent,
    accentSoft: accentSoft ?? this.accentSoft,
    warning: warning ?? this.warning,
    danger: danger ?? this.danger,
    success: success ?? this.success,
  );

  @override
  PassingTraceThemeColors lerp(
    covariant PassingTraceThemeColors? other,
    double t,
  ) {
    if (other == null) return this;
    return PassingTraceThemeColors(
      canvas: Color.lerp(canvas, other.canvas, t)!,
      surface: Color.lerp(surface, other.surface, t)!,
      surfaceRaised: Color.lerp(surfaceRaised, other.surfaceRaised, t)!,
      surfaceSoft: Color.lerp(surfaceSoft, other.surfaceSoft, t)!,
      surfaceTint: Color.lerp(surfaceTint, other.surfaceTint, t)!,
      ink: Color.lerp(ink, other.ink, t)!,
      inkSecondary: Color.lerp(inkSecondary, other.inkSecondary, t)!,
      inkTertiary: Color.lerp(inkTertiary, other.inkTertiary, t)!,
      line: Color.lerp(line, other.line, t)!,
      lineStrong: Color.lerp(lineStrong, other.lineStrong, t)!,
      primary: Color.lerp(primary, other.primary, t)!,
      primaryStrong: Color.lerp(primaryStrong, other.primaryStrong, t)!,
      onPrimary: Color.lerp(onPrimary, other.onPrimary, t)!,
      primarySoft: Color.lerp(primarySoft, other.primarySoft, t)!,
      accent: Color.lerp(accent, other.accent, t)!,
      accentSoft: Color.lerp(accentSoft, other.accentSoft, t)!,
      warning: Color.lerp(warning, other.warning, t)!,
      danger: Color.lerp(danger, other.danger, t)!,
      success: Color.lerp(success, other.success, t)!,
    );
  }
}

extension PassingTraceThemeContext on BuildContext {
  PassingTraceThemeColors get traceColors =>
      Theme.of(this).extension<PassingTraceThemeColors>() ??
      PassingTraceTheme.light(PassingTracePalette.pine)
          .extension<PassingTraceThemeColors>()!;
}

class PassingTraceTheme {
  const PassingTraceTheme._();

  static ThemeData light(PassingTracePalette palette) =>
      _build(_tokens(palette, Brightness.light));

  static ThemeData dark(PassingTracePalette palette) =>
      _build(_tokens(palette, Brightness.dark));

  static ThemeData _build(_ThemeTokens tokens) {
    final colors = tokens.colors;
    final brightness = tokens.brightness;
    final base = ThemeData(
      useMaterial3: true,
      brightness: brightness,
      fontFamily: 'sans-serif',
    );
    final scheme =
        ColorScheme.fromSeed(
          seedColor: colors.primary,
          brightness: brightness,
          surface: colors.surface,
        ).copyWith(
          primary: colors.primary,
          onPrimary: colors.onPrimary,
          secondary: colors.accent,
          onSecondary: brightness == Brightness.light
              ? Colors.white
              : const Color(0xff241912),
          error: colors.danger,
          onError: brightness == Brightness.light
              ? Colors.white
              : const Color(0xff2b1513),
          surface: colors.surface,
          onSurface: colors.ink,
          outline: colors.lineStrong,
          outlineVariant: colors.line,
        );
    final textTheme = base.textTheme
        .apply(
          bodyColor: colors.ink,
          displayColor: colors.ink,
          fontFamily: 'sans-serif',
        )
        .copyWith(
          headlineSmall: TextStyle(
            color: colors.ink,
            fontSize: 26,
            height: 1.25,
            fontWeight: FontWeight.w700,
            letterSpacing: -0.7,
          ),
          titleLarge: TextStyle(
            color: colors.ink,
            fontSize: 18,
            height: 1.3,
            fontWeight: FontWeight.w700,
            letterSpacing: -0.2,
          ),
          titleMedium: TextStyle(
            color: colors.ink,
            fontSize: 16,
            height: 1.4,
            fontWeight: FontWeight.w700,
          ),
          bodyLarge: TextStyle(color: colors.ink, fontSize: 16, height: 1.65),
          bodyMedium: TextStyle(color: colors.ink, fontSize: 14, height: 1.6),
          bodySmall: TextStyle(
            color: colors.inkSecondary,
            fontSize: 12,
            height: 1.5,
          ),
          labelLarge: TextStyle(
            color: colors.ink,
            fontSize: 14,
            fontWeight: FontWeight.w700,
          ),
        );
    const controlRadius = BorderRadius.all(Radius.circular(12));
    final inputBorder = OutlineInputBorder(
      borderRadius: controlRadius,
      borderSide: BorderSide(color: colors.lineStrong),
    );

    return base.copyWith(
      colorScheme: scheme,
      scaffoldBackgroundColor: colors.canvas,
      canvasColor: colors.surface,
      dividerColor: colors.line,
      textTheme: textTheme,
      appBarTheme: AppBarTheme(
        centerTitle: true,
        toolbarHeight: 64,
        backgroundColor: colors.surface,
        foregroundColor: colors.ink,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        shape: Border(bottom: BorderSide(color: colors.line)),
        titleTextStyle: textTheme.titleLarge,
      ),
      drawerTheme: DrawerThemeData(
        backgroundColor: colors.surface,
        surfaceTintColor: Colors.transparent,
        shape: const RoundedRectangleBorder(),
      ),
      cardTheme: CardThemeData(
        color: colors.surfaceRaised,
        surfaceTintColor: Colors.transparent,
        margin: EdgeInsets.zero,
        elevation: 0,
        shape: RoundedRectangleBorder(
          side: BorderSide(color: colors.line),
          borderRadius: const BorderRadius.all(Radius.circular(18)),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: colors.surfaceSoft,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 14,
          vertical: 15,
        ),
        labelStyle: TextStyle(color: colors.inkSecondary),
        hintStyle: TextStyle(color: colors.inkTertiary),
        helperStyle: TextStyle(color: colors.inkSecondary),
        enabledBorder: inputBorder,
        border: inputBorder,
        focusedBorder: inputBorder.copyWith(
          borderSide: BorderSide(color: colors.primary, width: 1.5),
        ),
        errorBorder: inputBorder.copyWith(
          borderSide: BorderSide(color: colors.danger),
        ),
        focusedErrorBorder: inputBorder.copyWith(
          borderSide: BorderSide(color: colors.danger, width: 1.5),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size(48, 50),
          backgroundColor: colors.primary,
          foregroundColor: colors.onPrimary,
          disabledBackgroundColor: colors.line,
          disabledForegroundColor: colors.inkTertiary,
          shape: const RoundedRectangleBorder(borderRadius: controlRadius),
          textStyle: const TextStyle(fontWeight: FontWeight.w700),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(48, 50),
          foregroundColor: colors.ink,
          side: BorderSide(color: colors.lineStrong),
          shape: const RoundedRectangleBorder(borderRadius: controlRadius),
          textStyle: const TextStyle(fontWeight: FontWeight.w700),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          minimumSize: const Size(48, 48),
          foregroundColor: colors.primaryStrong,
          shape: const RoundedRectangleBorder(borderRadius: controlRadius),
        ),
      ),
      iconButtonTheme: const IconButtonThemeData(
        style: ButtonStyle(
          minimumSize: WidgetStatePropertyAll(Size(48, 48)),
          shape: WidgetStatePropertyAll(
            RoundedRectangleBorder(borderRadius: controlRadius),
          ),
        ),
      ),
      floatingActionButtonTheme: FloatingActionButtonThemeData(
        backgroundColor: colors.primary,
        foregroundColor: colors.onPrimary,
        elevation: 2,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.all(Radius.circular(16)),
        ),
      ),
      navigationBarTheme: NavigationBarThemeData(
        height: 72,
        backgroundColor: colors.surface,
        indicatorColor: colors.primarySoft,
        elevation: 0,
        labelTextStyle: WidgetStateProperty.resolveWith((states) {
          final selected = states.contains(WidgetState.selected);
          return TextStyle(
            color: selected ? colors.primaryStrong : colors.inkTertiary,
            fontSize: 11,
            fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
          );
        }),
        iconTheme: WidgetStateProperty.resolveWith((states) {
          final selected = states.contains(WidgetState.selected);
          return IconThemeData(
            color: selected ? colors.primaryStrong : colors.inkTertiary,
            size: 23,
          );
        }),
      ),
      chipTheme: base.chipTheme.copyWith(
        backgroundColor: colors.surfaceSoft,
        selectedColor: colors.primarySoft,
        side: BorderSide(color: colors.line),
        labelStyle: TextStyle(color: colors.inkSecondary, fontSize: 12),
        secondaryLabelStyle: TextStyle(
          color: colors.primaryStrong,
          fontSize: 12,
          fontWeight: FontWeight.w700,
        ),
        shape: const StadiumBorder(),
      ),
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: colors.surface,
        modalBackgroundColor: colors.surface,
        surfaceTintColor: Colors.transparent,
        showDragHandle: true,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
        ),
      ),
      snackBarTheme: SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        backgroundColor: colors.ink,
        contentTextStyle: TextStyle(color: colors.surface),
        shape: const RoundedRectangleBorder(borderRadius: controlRadius),
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(
        color: colors.primary,
        linearTrackColor: colors.primarySoft,
      ),
      extensions: [colors],
    );
  }

  static _ThemeTokens _tokens(
    PassingTracePalette palette,
    Brightness brightness,
  ) => switch ((palette, brightness)) {
    (PassingTracePalette.pine, Brightness.light) => _ThemeTokens.light(
      canvas: Color(0xffe9ede8),
      surface: Color(0xffffffff),
      surfaceSoft: Color(0xfff4f7f3),
      surfaceTint: Color(0xffe5f0eb),
      ink: Color(0xff1c2520),
      inkSecondary: Color(0xff526159),
      inkTertiary: Color(0xff68756d),
      line: Color(0xffd9e1db),
      lineStrong: Color(0xffc4cec7),
      primary: Color(0xff2f6b57),
      primaryStrong: Color(0xff245443),
      primarySoft: Color(0xffdcebe4),
      accent: Color(0xffc96c47),
      accentSoft: Color(0xfff6e3da),
    ),
    (PassingTracePalette.pine, Brightness.dark) => _ThemeTokens.dark(
      canvas: Color(0xff101713),
      surface: Color(0xff18211c),
      surfaceRaised: Color(0xff202b25),
      surfaceSoft: Color(0xff1d2822),
      surfaceTint: Color(0xff243a31),
      ink: Color(0xfff1f5f2),
      inkSecondary: Color(0xffbcc8c0),
      inkTertiary: Color(0xff92a097),
      line: Color(0xff334139),
      lineStrong: Color(0xff46564c),
      primary: Color(0xff75b89f),
      primaryStrong: Color(0xff8dcbb4),
      primarySoft: Color(0xff29483b),
      accent: Color(0xffe29a79),
      accentSoft: Color(0xff4c3027),
    ),
    (PassingTracePalette.tide, Brightness.light) => _ThemeTokens.light(
      canvas: Color(0xffe8eef0),
      surface: Color(0xffffffff),
      surfaceSoft: Color(0xfff2f6f7),
      surfaceTint: Color(0xffe0edf1),
      ink: Color(0xff17252a),
      inkSecondary: Color(0xff4c6067),
      inkTertiary: Color(0xff64777e),
      line: Color(0xffd6e0e3),
      lineStrong: Color(0xffbbcbd0),
      primary: Color(0xff2b6678),
      primaryStrong: Color(0xff215364),
      primarySoft: Color(0xffd8eaf0),
      accent: Color(0xffc76849),
      accentSoft: Color(0xfff5e1d9),
    ),
    (PassingTracePalette.tide, Brightness.dark) => _ThemeTokens.dark(
      canvas: Color(0xff0d171b),
      surface: Color(0xff142127),
      surfaceRaised: Color(0xff1c2d34),
      surfaceSoft: Color(0xff17262c),
      surfaceTint: Color(0xff203b45),
      ink: Color(0xffedf4f6),
      inkSecondary: Color(0xffb5c7cd),
      inkTertiary: Color(0xff8da3aa),
      line: Color(0xff30444b),
      lineStrong: Color(0xff435b63),
      primary: Color(0xff76b3c6),
      primaryStrong: Color(0xff92c7d7),
      primarySoft: Color(0xff254553),
      accent: Color(0xffe7a184),
      accentSoft: Color(0xff4c3127),
    ),
    (PassingTracePalette.plum, Brightness.light) => _ThemeTokens.light(
      canvas: Color(0xffeeeaf0),
      surface: Color(0xffffffff),
      surfaceSoft: Color(0xfff7f4f8),
      surfaceTint: Color(0xffeee5f1),
      ink: Color(0xff291f2c),
      inkSecondary: Color(0xff625568),
      inkTertiary: Color(0xff77697c),
      line: Color(0xffe2d9e5),
      lineStrong: Color(0xffcfc2d3),
      primary: Color(0xff725a7d),
      primaryStrong: Color(0xff5c4667),
      primarySoft: Color(0xffe9ddec),
      accent: Color(0xffb95863),
      accentSoft: Color(0xfff4dfe2),
    ),
    (PassingTracePalette.plum, Brightness.dark) => _ThemeTokens.dark(
      canvas: Color(0xff18131a),
      surface: Color(0xff211a24),
      surfaceRaised: Color(0xff2c2330),
      surfaceSoft: Color(0xff271f2a),
      surfaceTint: Color(0xff3b2d40),
      ink: Color(0xfff5f0f6),
      inkSecondary: Color(0xffcbbfce),
      inkTertiary: Color(0xffa596aa),
      line: Color(0xff433749),
      lineStrong: Color(0xff5b4b62),
      primary: Color(0xffc1a1cb),
      primaryStrong: Color(0xffd2b8da),
      primarySoft: Color(0xff493751),
      accent: Color(0xffe49aa1),
      accentSoft: Color(0xff503036),
    ),
    (PassingTracePalette.dune, Brightness.light) => _ThemeTokens.light(
      canvas: Color(0xffeeece5),
      surface: Color(0xfffffdf9),
      surfaceSoft: Color(0xfff7f4ec),
      surfaceTint: Color(0xfff2ebd6),
      ink: Color(0xff28241a),
      inkSecondary: Color(0xff665f4d),
      inkTertiary: Color(0xff746a54),
      line: Color(0xffe3ddcd),
      lineStrong: Color(0xffcec5ae),
      primary: Color(0xff77622a),
      primaryStrong: Color(0xff5f4e20),
      primarySoft: Color(0xffece4c7),
      accent: Color(0xffb85f38),
      accentSoft: Color(0xfff4e1d5),
    ),
    (PassingTracePalette.dune, Brightness.dark) => _ThemeTokens.dark(
      canvas: Color(0xff181710),
      surface: Color(0xff222017),
      surfaceRaised: Color(0xff2d2a1e),
      surfaceSoft: Color(0xff28251b),
      surfaceTint: Color(0xff403a25),
      ink: Color(0xfff5f2e9),
      inkSecondary: Color(0xffcbc5b3),
      inkTertiary: Color(0xffa59d85),
      line: Color(0xff443f30),
      lineStrong: Color(0xff5c5540),
      primary: Color(0xffc9b56b),
      primaryStrong: Color(0xffdac983),
      primarySoft: Color(0xff4a4228),
      accent: Color(0xffe7a178),
      accentSoft: Color(0xff503328),
    ),
  };
}

class _ThemeTokens {
  _ThemeTokens.light({
    required Color canvas,
    required Color surface,
    Color? surfaceRaised,
    required Color surfaceSoft,
    required Color surfaceTint,
    required Color ink,
    required Color inkSecondary,
    required Color inkTertiary,
    required Color line,
    required Color lineStrong,
    required Color primary,
    required Color primaryStrong,
    required Color primarySoft,
    required Color accent,
    required Color accentSoft,
  }) : this._(
         brightness: Brightness.light,
         canvas: canvas,
         surface: surface,
         surfaceRaised: surfaceRaised ?? surface,
         surfaceSoft: surfaceSoft,
         surfaceTint: surfaceTint,
         ink: ink,
         inkSecondary: inkSecondary,
         inkTertiary: inkTertiary,
         line: line,
         lineStrong: lineStrong,
         primary: primary,
         primaryStrong: primaryStrong,
         onPrimary: Colors.white,
         primarySoft: primarySoft,
         accent: accent,
         accentSoft: accentSoft,
         warning: const Color(0xffa56712),
         danger: const Color(0xffb4443d),
         success: const Color(0xff317251),
       );

  _ThemeTokens.dark({
    required Color canvas,
    required Color surface,
    required Color surfaceRaised,
    required Color surfaceSoft,
    required Color surfaceTint,
    required Color ink,
    required Color inkSecondary,
    required Color inkTertiary,
    required Color line,
    required Color lineStrong,
    required Color primary,
    required Color primaryStrong,
    required Color primarySoft,
    required Color accent,
    required Color accentSoft,
  }) : this._(
         brightness: Brightness.dark,
         canvas: canvas,
         surface: surface,
         surfaceRaised: surfaceRaised,
         surfaceSoft: surfaceSoft,
         surfaceTint: surfaceTint,
         ink: ink,
         inkSecondary: inkSecondary,
         inkTertiary: inkTertiary,
         line: line,
         lineStrong: lineStrong,
         primary: primary,
         primaryStrong: primaryStrong,
         onPrimary: const Color(0xff151d18),
         primarySoft: primarySoft,
         accent: accent,
         accentSoft: accentSoft,
         warning: const Color(0xffe5b05d),
         danger: const Color(0xfff18a82),
         success: const Color(0xff83c9a3),
       );

  _ThemeTokens._({
    required this.brightness,
    required Color canvas,
    required Color surface,
    required Color surfaceRaised,
    required Color surfaceSoft,
    required Color surfaceTint,
    required Color ink,
    required Color inkSecondary,
    required Color inkTertiary,
    required Color line,
    required Color lineStrong,
    required Color primary,
    required Color primaryStrong,
    required Color onPrimary,
    required Color primarySoft,
    required Color accent,
    required Color accentSoft,
    required Color warning,
    required Color danger,
    required Color success,
  }) : colors = PassingTraceThemeColors(
         canvas: canvas,
         surface: surface,
         surfaceRaised: surfaceRaised,
         surfaceSoft: surfaceSoft,
         surfaceTint: surfaceTint,
         ink: ink,
         inkSecondary: inkSecondary,
         inkTertiary: inkTertiary,
         line: line,
         lineStrong: lineStrong,
         primary: primary,
         primaryStrong: primaryStrong,
         onPrimary: onPrimary,
         primarySoft: primarySoft,
         accent: accent,
         accentSoft: accentSoft,
         warning: warning,
         danger: danger,
         success: success,
       );

  final Brightness brightness;
  final PassingTraceThemeColors colors;
}
