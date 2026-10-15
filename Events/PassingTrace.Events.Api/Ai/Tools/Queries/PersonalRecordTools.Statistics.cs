using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai.Evidence;

namespace PassingTrace.Events.Api.Ai.Tools.Queries;

public sealed partial class PersonalRecordTools
{
    [Description("对当前用户记录执行精确统计：金额或消费合计用 expense_total，记录数量用 count，月度趋势用 trend，计划完成率用 plan_completion_rate。总金额跨主分类汇总所有金额事实，不依赖搜索分页或金额标签，不要自行限定美食、购物等分类。不接受其他统计类型，不执行模型生成的 SQL。用户询问所有记录时不要添加时间范围；缺少金额事实不能解释为没有消费。")]
    public async Task<EvidenceBundle> AggregateMyRecordsAsync(
        [Description("必填：金额合计 expense_total；数量 count；月度趋势 trend；计划完成率 plan_completion_rate。")] RecordAggregateMetric metric,
        [DataType(DataType.DateTime), Description("时间范围起点，ISO 8601，带时区；统计全部时留空。")] string? from = null,
        [DataType(DataType.DateTime), Description("时间范围终点，ISO 8601，带时区；统计全部时留空。")] string? to = null,
        [RegularExpression("^[A-Za-z]{3}$"), Description("金额统计的币种代码，默认 CNY；不同币种不混合求和。")] string? currency = null,
        [Description("已知记录主分类 key，可空，不猜测不存在的分类。")] string? category = null,
        [Description("已知记录标签 key，可空。")] string? tag = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(metric)) throw new AssistantStatisticsToolException();
        if (_currentMonthRange is not null)
        {
            // Never let a model substitute its training-date month for an explicit "this month".
            from = _currentMonthRange.FromText;
            to = _currentMonthRange.ToText;
        }
        var userId = currentUser.UserId;
        var filter = new RecordStatisticsFilter(
            DateTimeOffset.TryParse(from, out var fromValue) ? fromValue.ToUniversalTime() : null,
            DateTimeOffset.TryParse(to, out var toValue) ? toValue.ToUniversalTime() : null,
            NormalizeKey(category), metric == RecordAggregateMetric.ExpenseTotal && string.Equals(tag, "amount", StringComparison.OrdinalIgnoreCase)
                ? null : NormalizeKey(tag));
        object result = metric switch
        {
            RecordAggregateMetric.Count => new { metric = "count", value = await queries.CountRecordsAsync(userId, filter, cancellationToken) },
            RecordAggregateMetric.ExpenseTotal => await AggregateExpensesAsync(filter, userId, currency, cancellationToken),
            RecordAggregateMetric.PlanCompletionRate => await AggregateCompletionAsync(filter, userId, cancellationToken),
            RecordAggregateMetric.Trend => await AggregateTrendAsync(filter, userId, cancellationToken),
            _ => throw new AssistantStatisticsToolException(),
        };
        _aggregateEvidence = JsonSerializer.Serialize(result);
        _aggregateTimeRange = BuildTimeRange(from, to);
        return new EvidenceBundle([], [], _aggregateEvidence, _aggregateTimeRange);
    }

    private async Task<object> AggregateExpensesAsync(RecordStatisticsFilter filter, long userId, string? currency, CancellationToken cancellationToken)
    {
        currency = string.IsNullOrWhiteSpace(currency) ? "CNY" : currency.ToUpperInvariant();
        var total = await queries.SumExpensesAsync(userId, filter, currency, cancellationToken);
        return new
        {
            metric = "expense_total",
            value = total.Value,
            currency,
            amountFactCount = total.Count,
            hasAmountData = total.Value is not null,
            explanation = total.Value is null
                ? "所选范围内没有可核实的金额，无法确认消费合计；不能解释为消费为零。"
                : "仅汇总所选范围内已确认的金额，不代表没有记下的实际消费。",
        };
    }

    private async Task<object> AggregateCompletionAsync(RecordStatisticsFilter filter, long userId, CancellationToken cancellationToken)
    {
        var result = await queries.CountPlanCompletionAsync(userId, filter, cancellationToken);
        return new
        {
            metric = "plan_completion_rate",
            completed = result.Completed,
            total = result.Total,
            value = result.Total == 0 ? 0 : result.Completed / (double)result.Total
        };
    }

    private async Task<object> AggregateTrendAsync(RecordStatisticsFilter filter, long userId, CancellationToken cancellationToken)
    {
        var rows = await queries.CountMonthlyRecordsAsync(userId, filter, cancellationToken);
        return new { metric = "trend", points = rows.Select(x => new { month = $"{x.Year:D4}-{x.Month:D2}", count = x.Count }).ToArray() };
    }
}
