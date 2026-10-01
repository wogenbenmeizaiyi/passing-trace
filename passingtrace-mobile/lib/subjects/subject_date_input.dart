import 'package:flutter/material.dart';

import '../events/event_datetime.dart';

class SubjectDateInput extends StatelessWidget {
  const SubjectDateInput({
    super.key,
    required this.controller,
    required this.label,
    this.requiredDate = false,
    this.enabled = true,
  });
  final TextEditingController controller;
  final String label;
  final bool requiredDate;
  final bool enabled;
  @override
  Widget build(BuildContext context) => TextFormField(
    controller: controller,
    enabled: enabled,
    decoration: InputDecoration(
      labelText: label,
      hintText: 'YYYY-MM-DD HH:mm',
      suffixIcon: IconButton(
        tooltip: '选择日期时间',
        icon: const Icon(Icons.calendar_month),
        onPressed: !enabled
            ? null
            : () async {
                final parsed = DateTime.tryParse(
                      controller.text.replaceFirst(' ', 'T'),
                    ),
                    now = DateTime.now();
                final date = await showDatePicker(
                  context: context,
                  initialDate: parsed ?? now,
                  firstDate: DateTime(1700),
                  lastDate: DateTime(2200),
                );
                if (date == null || !context.mounted) return;
                final time = await showTimePicker(
                  context: context,
                  initialTime: parsed == null
                      ? TimeOfDay.now()
                      : TimeOfDay.fromDateTime(parsed),
                );
                if (time == null) return;
                controller.text = toWallClockLocal(
                  DateTime(
                    date.year,
                    date.month,
                    date.day,
                    time.hour,
                    time.minute,
                  ),
                );
              },
      ),
    ),
    keyboardType: TextInputType.datetime,
    validator: (v) {
      if (v == null || v.isEmpty) return requiredDate ? '请选择实际日期' : null;
      return DateTime.tryParse(v.replaceFirst(' ', 'T')) == null
          ? '日期格式应为 YYYY-MM-DD HH:mm'
          : null;
    },
  );
}
