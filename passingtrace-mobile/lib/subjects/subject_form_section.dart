import 'package:flutter/material.dart';

import '../theme/passingtrace_theme.dart';

class SubjectFormSection extends StatelessWidget {
  const SubjectFormSection({
    super.key,
    required this.title,
    required this.children,
    this.description,
  });

  final String title;
  final String? description;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Material(
    color: context.traceColors.surface,
    clipBehavior: Clip.antiAlias,
    shape: RoundedRectangleBorder(
      side: BorderSide(color: context.traceColors.line),
      borderRadius: BorderRadius.circular(20),
    ),
    child: Padding(
      padding: const EdgeInsets.all(18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Semantics(
            header: true,
            child: Text(
              title,
              style: Theme.of(context).textTheme.titleMedium
                  ?.copyWith(fontWeight: FontWeight.w700),
            ),
          ),
          if (description != null) ...[
            const SizedBox(height: 10),
            Text(
              description!,
              style: TextStyle(
                color: context.traceColors.inkSecondary,
                height: 1.5,
              ),
            ),
          ],
          const SizedBox(height: 24),
          for (var i = 0; i < children.length; i++) ...[
            if (i > 0) const SizedBox(height: 20),
            children[i],
          ],
        ],
      ),
    ),
  );
}
