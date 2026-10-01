import 'package:flutter/material.dart';

import '../storylines/storyline_id.dart';
import 'subject_model.dart';

class SubjectFieldsEditor extends StatefulWidget {
  const SubjectFieldsEditor({
    super.key,
    required this.fields,
    required this.values,
    required this.onChanged,
    this.onFieldsChanged,
    this.planned = false,
    this.showHeading = true,
  });
  final List<SubjectField> fields;
  final Map<String, dynamic> values;
  final ValueChanged<Map<String, dynamic>> onChanged;
  final ValueChanged<List<SubjectField>>? onFieldsChanged;
  final bool planned, showHeading;
  @override
  State<SubjectFieldsEditor> createState() => _SubjectFieldsEditorState();
}

class _SubjectFieldsEditorState extends State<SubjectFieldsEditor> {
  List<SubjectField> get fields => widget.fields;
  Map<String, dynamic> get values => widget.values;
  ValueChanged<Map<String, dynamic>> get onChanged => widget.onChanged;
  ValueChanged<List<SubjectField>>? get onFieldsChanged =>
      widget.onFieldsChanged;
  bool get planned => widget.planned;
  final _controllers = <String, TextEditingController>{};
  final _focus = <String, FocusNode>{};
  TextEditingController _controller(String id) => _controllers.putIfAbsent(
    id,
    () => TextEditingController(text: values[id]?.toString() ?? ''),
  );
  @override
  void didUpdateWidget(covariant SubjectFieldsEditor oldWidget) {
    super.didUpdateWidget(oldWidget);
    for (final field in fields) {
      if (oldWidget.values[field.id] != values[field.id] &&
          _focus[field.id]?.hasFocus != true) {
        final text = values[field.id]?.toString() ?? '';
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted &&
              _focus[field.id]?.hasFocus != true &&
              (values[field.id]?.toString() ?? '') == text &&
              _controller(field.id).text != text) {
            _controller(field.id).text = text;
          }
        });
      }
    }
  }

  @override
  void dispose() {
    for (final controller in _controllers.values) {
      controller.dispose();
    }
    for (final focus in _focus.values) {
      focus.dispose();
    }
    super.dispose();
  }

  void _value(String id, dynamic value) => onChanged({...values, id: value});
  Future<void> _definition(BuildContext context, SubjectField? field) async {
    final name = TextEditingController(text: field?.name),
        unit = TextEditingController(text: field?.unit),
        options = TextEditingController(text: field?.options.join('，'));
    var type = field?.type ?? 'text';
    final dialogRoute = DialogRoute<SubjectField>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, setState) => AlertDialog(
          title: Text(field == null ? '添加字段' : '编辑字段'),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextField(
                  controller: name,
                  decoration: const InputDecoration(labelText: '字段名称'),
                ),
                DropdownButtonFormField<String>(
                  initialValue: type,
                  decoration: const InputDecoration(labelText: '类型'),
                  items: const [
                    DropdownMenuItem(value: 'text', child: Text('文本')),
                    DropdownMenuItem(value: 'number', child: Text('数字')),
                    DropdownMenuItem(value: 'date', child: Text('日期')),
                    DropdownMenuItem(value: 'boolean', child: Text('是／否')),
                    DropdownMenuItem(value: 'select', child: Text('单选')),
                    DropdownMenuItem(value: 'phone', child: Text('电话号码')),
                  ],
                  onChanged: (v) => setState(() => type = v!),
                ),
                if (type == 'number')
                  TextField(
                    controller: unit,
                    decoration: const InputDecoration(labelText: '单位'),
                  ),
                if (type == 'select')
                  TextField(
                    controller: options,
                    decoration: const InputDecoration(labelText: '选项（逗号分隔）'),
                  ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () {
                if (name.text.trim().isEmpty) return;
                Navigator.pop(
                  context,
                  SubjectField(
                    id: field?.id ?? newStorylineKey(),
                    name: name.text.trim(),
                    type: type,
                    unit: unit.text.isEmpty ? null : unit.text,
                    options: options.text
                        .split(RegExp('[,，]'))
                        .map((x) => x.trim())
                        .where((x) => x.isNotEmpty)
                        .toList(),
                    key: field?.key,
                  ),
                );
              },
              child: const Text('保存'),
            ),
          ],
        ),
      ),
    );
    final result = await Navigator.of(context).push(dialogRoute);
    await dialogRoute.completed;
    name.dispose();
    unit.dispose();
    options.dispose();
    if (result != null && mounted) {
      onFieldsChanged?.call(
        field == null
            ? [...fields, result]
            : fields.map((x) => x.id == field.id ? result : x).toList(),
      );
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      if (widget.showHeading)
        Text(
          planned ? '预期字段变化' : '生活字段',
          style: const TextStyle(fontWeight: FontWeight.bold),
        ),
      if (widget.showHeading) const SizedBox(height: 16),
      if (planned) const Text('预期值不会提前更新资料，完成时需确认实际值。'),
      for (final field in fields.where((x) => !x.removed))
        Padding(
          padding: const EdgeInsets.only(bottom: 20),
          child: Column(
            children: [
              if (field.type == 'boolean')
                DropdownButtonFormField<String>(
                  isExpanded: true,
                  key: ValueKey('${field.id}:${values[field.id]}'),
                  initialValue: values[field.id]?.toString(),
                  decoration: InputDecoration(labelText: field.name),
                  items: const [
                    DropdownMenuItem(value: 'true', child: Text('是')),
                    DropdownMenuItem(value: 'false', child: Text('否')),
                  ],
                  onChanged: (v) =>
                      _value(field.id, v == null ? null : v == 'true'),
                )
              else if (field.type == 'select')
                DropdownButtonFormField<String>(
                  isExpanded: true,
                  key: ValueKey('${field.id}:${values[field.id]}'),
                  initialValue: values[field.id] as String?,
                  decoration: InputDecoration(labelText: field.name),
                  items: field.options
                      .map((o) => DropdownMenuItem(value: o, child: Text(o)))
                      .toList(),
                  onChanged: (v) => _value(field.id, v),
                )
              else
                TextFormField(
                  key: ValueKey(field.id),
                  controller: _controller(field.id),
                  focusNode: _focus.putIfAbsent(field.id, () => FocusNode()),
                  decoration: InputDecoration(
                    labelText: field.unit == null
                        ? field.name
                        : '${field.name} (${field.unit})',
                    hintText: field.type == 'date' ? 'YYYY-MM-DD' : null,
                  ),
                  keyboardType: field.type == 'number'
                      ? const TextInputType.numberWithOptions(
                          decimal: true,
                          signed: true,
                        )
                      : field.type == 'phone'
                      ? TextInputType.phone
                      : field.type == 'date'
                      ? TextInputType.datetime
                      : TextInputType.text,
                  validator: (v) {
                    if (v == null || v.isEmpty) return null;
                    if (field.type == 'number' && num.tryParse(v) == null) {
                      return '请输入数字';
                    }
                    if (field.type == 'date' &&
                        !RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(v)) {
                      return '请填写 YYYY-MM-DD';
                    }
                    return null;
                  },
                  onChanged: (v) => _value(
                    field.id,
                    v.isEmpty
                        ? null
                        : field.type == 'number'
                        ? num.tryParse(v)
                        : v,
                  ),
                ),
              if (onFieldsChanged != null)
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Align(
                    alignment: Alignment.centerRight,
                    child: Wrap(
                      alignment: WrapAlignment.end,
                      spacing: 8,
                      children: [
                        TextButton(
                          onPressed: () => _definition(context, field),
                          child: const Text('编辑字段'),
                        ),
                        TextButton(
                          onPressed: () => onFieldsChanged!(
                            fields
                                .map(
                                  (x) => x.id == field.id
                                      ? x.copyWith(removed: true)
                                      : x,
                                )
                                .toList(),
                          ),
                          child: const Text('移除字段'),
                        ),
                      ],
                    ),
                  ),
                ),
            ],
          ),
        ),
      if (onFieldsChanged != null)
        TextButton.icon(
          onPressed: () => _definition(context, null),
          icon: const Icon(Icons.add),
          label: const Text('添加自定义字段'),
        ),
    ],
  );
}
