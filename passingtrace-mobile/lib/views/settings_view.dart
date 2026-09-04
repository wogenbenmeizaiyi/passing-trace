import 'package:flutter/material.dart';

import '../theme/appearance_controller.dart';
import '../theme/appearance_sheet.dart';
import '../theme/passingtrace_theme.dart';
import '../theme/quiet_trace_components.dart';
import '../theme/quiet_trace_icons.dart';

class SettingsView extends StatelessWidget {
  const SettingsView({super.key, required this.onSignOut});

  final Future<void> Function() onSignOut;

  @override
  Widget build(BuildContext context) {
    final appearance = AppearanceScope.of(context);
    final colors = context.traceColors;
    return Scaffold(
      appBar: TraceAppBar(
        title: '设置',
        leading: TraceIconButton(
          glyph: TraceGlyph.chevronLeft,
          tooltip: '返回',
          onPressed: () => Navigator.of(context).pop(),
        ),
      ),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(20, 24, 20, 40),
        children: [
          const _SectionLabel('外观'),
          const SizedBox(height: 8),
          _SettingsRow(
            leading: _PalettePreview(palette: appearance.palette),
            title: '主题与外观',
            subtitle:
                '${appearance.palette.label} · ${_modeLabel(appearance.mode)}',
            onTap: () => showAppearanceSheet(context),
          ),
          const SizedBox(height: 28),
          const _SectionLabel('账号与设备'),
          const SizedBox(height: 8),
          _SettingsRow(
            leading: Container(
              width: 44,
              height: 44,
              decoration: BoxDecoration(
                color: colors.danger.withValues(alpha: 0.10),
                borderRadius: BorderRadius.circular(13),
              ),
              child: Center(
                child: TraceIcon(
                  TraceGlyph.logout,
                  size: 21,
                  color: colors.danger,
                ),
              ),
            ),
            title: '退出此设备',
            subtitle: '移除这台设备上的登录凭据',
            danger: true,
            onTap: () => _openSignOut(context),
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

class _SettingsRow extends StatelessWidget {
  const _SettingsRow({
    required this.leading,
    required this.title,
    required this.subtitle,
    required this.onTap,
    this.danger = false,
  });

  final Widget leading;
  final String title;
  final String subtitle;
  final VoidCallback onTap;
  final bool danger;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final titleColor = danger ? colors.danger : colors.ink;
    return Material(
      color: colors.surface,
      shape: RoundedRectangleBorder(
        side: BorderSide(color: colors.line),
        borderRadius: BorderRadius.circular(14),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 72),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
            child: Row(
              children: [
                leading,
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        style: TextStyle(
                          color: titleColor,
                          fontSize: 14,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 3),
                      Text(
                        subtitle,
                        style: TextStyle(
                          color: colors.inkTertiary,
                          fontSize: 11,
                        ),
                      ),
                    ],
                  ),
                ),
                TraceIcon(
                  TraceGlyph.chevronRight,
                  size: 18,
                  color: danger ? colors.danger : colors.inkMuted,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
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
