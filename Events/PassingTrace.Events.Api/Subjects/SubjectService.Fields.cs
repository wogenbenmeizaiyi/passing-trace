using System.Globalization;
using System.Text.Json;
using PassingTrace.Core.Events;
using PassingTrace.Core.Subjects;

namespace PassingTrace.Events.Api.Subjects;

public sealed partial class SubjectService
{
    public static IReadOnlyList<SubjectField> Presets(SubjectKind kind, string? itemType)
    {
        SubjectField F(string key, string name, string type, string? unit = null) => new(Guid.NewGuid(), name, type, unit, Key: key);
        return kind switch
        {
            SubjectKind.Person => [F("birthday", "生日", "date"), F("height", "身高", "number", "cm"), F("weight", "体重", "number", "kg"), F("phone", "电话号码", "phone")],
            SubjectKind.Pet => [F("breed", "品种", "text"), F("birthday", "生日", "date"), F("arrival", "到家日期", "date"), F("weight", "体重", "number", "kg")],
            _ => itemType switch
            {
                "vehicle" => [F("model", "品牌型号", "text"), F("plate", "车牌", "text"), F("purchase", "购入日期", "date"), F("price", "购入价格", "number", "元"), F("mileage", "里程", "number", "km")],
                "property" => [F("address", "地址", "text"), F("area", "面积", "number", "m²"), F("purchase", "购入日期", "date"), F("price", "购入价格", "number", "元")],
                "bicycle" => [F("model", "品牌型号", "text"), F("frame", "车架号", "text"), F("purchase", "购入日期", "date"), F("price", "购入价格", "number", "元")],
                _ => [F("model", "品牌型号", "text"), F("purchase", "购入日期", "date"), F("price", "购入价格", "number", "元")]
            }
        };
    }

    private static IReadOnlyList<SubjectField> ValidateFields(IReadOnlyList<SubjectField> fields, IReadOnlyList<SubjectField> previous)
    {
        if (fields.Count > 100) throw new DomainValidationException("一份档案最多包含 100 个字段。");
        var result = fields.Select(f => f with { Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id, Name = Text(f.Name, "字段名称", 100) }).ToArray();
        if (result.Select(x => x.Id).Distinct().Count() != result.Length) throw new DomainValidationException("字段 ID 不能重复。");
        foreach (var field in result)
        {
            if (field.Type is not ("text" or "number" or "date" or "boolean" or "select" or "phone")) throw new DomainValidationException("字段类型无效。");
            if (field.Unit?.Length > 30) throw new DomainValidationException("单位过长。");
            if (field.Type == "select" && (field.Options is null || field.Options.Count is < 1 or > 100 || field.Options.Distinct().Count() != field.Options.Count))
                throw new DomainValidationException("单选字段需要 1 至 100 个不重复选项。");
            var old = previous.FirstOrDefault(x => x.Id == field.Id);
            if (old is not null && old.Key != field.Key) throw new DomainValidationException("预设字段标识不能更改。");
        }
        return result;
    }

    private static Dictionary<string, JsonElement> ValidateValues(Subject subject, Dictionary<string, JsonElement>? values)
    {
        var fields = Read<SubjectField[]>(subject.FieldsJson).Where(x => !x.Removed).ToDictionary(x => x.Id.ToString());
        foreach (var pair in values ?? [])
        {
            if (!fields.TryGetValue(pair.Key, out var field)) throw new DomainValidationException("变化值引用了不存在或已移除的字段。");
            var value = pair.Value;
            if (value.ValueKind == JsonValueKind.Null) continue;
            var valid = field.Type switch
            {
                "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "date" => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "select" => value.ValueKind == JsonValueKind.String && field.Options!.Contains(value.GetString()),
                _ => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 4000
            };
            if (!valid) throw new DomainValidationException($"字段“{field.Name}”的值不符合类型或选项。");
        }
        return values ?? [];
    }

    private async Task RebuildFieldsAsync(Subject subject, CancellationToken ct)
    {
        var values = new Dictionary<string, JsonElement>();
        var entries = (await repository.EntriesAsync(subject.UserId, subject.Id, ct))
            .Where(x => x.DeletedAt == null && x.State == SubjectEntryState.Completed)
            .OrderBy(x => x.HappenedAt ?? x.CreatedAt).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id);
        foreach (var entry in entries)
            foreach (var field in Read<Dictionary<string, JsonElement>>(entry.Kind == SubjectEntryKind.Plan ? entry.ActualFieldChangesJson : entry.FieldChangesJson))
                values[field.Key] = field.Value;
        subject.ValuesJson = Write(values);
    }
}
