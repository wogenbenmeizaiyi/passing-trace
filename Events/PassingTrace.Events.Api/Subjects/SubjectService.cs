using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Subjects;
using PassingTrace.Events.Api.Media;

namespace PassingTrace.Events.Api.Subjects;

/// <summary>Private dossiers and their independent activities. Persistence is accessed only through Core ports.</summary>
public sealed partial class SubjectService(ISubjectRepository repository, IEventMediaService media,
    IAnalysisOutbox outbox, TimeProvider clock) : IEventSubjectService
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)!;
    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Write(value))));

    public Task<SubjectResponse> EnsureSelfAsync(long userId, CancellationToken ct) => repository.ExecuteAsync(userId,
        async token => ToResponse(await EnsureSelfCoreAsync(userId, token)), ct);

    private async Task<Subject> EnsureSelfCoreAsync(long userId, CancellationToken ct)
    {
        var self = await repository.SelfAsync(userId, ct);
        if (self is not null)
        {
            if (self.FieldsJson == "[]") { self.FieldsJson = Write(Presets(SubjectKind.Person, null)); Touch(self); await ChangedAsync(userId, ct); }
            return self;
        }
        var now = clock.GetUtcNow();
        self = new Subject
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IsSelf = true,
            Kind = SubjectKind.Person,
            Name = "自己",
            FieldsJson = Write(Presets(SubjectKind.Person, null)),
            CreatedAt = now,
            UpdatedAt = now
        };
        repository.Add(self);
        History(userId, "Subject", self.Id, self.Revision, self);
        await ChangedAsync(userId, ct);
        return self;
    }

    public async Task<IReadOnlyList<SubjectResponse>> ListAsync(long userId, CancellationToken ct)
    {
        await EnsureSelfAsync(userId, ct);
        return (await repository.SubjectsAsync(userId, false, ct)).Select(ToResponse).ToArray();
    }
    public async Task<SubjectResponse> GetAsync(long userId, Guid id, CancellationToken ct) => ToResponse(await OwnedAsync(userId, id, ct));
    private async Task<Subject> OwnedAsync(long userId, Guid id, CancellationToken ct) =>
        await repository.SubjectAsync(userId, id, false, ct) ?? throw new KeyNotFoundException("人物档案不存在。");
    private static void Version(int actual, int expected)
    {
        if (actual != expected) throw new ConcurrencyException("内容已更新，请刷新后重试。");
    }
    private static string Text(string? value, string label, int max)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0 || text.Length > max) throw new DomainValidationException($"{label}不能为空且不能超过 {max} 字。");
        return text;
    }
    private static string Zone(string? zone)
    {
        var value = string.IsNullOrWhiteSpace(zone) ? "UTC" : zone.Trim();
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(value); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new DomainValidationException("时区无效。"); }
        return value;
    }
    private void History(long userId, string type, Guid id, int version, object value) => repository.Add(new SubjectHistory
    { UserId = userId, TargetType = type, TargetId = id, Revision = version, SnapshotJson = Write(value), CreatedAt = clock.GetUtcNow() });
    private async Task ChangedAsync(long userId, CancellationToken ct)
    {
        await outbox.IncrementWatermarkAsync(userId, clock.GetUtcNow(), ct);
        await repository.SaveAsync(ct);
    }
    private void Touch(Subject subject)
    {
        subject.Revision++;
        subject.UpdatedAt = clock.GetUtcNow();
        History(subject.UserId, "Subject", subject.Id, subject.Revision, subject);
    }

    public Task<SubjectResponse> CreateAsync(long userId, CreateSubjectRequest request, string? key, CancellationToken ct) =>
        repository.ExecuteAsync(userId, async token =>
        {
            await EnsureSelfCoreAsync(userId, token);
            if (!string.IsNullOrWhiteSpace(key) && await repository.FindSubjectKeyAsync(userId, key, token) is { } prior)
            {
                if (prior.RequestHash != Hash(request)) throw new IdempotencyConflictException(key);
                return ToResponse(prior);
            }
            if (!Enum.IsDefined(request.Kind)) throw new DomainValidationException("人物类型无效。");
            if (request.Relations is null || request.Relations.Count == 0) throw new DomainValidationException("请选择至少一个关联档案，使它连接到自己。");
            var now = clock.GetUtcNow();
            var fields = ValidateFields(request.Fields ?? Presets(request.Kind, request.ItemType), []);
            await media.ResolveAsync(userId, request.MediaIds, token);
            if (request.CoverMediaId is { } cover) await media.ResolveAsync(userId, [cover], token);
            var subject = new Subject
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Kind = request.Kind,
                ItemType = request.ItemType,
                Name = Text(request.Name, "名称", 200),
                Description = request.Description,
                Timezone = Zone(request.Timezone),
                StartedAt = request.StartedAt?.ToUniversalTime(),
                FieldsJson = Write(fields),
                MediaIdsJson = Write(request.MediaIds ?? []),
                CoverMediaId = request.CoverMediaId,
                IdempotencyKey = key,
                RequestHash = Hash(request),
                CreatedAt = now,
                UpdatedAt = now
            };
            foreach (var relation in request.Relations) await OwnedAsync(userId, relation.ToSubjectId, token);
            repository.Add(subject);
            foreach (var relation in request.Relations) AddRelation(subject, relation);
            await repository.SaveAsync(token);
            await ValidateGraphAsync(userId, token);
            await AddEntryCoreAsync(userId, subject, new(SubjectEntryKind.Record, "建档", request.Description,
                request.StartedAt ?? now, Timezone: subject.Timezone, FieldChanges: request.Values), "initial:" + subject.Id, token);
            await RebuildFieldsAsync(subject, token);
            SaveMedia(subject.UserId, "Subject", subject.Id, subject.Revision, Read<Guid[]>(subject.MediaIdsJson).Concat(subject.CoverMediaId is { } c ? [c] : []).Distinct());
            History(userId, "Subject", subject.Id, subject.Revision, subject);
            await ChangedAsync(userId, token);
            return ToResponse(subject);
        }, ct);

    public Task<SubjectResponse> UpdateAsync(long userId, Guid id, int version, UpdateSubjectRequest request, CancellationToken ct) =>
        repository.ExecuteAsync(userId, async token =>
        {
            var subject = await OwnedAsync(userId, id, token);
            Version(subject.Revision, version);
            if (request.Name is not null)
            {
                if (subject.IsSelf) throw new DomainValidationException("自己的名称沿用账号资料，请在账号页面修改。");
                subject.Name = Text(request.Name, "名称", 200);
            }
            if (request.Description is not null) subject.Description = request.Description;
            if (request.Fields is not null)
            {
                var previous = Read<SubjectField[]>(subject.FieldsJson);
                var next = ValidateFields(request.Fields, previous);
                var history = await repository.EntriesAsync(userId, id, token);
                foreach (var field in next)
                {
                    var old = previous.FirstOrDefault(x => x.Id == field.Id);
                    if (old is not null && (old.Type != field.Type || old.Unit != field.Unit) &&
                        history.Any(x => Read<Dictionary<string, JsonElement>>(x.FieldChangesJson).ContainsKey(field.Id.ToString()) ||
                            Read<Dictionary<string, JsonElement>>(x.ActualFieldChangesJson).ContainsKey(field.Id.ToString())))
                        throw new DomainValidationException("已有历史值的字段不能直接改变类型或单位，请创建新字段。");
                }
                // Omitted definitions become hidden, rather than erasing their history.
                subject.FieldsJson = Write(next.Concat(previous.Where(x => next.All(n => n.Id != x.Id)).Select(x => x with { Removed = true })).ToArray());
            }
            if (request.MediaIds is not null) { await media.ResolveAsync(userId, request.MediaIds, token); subject.MediaIdsJson = Write(request.MediaIds); }
            if (request.ClearCover) subject.CoverMediaId = null;
            else if (request.CoverMediaId is { } cover) { await media.ResolveAsync(userId, [cover], token); subject.CoverMediaId = cover; }
            if (request.Values is { Count: > 0 })
                await AddEntryCoreAsync(userId, subject, new(SubjectEntryKind.Record, "更新资料", HappenedAt: request.EffectiveAt ?? clock.GetUtcNow(),
                    Timezone: subject.Timezone, FieldChanges: request.Values), null, token);
            await RebuildFieldsAsync(subject, token);
            subject.Revision++;
            subject.UpdatedAt = clock.GetUtcNow();
            SaveMedia(userId, "Subject", id, subject.Revision, Read<Guid[]>(subject.MediaIdsJson).Concat(subject.CoverMediaId is { } c ? [c] : []).Distinct());
            History(userId, "Subject", id, subject.Revision, subject);
            await ChangedAsync(userId, token);
            return ToResponse(subject);
        }, ct);

    public async Task<SubjectGraphResponse> GraphAsync(long userId, CancellationToken ct)
    {
        var subjects = await ListAsync(userId, ct);
        var ids = subjects.Select(x => x.Id).ToHashSet();
        return new(subjects.Single(x => x.IsSelf).Id, subjects,
            (await repository.RelationsAsync(userId, ct)).Where(x => x.RemovedAt == null && ids.Contains(x.FromSubjectId) && ids.Contains(x.ToSubjectId)).ToArray());
    }

    private async Task ValidateGraphAsync(long userId, CancellationToken ct, Guid? excludedSubject = null, Guid? excludedRelation = null)
    {
        var subjects = (await repository.SubjectsAsync(userId, false, ct)).Where(x => x.DeletedAt == null && x.Id != excludedSubject).ToArray();
        var root = subjects.Single(x => x.IsSelf);
        var ids = subjects.Select(x => x.Id).ToHashSet();
        var adjacent = ids.ToDictionary(x => x, _ => new List<Guid>());
        foreach (var edge in (await repository.RelationsAsync(userId, ct)).Where(x => x.RemovedAt == null && x.Id != excludedRelation && ids.Contains(x.FromSubjectId) && ids.Contains(x.ToSubjectId)))
        { adjacent[edge.FromSubjectId].Add(edge.ToSubjectId); adjacent[edge.ToSubjectId].Add(edge.FromSubjectId); }
        var visited = new HashSet<Guid> { root.Id };
        var queue = new Queue<Guid>(); queue.Enqueue(root.Id);
        while (queue.TryDequeue(out var node)) foreach (var next in adjacent[node]) if (visited.Add(next)) queue.Enqueue(next);
        if (visited.Count != ids.Count)
            throw new DomainValidationException("操作会使这些档案无法连接到自己，请先补充关联：" + string.Join("、", subjects.Where(x => !visited.Contains(x.Id)).Select(x => x.Name)));
    }

    private void AddRelation(Subject subject, SubjectRelationInput input)
    {
        if (subject.Id == input.ToSubjectId) throw new DomainValidationException("不能关联自身节点。");
        if (input.EndedAt < input.StartedAt) throw new DomainValidationException("关系结束时间不能早于开始时间。");
        var now = clock.GetUtcNow();
        var relation = new SubjectRelation
        {
            Id = Guid.NewGuid(),
            UserId = subject.UserId,
            FromSubjectId = subject.Id,
            ToSubjectId = input.ToSubjectId,
            Label = Text(input.Label, "关系名称", 100),
            Directed = input.Directed,
            StartedAt = input.StartedAt?.ToUniversalTime(),
            EndedAt = input.EndedAt?.ToUniversalTime(),
            CreatedAt = now,
            UpdatedAt = now
        };
        repository.Add(relation);
        History(subject.UserId, "SubjectRelation", relation.Id, relation.Revision, relation);
    }

    public Task<SubjectGraphResponse> AddRelationAsync(long userId, Guid id, int version, SubjectRelationInput request, CancellationToken ct) =>
        repository.ExecuteAsync(userId, async token =>
        {
            var subject = await OwnedAsync(userId, id, token); Version(subject.Revision, version);
            var target = await OwnedAsync(userId, request.ToSubjectId, token);
            if ((await repository.RelationsAsync(userId, token)).Any(x => x.RemovedAt == null && x.FromSubjectId == id && x.ToSubjectId == request.ToSubjectId && x.Label == request.Label && x.EndedAt == null))
                throw new DomainValidationException("这条当前关系已经存在。");
            AddRelation(subject, request); Touch(subject); Touch(target);
            await ChangedAsync(userId, token);
            return await GraphAsync(userId, token);
        }, ct);

    public Task<SubjectRelation> UpdateRelationAsync(long userId, Guid id, int version, UpdateSubjectRelationRequest request, CancellationToken ct) =>
        repository.ExecuteAsync(userId, async token =>
        {
            var relation = await repository.RelationAsync(userId, id, token) ?? throw new KeyNotFoundException("关系不存在。");
            Version(relation.Revision, version);
            if (relation.RemovedAt != null) throw new KeyNotFoundException("关系已移除。");
            var oldFrom = relation.FromSubjectId;
            var oldTo = relation.ToSubjectId;
            if (request.FromSubjectId is { } from) { await OwnedAsync(userId, from, token); relation.FromSubjectId = from; }
            if (request.ToSubjectId is { } to) { await OwnedAsync(userId, to, token); relation.ToSubjectId = to; }
            if (relation.FromSubjectId == relation.ToSubjectId) throw new DomainValidationException("不能关联自身节点。");
            await ValidateGraphAsync(userId, token);
            if (request.Label is not null) relation.Label = Text(request.Label, "关系名称", 100);
            if (request.Directed is { } directed) relation.Directed = directed;
            if (request.StartedAt is { } start) relation.StartedAt = start.ToUniversalTime();
            if (request.EndedAt is { } end) relation.EndedAt = end.ToUniversalTime();
            if (request.Resume) relation.EndedAt = null;
            if (relation.EndedAt < relation.StartedAt) throw new DomainValidationException("关系结束时间不能早于开始时间。");
            relation.Revision++; relation.UpdatedAt = clock.GetUtcNow();
            History(userId, "SubjectRelation", id, relation.Revision, relation);
            foreach (var subjectId in new[] { oldFrom, oldTo, relation.FromSubjectId, relation.ToSubjectId }.Distinct())
                Touch(await OwnedAsync(userId, subjectId, token));
            await ChangedAsync(userId, token); return relation;
        }, ct);

    public Task<bool> DeleteRelationAsync(long userId, Guid id, int version, CancellationToken ct) => repository.ExecuteAsync(userId, async token =>
    {
        var relation = await repository.RelationAsync(userId, id, token) ?? throw new KeyNotFoundException("关系不存在。");
        Version(relation.Revision, version);
        if (relation.RemovedAt != null) throw new KeyNotFoundException("关系已移除。");
        // A rejected approval must not leave staged changes in its surrounding transaction.
        await ValidateGraphAsync(userId, token, excludedRelation: id);
        relation.RemovedAt = clock.GetUtcNow();
        relation.Revision++;
        History(userId, "SubjectRelation", id, relation.Revision, relation);
        Touch(await OwnedAsync(userId, relation.FromSubjectId, token)); Touch(await OwnedAsync(userId, relation.ToSubjectId, token));
        await ChangedAsync(userId, token); return true;
    }, ct);

    public Task<bool> DeleteAsync(long userId, Guid id, int version, CancellationToken ct) => repository.ExecuteAsync(userId, async token =>
    {
        var subject = await OwnedAsync(userId, id, token); Version(subject.Revision, version);
        if (subject.IsSelf) throw new DomainValidationException("自己的档案不能删除。");
        await ValidateGraphAsync(userId, token, excludedSubject: id);
        subject.DeletedAt = clock.GetUtcNow();
        foreach (var relation in (await repository.RelationsAsync(userId, token)).Where(x => x.RemovedAt == null && (x.FromSubjectId == id || x.ToSubjectId == id)))
        { relation.RemovedAt = clock.GetUtcNow(); relation.Revision++; History(userId, "SubjectRelation", relation.Id, relation.Revision, relation); }
        Touch(subject); await ChangedAsync(userId, token); return true;
    }, ct);

    public Task<IReadOnlyList<SubjectHistory>> HistoryAsync(long userId, string type, Guid id, CancellationToken ct) => repository.HistoryAsync(userId, type, id, ct);

    public async Task<SubjectRelation> GetRelationAsync(long userId, Guid id, CancellationToken ct)
    {
        var relation = await repository.RelationAsync(userId, id, ct);
        return relation is { RemovedAt: null } ? relation : throw new KeyNotFoundException("关系不存在。");
    }

    public SubjectResponse ToResponse(Subject s)
    {
        var fields = Read<SubjectField[]>(s.FieldsJson);
        var values = Read<Dictionary<string, JsonElement>>(s.ValuesJson);
        int? age = null;
        var birthday = fields.FirstOrDefault(x => !x.Removed && x.Key == "birthday");
        if (birthday is not null && values.TryGetValue(birthday.Id.ToString(), out var b) && b.ValueKind == JsonValueKind.String &&
            DateOnly.TryParseExact(b.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var born))
        {
            var asOf = s.EndReason == "deceased" ? s.EndedAt ?? clock.GetUtcNow() : clock.GetUtcNow();
            age = Math.Max(0, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(asOf, TimeZoneInfo.FindSystemTimeZoneById(s.Timezone)).Date).DayNumber - born.DayNumber);
        }
        return new(s.Id, s.Kind, s.ItemType, s.Name, s.Description, s.IsSelf, s.State, s.StartedAt, s.EndedAt,
            s.EndReason, s.Revision, s.Timezone, fields, values, Read<Guid[]>(s.MediaIdsJson), s.CoverMediaId, s.UpdatedAt, age);
    }

    private void SaveMedia(long userId, string type, Guid id, int version, IEnumerable<Guid> ids)
    { foreach (var mediaId in ids) repository.Add(new SubjectMediaReference { UserId = userId, TargetType = type, TargetId = id, Revision = version, MediaId = mediaId }); }
}
