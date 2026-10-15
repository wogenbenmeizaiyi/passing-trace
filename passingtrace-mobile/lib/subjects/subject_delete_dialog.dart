import 'dart:async';

import 'package:flutter/material.dart';

import '../auth_service.dart';
import '../events/ai_api.dart';
import '../events/events_api.dart';
import '../storylines/storyline_id.dart';
import 'subjects_api.dart';

Future<AiMutationResultModel?> showSubjectDeleteDialog({
  required BuildContext context,
  required SubjectsApi api,
  required AuthSession session,
  required String targetType,
  required String targetId,
  String? contextLabel,
}) => showDialog<AiMutationResultModel>(
  context: context,
  barrierDismissible: false,
  builder: (_) => _SubjectDeleteDialog(
    api: api,
    session: session,
    targetType: targetType,
    targetId: targetId,
    contextLabel: contextLabel,
  ),
);

class _SubjectDeleteDialog extends StatefulWidget {
  const _SubjectDeleteDialog({
    required this.api,
    required this.session,
    required this.targetType,
    required this.targetId,
    this.contextLabel,
  });
  final SubjectsApi api;
  final AuthSession session;
  final String targetType, targetId;
  final String? contextLabel;

  @override
  State<_SubjectDeleteDialog> createState() => _SubjectDeleteDialogState();
}

class _SubjectDeleteDialogState extends State<_SubjectDeleteDialog> {
  final _requestId = newStorylineKey();
  AiApprovalModel? _approval;
  AiMutationResultModel? _result;
  bool _busy = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_prepare());
  }

  String get _title => switch (widget.targetType) {
    'SubjectRelation' => '移除这条关系？',
    'SubjectEntry' => '删除这条记录或计划？',
    _ => '删除这个档案？',
  };

  String _errorMessage(Object error) =>
      error is EventApiException && error.status < 500
      ? error.message
      : '暂时无法处理，请重试。重复点击不会再次删除。';

  Future<void> _prepare() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final approval = await widget.api.requestDelete(
        widget.session,
        widget.targetType,
        widget.targetId,
        _requestId,
      );
      if (mounted) setState(() => _approval = approval);
    } catch (error) {
      if (mounted) setState(() => _error = _errorMessage(error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _decide(String decision) async {
    if (_busy) return;
    if (_result != null) {
      Navigator.of(context).pop(_result);
      return;
    }
    final approval = _approval;
    if (approval == null) {
      if (decision == 'cancel') Navigator.of(context).pop();
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await widget.api.decideDelete(
        widget.session,
        approval,
        decision,
      );
      if (!mounted) return;
      if (result.state == 'Succeeded' || result.state == 'Cancelled') {
        Navigator.of(context).pop(result);
      } else {
        setState(() => _result = result);
      }
    } catch (error) {
      if (mounted) setState(() => _error = _errorMessage(error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: false,
    onPopInvokedWithResult: (didPop, result) {
      if (!didPop) unawaited(_decide('cancel'));
    },
    child: AlertDialog(
      title: Text(_title),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (_approval != null) ...[
              Text(
                _approval!.title,
                style: Theme.of(context).textTheme.titleMedium,
              ),
              if (widget.contextLabel != null) ...[
                const SizedBox(height: 8),
                Text(widget.contextLabel!),
              ],
              const SizedBox(height: 16),
              Text(_approval!.description),
            ],
            if (_busy) ...[
              const SizedBox(height: 16),
              Row(
                children: [
                  const SizedBox.square(
                    dimension: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Text(_approval == null ? '正在核实目标…' : '正在处理…'),
                  ),
                ],
              ),
            ],
            if (_error != null || _result != null) ...[
              const SizedBox(height: 16),
              Semantics(
                liveRegion: true,
                child: Text(
                  _error ?? _result!.message.content,
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
              ),
            ],
          ],
        ),
      ),
      actions: [
        if (_result != null)
          TextButton(
            onPressed: _busy ? null : () => _decide('cancel'),
            child: const Text('关闭'),
          )
        else ...[
          TextButton(
            onPressed: _busy ? null : () => _decide('cancel'),
            child: const Text('取消'),
          ),
          if (_approval == null)
            TextButton(
              onPressed: _busy ? null : _prepare,
              child: const Text('重试'),
            )
          else
            TextButton(
              onPressed: _busy ? null : () => _decide('confirm'),
              style: TextButton.styleFrom(
                foregroundColor: Theme.of(context).colorScheme.error,
              ),
              child: const Text('确定'),
            ),
        ],
      ],
    ),
  );
}
