import 'package:flutter/material.dart';

import '../theme/appearance_controller.dart';
import '../theme/appearance_sheet.dart';
import '../theme/passingtrace_theme.dart';

class SettingsView extends StatelessWidget {
  const SettingsView({super.key, required this.onSignOut});

  final Future<void> Function() onSignOut;

  @override
  Widget build(BuildContext context) {
    final appearance = AppearanceScope.of(context);
    final colors = context.traceColors;
    return Scaffold(
      appBar: AppBar(title: const Text('设置')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(20, 24, 20, 40),
        children: [
          const _SectionLabel('外观'),
          const SizedBox(height: 8),
          Card(
            clipBehavior: Clip.antiAlias,
            child: ListTile(
              minTileHeight: 72,
              contentPadding: const EdgeInsets.symmetric(horizontal: 16),
              leading: _PalettePreview(palette: appearance.palette),
              title: const Text(
                '主题与外观',
                style: TextStyle(fontWeight: FontWeight.w700),
              ),
              subtitle: Text(
                '${appearance.palette.label} · ${_modeLabel(appearance.mode)}',
              ),
              trailing: const Icon(Icons.chevron_right, size: 20),
              onTap: () => showAppearanceSheet(context),
            ),
          ),
          const SizedBox(height: 28),
          const _SectionLabel('账号与设备'),
          const SizedBox(height: 8),
          Card(
            clipBehavior: Clip.antiAlias,
            child: ListTile(
              minTileHeight: 72,
              contentPadding: const EdgeInsets.symmetric(horizontal: 16),
              leading: Icon(Icons.logout, color: colors.danger),
              title: Text(
                '退出此设备',
                style: TextStyle(
                  color: colors.danger,
                  fontWeight: FontWeight.w700,
                ),
              ),
              subtitle: const Text('移除这台设备上的登录凭据'),
              trailing: const Icon(Icons.chevron_right, size: 20),
              onTap: () => _openSignOut(context),
            ),
          ),
          const SizedBox(height: 16),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 4),
            child: Text(
              '外观偏好仅保存在当前设备，不会影响记录、附件或 AI 内容。',
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: colors.inkTertiary),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _openSignOut(BuildContext context) async {
    final callback = onSignOut;
    Navigator.of(context).pop();
    await Future<void>.delayed(const Duration(milliseconds: 160));
    await callback();
  }

  static String _modeLabel(ThemeMode mode) => switch (mode) {
    ThemeMode.system => '跟随系统',
    ThemeMode.light => '浅色',
    ThemeMode.dark => '深色',
  };
}

class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(horizontal: 4),
    child: Text(
      text,
      style: Theme.of(context).textTheme.labelLarge
          ?.copyWith(color: context.traceColors.inkSecondary),
    ),
  );
}

class _PalettePreview extends StatelessWidget {
  const _PalettePreview({required this.palette});

  final PassingTracePalette palette;

  @override
  Widget build(BuildContext context) {
    final preview = PassingTraceTheme.light(palette)
        .extension<PassingTraceThemeColors>()!;
    return Container(
      width: 44,
      height: 44,
      padding: const EdgeInsets.all(9),
      decoration: BoxDecoration(
        color: preview.surfaceTint,
        borderRadius: BorderRadius.circular(13),
        border: Border.all(color: preview.line),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          _Dot(color: preview.primary, size: 12),
          const SizedBox(width: 3),
          _Dot(color: preview.accent, size: 8),
        ],
      ),
    );
  }
}

class _Dot extends StatelessWidget {
  const _Dot({required this.color, required this.size});

  final Color color;
  final double size;

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(color: color, shape: BoxShape.circle),
  );
}
