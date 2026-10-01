using PassingTrace.Core.Events;
using PassingTrace.Core.Subjects;

namespace PassingTrace.Events.Api.Subjects;

public sealed partial class SubjectService
{
    public async Task<SubjectLifecyclePreview> PreviewLifecycleAsync(long userId, Guid id, CancellationToken ct)
    {
        var subject = await GetAsync(userId, id, ct);
        var entries = (await repository.EntriesAsync(userId, id, ct)).Where(x => x.DeletedAt == null && x.Kind == SubjectEntryKind.Plan && x.State == SubjectEntryState.Planned);
        var plans = new List<SubjectEntryResponse>();
        foreach (var entry in entries) plans.Add(await EntryResponseAsync(entry, ct));
        return new(subject, plans, await repository.MilestonesAsync(userId, id, ct));
    }

    public Task<SubjectResponse> LifecycleAsync(long userId, Guid id, int version, SubjectLifecycleRequest request, CancellationToken ct) =>
        repository.ExecuteAsync(userId, async token =>
        {
            var subject = await OwnedAsync(userId, id, token); Version(subject.Revision, version);
            if (subject.IsSelf) throw new DomainValidationException("自己的档案不能结束或改变生命周期。");
            var at = request.EffectiveAt.ToUniversalTime();
            if (at > clock.GetUtcNow().AddMinutes(1)) throw new DomainValidationException("生命周期操作需要实际日期，未来安排请创建计划。");
            var milestones = await repository.MilestonesAsync(userId, id, token);
            var latest = milestones.Where(x => x.VoidedAt == null && x.Operation is "end" or "resume").OrderBy(x => x.EffectiveAt).ThenBy(x => x.CreatedAt).LastOrDefault();
            if (request.Operation == "end")
            {
                if (subject.State != SubjectState.Active) throw new DomainValidationException("档案已经结束，请使用纠正操作修改结束信息。");
                if (at < (latest?.EffectiveAt ?? subject.StartedAt)) throw new DomainValidationException("结束日期不能早于当前这段生命周期的开始。");
                ValidateReason(subject.Kind, request.Reason);
                var plans = new List<SubjectEntry>();
                foreach (var selected in request.CancelPlans ?? [])
                {
                    var plan = await OwnedEntryAsync(userId, selected.Id, token);
                    Version(plan.Revision, selected.Version);
                    if (plan.SubjectId != id || plan.Kind != SubjectEntryKind.Plan || plan.State != SubjectEntryState.Planned)
                        throw new DomainValidationException("只能选择这个档案所属的待执行计划，引用计划由来源管理。");
                    plans.Add(plan);
                }
                if (plans.Select(x => x.Id).Distinct().Count() != plans.Count) throw new DomainValidationException("不能重复选择计划。");
                foreach (var plan in plans)
                {
                    plan.State = SubjectEntryState.Cancelled; plan.Revision++; plan.UpdatedAt = clock.GetUtcNow();
                    await SaveEntryHistoryAsync(plan, token);
                }
                subject.State = SubjectState.Ended; subject.EndedAt = at; subject.EndReason = request.Reason;
            }
            else if (request.Operation == "resume")
            {
                if (subject.State != SubjectState.Ended || at < subject.EndedAt) throw new DomainValidationException("恢复日期不能早于结束日期。");
                if (subject.EndReason == "deceased") throw new DomainValidationException("离世状态只能纠正误标，不能直接恢复。");
                subject.State = SubjectState.Active; subject.EndedAt = null; subject.EndReason = null;
            }
            else if (request.Operation == "correct")
            {
                if (latest is null || latest.Operation != "end" || latest.Id != request.CorrectsId || subject.State != SubjectState.Ended)
                    throw new DomainValidationException("只能纠正当前结束节点，请重新加载生命周期历史。");
                if (!request.UndoEnd)
                {
                    ValidateReason(subject.Kind, request.Reason);
                    var previous = milestones.Where(x => x.VoidedAt == null && x.Id != latest.Id && x.Operation == "resume").LastOrDefault();
                    if (at < (previous?.EffectiveAt ?? subject.StartedAt)) throw new DomainValidationException("纠正日期不能早于开始日期。");
                }
                latest.VoidedAt = clock.GetUtcNow();
                if (request.UndoEnd) { subject.State = SubjectState.Active; subject.EndedAt = null; subject.EndReason = null; }
                else
                {
                    subject.EndedAt = at; subject.EndReason = request.Reason;
                    repository.Add(new SubjectMilestone
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        SubjectId = id,
                        Operation = "end",
                        Reason = request.Reason,
                        Note = request.Note,
                        EffectiveAt = at,
                        CreatedAt = clock.GetUtcNow()
                    });
                }
            }
            else throw new DomainValidationException("生命周期操作无效。");
            repository.Add(new SubjectMilestone
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SubjectId = id,
                Operation = request.Operation,
                Reason = request.Reason,
                Note = request.Note,
                EffectiveAt = at,
                CreatedAt = clock.GetUtcNow()
            });
            Touch(subject); await ChangedAsync(userId, token); return ToResponse(subject);
        }, ct);

    private static void ValidateReason(SubjectKind kind, string? reason)
    {
        if (reason is not ("deceased" or "relationship-ended" or "sold" or "gifted" or "lost" or "scrapped" or "other") ||
            (reason == "deceased" && kind == SubjectKind.Item)) throw new DomainValidationException("请选择适合档案类型的结束原因。");
    }
}
