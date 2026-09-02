import 'package:flutter/material.dart';

import 'passingtrace_theme.dart';
import 'quiet_trace_icons.dart';

class TraceAppBar extends StatelessWidget implements PreferredSizeWidget {
  const TraceAppBar({
    super.key,
    required this.title,
    this.leading,
    this.trailing,
    this.trailingWidth = 48,
  });

  final String title;
  final Widget? leading;
  final Widget? trailing;
  final double trailingWidth;

  @override
  Size get preferredSize => const Size.fromHeight(64);

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Material(
      color: colors.surface,
      child: SafeArea(
        bottom: false,
        child: SizedBox(
          height: 64,
          child: DecoratedBox(
            decoration: BoxDecoration(
              border: Border(bottom: BorderSide(color: colors.line)),
            ),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 12),
              child: Row(
                children: [
                  SizedBox(
                    width: trailingWidth,
                    height: 48,
                    child: Align(
                      alignment: Alignment.centerLeft,
                      child: leading,
                    ),
                  ),
                  Expanded(
                    child: Text(
                      title,
                      textAlign: TextAlign.center,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: colors.ink,
                        fontSize: 18,
                        height: 1.2,
                        fontWeight: FontWeight.w700,
                        letterSpacing: -0.2,
                      ),
                    ),
                  ),
                  SizedBox(
                    width: trailingWidth,
                    height: 48,
                    child: Align(
                      alignment: Alignment.centerRight,
                      child: trailing,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class TraceIconButton extends StatelessWidget {
  const TraceIconButton({
    super.key,
    required this.glyph,
    required this.tooltip,
    required this.onPressed,
    this.color,
    this.backgroundColor,
    this.borderColor,
    this.expanded,
  });

  final TraceGlyph glyph;
  final String tooltip;
  final VoidCallback? onPressed;
  final Color? color;
  final Color? backgroundColor;
  final Color? borderColor;
  final bool? expanded;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Tooltip(
      message: tooltip,
      child: Material(
        color: backgroundColor ?? Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(12),
          side: borderColor == null
              ? BorderSide.none
              : BorderSide(color: borderColor!),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onPressed,
          child: Semantics(
            button: true,
            label: tooltip,
            enabled: onPressed != null,
            expanded: expanded,
            child: Center(
              child: Opacity(
                opacity: onPressed == null ? 0.42 : 1,
                child: TraceIcon(glyph, color: color ?? colors.ink, size: 22),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class TraceBottomNavigation extends StatelessWidget {
  const TraceBottomNavigation({
    super.key,
    required this.selectedIndex,
    required this.onSelected,
  });

  final int selectedIndex;
  final ValueChanged<int> onSelected;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Material(
      color: colors.surface.withValues(alpha: 0.98),
      child: SafeArea(
        top: false,
        child: DecoratedBox(
          decoration: BoxDecoration(
            border: Border(top: BorderSide(color: colors.line)),
          ),
          child: SizedBox(
            height: 72,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(18, 6, 18, 8),
              child: Row(
                children: [
                  Expanded(
                    child: _TraceNavigationItem(
                      glyph: TraceGlyph.journal,
                      label: '记录',
                      selected: selectedIndex == 0,
                      onTap: () => onSelected(0),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: _TraceNavigationItem(
                      glyph: TraceGlyph.storyline,
                      label: '故事线',
                      selected: selectedIndex == 1,
                      onTap: () => onSelected(1),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: _TraceNavigationItem(
                      glyph: TraceGlyph.sparkle,
                      label: '问 AI',
                      selected: selectedIndex == 2,
                      onTap: () => onSelected(2),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _TraceNavigationItem extends StatelessWidget {
  const _TraceNavigationItem({
    required this.glyph,
    required this.label,
    required this.selected,
    required this.onTap,
  });

  final TraceGlyph glyph;
  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final color = selected ? colors.primaryStrong : colors.inkTertiary;
    return Semantics(
      button: true,
      selected: selected,
      label: label,
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(12),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              AnimatedContainer(
                duration: const Duration(milliseconds: 140),
                width: 22,
                height: 2,
                decoration: BoxDecoration(
                  color: selected ? colors.primary : Colors.transparent,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
              const SizedBox(height: 3),
              TraceIcon(glyph, size: 22, color: color),
              const SizedBox(height: 1),
              Text(
                label,
                style: TextStyle(
                  color: color,
                  fontSize: 11,
                  height: 1.2,
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

class TraceDrawerItem extends StatelessWidget {
  const TraceDrawerItem({
    super.key,
    required this.glyph,
    required this.label,
    required this.onTap,
    this.selected = false,
    this.trailingText,
    this.danger = false,
  });

  final TraceGlyph glyph;
  final String label;
  final VoidCallback? onTap;
  final bool selected;
  final String? trailingText;
  final bool danger;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    final color = danger
        ? colors.danger
        : selected
        ? colors.primaryStrong
        : colors.inkSecondary;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 2, vertical: 2),
      child: Material(
        color: selected ? colors.primarySoft : Colors.transparent,
        borderRadius: BorderRadius.circular(12),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Semantics(
            button: true,
            selected: selected,
            child: SizedBox(
              height: 50,
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 12),
                child: Row(
                  children: [
                    TraceIcon(glyph, color: color, size: 22),
                    const SizedBox(width: 18),
                    Expanded(
                      child: Text(
                        label,
                        style: TextStyle(
                          color: color,
                          fontSize: 14,
                          fontWeight: selected
                              ? FontWeight.w700
                              : FontWeight.w500,
                        ),
                      ),
                    ),
                    if (trailingText != null)
                      Text(
                        trailingText!,
                        style: TextStyle(
                          color: colors.inkTertiary,
                          fontSize: 10,
                        ),
                      )
                    else if (!danger)
                      TraceIcon(
                        TraceGlyph.chevronRight,
                        size: 18,
                        color: colors.inkTertiary,
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class TraceFieldLabel extends StatelessWidget {
  const TraceFieldLabel(this.text, {super.key});

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 8),
    child: Text(
      text,
      style: TextStyle(
        color: context.traceColors.inkSecondary,
        fontSize: 12,
        fontWeight: FontWeight.w700,
      ),
    ),
  );
}

class TraceRowButton extends StatelessWidget {
  const TraceRowButton({
    super.key,
    required this.glyph,
    required this.title,
    required this.subtitle,
    required this.onTap,
    this.trailing = TraceGlyph.chevronRight,
  });

  final TraceGlyph glyph;
  final String title;
  final String subtitle;
  final VoidCallback? onTap;
  final TraceGlyph? trailing;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Material(
      color: colors.surface,
      shape: RoundedRectangleBorder(
        side: BorderSide(color: colors.line),
        borderRadius: BorderRadius.circular(12),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 64),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
            child: Row(
              children: [
                Container(
                  width: 36,
                  height: 36,
                  decoration: BoxDecoration(
                    color: colors.primarySoft,
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Center(
                    child: TraceIcon(
                      glyph,
                      size: 18,
                      color: colors.primaryStrong,
                    ),
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          color: colors.ink,
                          fontSize: 13,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        subtitle,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          color: colors.inkTertiary,
                          fontSize: 11,
                          height: 1.35,
                        ),
                      ),
                    ],
                  ),
                ),
                if (trailing != null)
                  TraceIcon(trailing!, size: 18, color: colors.inkTertiary),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class TraceTag extends StatelessWidget {
  const TraceTag({
    super.key,
    required this.label,
    this.category = false,
    this.ai = false,
  });

  final String label;
  final bool category;
  final bool ai;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Container(
      constraints: const BoxConstraints(minHeight: 26),
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
      decoration: BoxDecoration(
        color: category ? colors.primarySoft : colors.surfaceSoft,
        border: Border.all(color: category ? Colors.transparent : colors.line),
        borderRadius: BorderRadius.circular(99),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (ai) ...[
            TraceIcon(
              TraceGlyph.sparkle,
              size: 12,
              strokeWidth: 2,
              color: colors.accent,
            ),
            const SizedBox(width: 4),
          ],
          Text(
            label,
            style: TextStyle(
              color: ai
                  ? colors.accent
                  : category
                  ? colors.primaryStrong
                  : colors.inkSecondary,
              fontSize: 11,
              height: 1.2,
              fontWeight: category ? FontWeight.w700 : FontWeight.w500,
            ),
          ),
        ],
      ),
    );
  }
}

class TracePrimaryActionBar extends StatelessWidget {
  const TracePrimaryActionBar({
    super.key,
    required this.label,
    required this.onPressed,
    this.loading = false,
  });

  final String label;
  final VoidCallback? onPressed;
  final bool loading;

  @override
  Widget build(BuildContext context) {
    final colors = context.traceColors;
    return Material(
      color: colors.surface,
      child: SafeArea(
        top: false,
        child: Container(
          padding: const EdgeInsets.fromLTRB(18, 12, 18, 16),
          decoration: BoxDecoration(
            border: Border(top: BorderSide(color: colors.line)),
          ),
          child: FilledButton(
            onPressed: onPressed,
            child: loading
                ? SizedBox.square(
                    dimension: 18,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: colors.onPrimary,
                    ),
                  )
                : Text(label),
          ),
        ),
      ),
    );
  }
}
