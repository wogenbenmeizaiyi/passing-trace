using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PassingTrace.Events.Api.Ai.Capabilities;

/// <summary>
/// Validates model-provided metrics before enum binding. One explicit correction is allowed per
/// request, without executing a query or adding failed results to the evidence snapshot.
/// </summary>
internal sealed class StatisticsAIFunction(AIFunction innerFunction) : DelegatingAIFunction(innerFunction)
{
    private static readonly string[] AllowedMetrics = Enum.GetValues<RecordAggregateMetric>()
        .Select(metric => JsonSerializer.SerializeToElement(metric).GetString()!).ToArray();
    private int _invalidInvocations;

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (arguments.TryGetValue("metric", out var value) && TryReadMetric(value, out var metric))
        {
            // Use the validated enum value; do not rely on the serializer accepting numeric enums.
            var validatedArguments = new AIFunctionArguments(arguments)
            {
                Services = arguments.Services,
                Context = arguments.Context,
                ["metric"] = metric,
            };
            return InnerFunction.InvokeAsync(validatedArguments, cancellationToken);
        }

        if (Interlocked.Increment(ref _invalidInvocations) > 1)
            throw new AssistantStatisticsToolException();

        return ValueTask.FromResult<object?>(JsonSerializer.SerializeToElement(new
        {
            success = false,
            error = "invalid_statistics_metric",
            allowedMetrics = AllowedMetrics,
            instruction = "请按问题选择明确的统计类型：金额合计用 expense_total，记录数量用 count，月度趋势用 trend，计划完成率用 plan_completion_rate。只允许修正后再调用一次；不得把失败当作金额为零，也不要向用户复述工具参数或纠错过程。",
        }));
    }

    private static bool TryReadMetric(object? value, out RecordAggregateMetric metric)
    {
        if (value is RecordAggregateMetric typed && Enum.IsDefined(typed))
        {
            metric = typed;
            return true;
        }

        var text = value switch
        {
            string literal => literal,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => null,
        };
        var index = Array.FindIndex(AllowedMetrics, allowed => string.Equals(allowed, text, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            metric = Enum.GetValues<RecordAggregateMetric>()[index];
            return true;
        }

        metric = default;
        return false;
    }
}
