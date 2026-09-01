import 'dart:async';

import 'package:flutter/material.dart';

import 'appearance_controller.dart';
import 'passingtrace_theme.dart';

Future<void> showAppearanceSheet(BuildContext context) {
  return showModalBottomSheet<void>(
    context: context,
    useSafeArea: true,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => const AppearanceSheet(),
  );
}

class AppearanceSheet extends StatelessWidget {
  const AppearanceSheet({super.key});

  @override
  Widget build(BuildContext context) {
    final controller = AppearanceScope.of(context);
    final colors = context.traceColors;
    return SafeArea(
      top: false,
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Center(
              child: Text(
                '主题与外观',
                style: Theme.of(context).textTheme.titleLarge,
              ),
            ),
            const SizedBox(height: 24),
            Text('显示模式', style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 10),
            Row(
              children: [
                Expanded(
                  child: _ModeOption(
                    icon: Icons.devices_outlined,
                    label: '跟随系统',
                    selected: controller.mode == ThemeMode.system,
                    onTap: () =>
                        unawaited(controller.setMode(ThemeMode.system)),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: _ModeOption(
                    icon: Icons.light_mode_outlined,
                    label: '浅色',
                    selected: controller.mode == ThemeMode.light,
                    onTap: () => unawaited(controller.setMode(ThemeMode.light)),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: _ModeOption(
                    icon: Icons.dark_mode_outlined,
                    label: '深色',
                    selected: controller.mode == ThemeMode.dark,
                    onTap: () => unawaited(controller.setMode(ThemeMode.dark)),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 26),
            Text('颜色主题', style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 10),
            GridView.count(
              crossAxisCount: 2,
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              mainAxisSpacing: 10,
              crossAxisSpacing: 10,
              childAspectRatio: 1.52,
              children: [
                for (final palette in PassingTracePalette.values)
                  _PaletteOption(
                    palette: palette,
                    selected: controller.palette == palette,
                    onTap: () => unawaited(controller.setPalette(palette)),
                  ),
              ],
            ),
            const SizedBox(height: 14),
            Text(
              '主题只影响这台设备的显示，不会写入记录，也不会改变附件和 AI 内容。',
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: colors.inkTertiary),
            ),
          ],
        ),
      ),
    );
  }
}

class _ModeOption extends StatelessWidget {
  const _ModeOption({
    required this.icon,
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Semantics(
      button: true,
      selected: selected,
      label: label,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 180),
          constraints: const BoxConstraints(minHeight: 64),
          padding: const EdgeInsets.symmetric(vertical: 8),
          decoration: BoxDecoration(
            color: selected ? colors.primarySoft : colors.surfaceSoft,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: selected ? colors.primary : colors.line),
          ),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(icon, size: 20),
              const SizedBox(height: 3),
              Text(
                label,
                style: TextStyle(
                  color: selected ? colors.primaryStrong : colors.inkSecondary,
                  fontSize: 11,
                  fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _PaletteOption extends StatelessWidget {
  const _PaletteOption({
    required this.palette,
    required this.selected,
    required this.onTap,
  });

  final PassingTracePalette palette;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final preview = PassingTraceTheme.light(palette)
        .extension<PassingTraceThemeColors>()!;
    return Semantics(
      button: true,
      selected: selected,
      label: '${palette.label}，${palette.description}',
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(15),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 180),
          padding: const EdgeInsets.all(10),
          decoration: BoxDecoration(
            color: selected ? colors.surface : colors.surfaceSoft,
            borderRadius: BorderRadius.circular(15),
            border: Border.all(
              color: selected ? colors.primary : colors.line,
              width: selected ? 1.5 : 1,
            ),
          ),
          child: Stack(
            children: [
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Container(
                    height: 30,
                    padding: const EdgeInsets.symmetric(horizontal: 7),
                    decoration: BoxDecoration(
                      color: preview.surfaceTint,
                      borderRadius: BorderRadius.circular(9),
                    ),
                    child: Row(
                      children: [
                        _ColorDot(color: preview.primary, size: 18),
                        const SizedBox(width: 5),
                        _ColorDot(color: preview.accent, size: 13),
                        const SizedBox(width: 5),
                        _ColorDot(color: preview.ink, size: 9),
                      ],
                    ),
                  ),
                  const Spacer(),
                  Text(
                    palette.label,
                    style: const TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  Text(
                    palette.description,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(color: colors.inkTertiary, fontSize: 9),
                  ),
                ],
              ),
              if (selected)
                Positioned(
                  top: 4,
                  right: 4,
                  child: Container(
                    width: 22,
                    height: 22,
                    decoration: BoxDecoration(
                      color: colors.primary,
                      shape: BoxShape.circle,
                    ),
                    child: Icon(Icons.check, size: 14, color: colors.onPrimary),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _ColorDot extends StatelessWidget {
  const _ColorDot({required this.color, required this.size});

  final Color color;
  final double size;

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(color: color, shape: BoxShape.circle),
  );
}
