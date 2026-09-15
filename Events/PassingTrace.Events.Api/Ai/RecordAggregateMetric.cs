using System.Text.Json.Serialization;

namespace PassingTrace.Events.Api.Ai;

/// <summary>Stable model-facing names for the server's read-only statistics whitelist.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecordAggregateMetric>))]
public enum RecordAggregateMetric
{
    [JsonStringEnumMemberName("count")]
    Count,
    [JsonStringEnumMemberName("expense_total")]
    ExpenseTotal,
    [JsonStringEnumMemberName("trend")]
    Trend,
    [JsonStringEnumMemberName("plan_completion_rate")]
    PlanCompletionRate,
}
