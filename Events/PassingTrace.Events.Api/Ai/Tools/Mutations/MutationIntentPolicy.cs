using System.Text.RegularExpressions;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai.Assistant.Context;

namespace PassingTrace.Events.Api.Ai.Tools.Mutations;

internal static class MutationIntentPolicy
{
    private static readonly string[] Operations = ["create", "update", "delete"];
    private const string AddVerbs = "加上|加进去|加一下|补上|追加|加(?:个|一(?:个|条))|(?:请|帮我|麻烦你|你)(?:先)?加(?:[，。！？,.!?\\s]|$)";

    public static IReadOnlySet<string> Resolve(string question, IReadOnlyList<ConversationContextMessage>? history)
    {
        var allowed = Operations.Where(operation => HasIntent(question, operation)).ToHashSet(StringComparer.Ordinal);
        if (allowed.Count > 0 || !IsConfirmation(question) || history is null) return allowed;

        // Bind a short confirmation to the latest pending proposal and the user's own request.
        // Summaries, assistant suggestions and completed receipts never grant write permission.
        var recent = history.TakeLast(12).ToArray();
        var proposalIndex = Array.FindLastIndex(recent, message => message.Role == AiMessageRole.Assistant);
        if (proposalIndex < 1 || recent[proposalIndex].IsMutationReceipt ||
            recent.Skip(proposalIndex + 1).Any(message => message.Role != AiMessageRole.User || !IsConfirmation(message.Content)))
            return allowed;
        var requestIndex = Array.FindLastIndex(recent, proposalIndex - 1, message => message.Role == AiMessageRole.User);
        if (requestIndex < 0 || recent.Skip(requestIndex + 1).Any(message => message.IsMutationReceipt) ||
            !Regex.IsMatch(recent[proposalIndex].Content,
            "确认|确定|同意|可以吗|可不可以|要不要|是否|你回|点头|\\b(?:confirm|should i)\\b", RegexOptions.IgnoreCase))
            return allowed;

        foreach (var operation in new[] { "create", "update" })
            if (HasIntent(recent[requestIndex].Content, operation) && HasIntent(recent[proposalIndex].Content, operation))
                allowed.Add(operation);
        return allowed;
    }

    private static bool IsConfirmation(string question) => Regex.IsMatch(question.Trim(),
        "^(?:(?:好|好的|对|是的|嗯|可以)[，,\\s]*)?(?:就这样|就按(?:这个|那个|刚才的|刚才(?:的)?方案)来|按(?:这个|刚才(?:的)?方案|你说的)来|确定|确认|同意|好(?:的)?|对(?:的)?|是的|嗯|可以|yes|ok|okay|go ahead)[。.!！\\s]*$",
        RegexOptions.IgnoreCase);

    public static bool HasIntent(string question, string operation)
    {
        var verbs = operation switch
        {
            "create" => $"保存|创建|新建|记下来|记下|记一笔|记录一下|(?:请|帮我)记录|^记录|写一条|写(?:记录|计划)|整理成(?:记录|计划|故事线)|新增|添加|建立|建(?:个|一(?:个|条)|故事线|计划|记录)|我就建|{AddVerbs}|\\b(?:save|create|add)\\b",
            "update" => $"编辑|修改|更新|改成|改为|调整|同步|完成|结束|恢复|纠正|终止|取消.*计划|移除.*节点|节点.*移除|挪.*(?:时间|计划)|(?:时间|计划).*挪|{AddVerbs}|\\b(?:edit|update|change|complete|resume|end)\\b",
            _ => "删(?:除|掉|了|去)?|移除|\\b(?:delete|remove)\\b",
        };
        // A negative instruction or a question about capability is not a grant to execute it.
        if (Regex.IsMatch(question, $"(?:不要|别|不用|无需|不想|不需要|禁止|取消|暂不|先不|do not|don't)[^，,。.!！?？;；\\n]{{0,12}}(?:{verbs})", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(question, "(?:能否|是否|可以|能不能).*(?:功能|能力)|如何|怎么(?:创建|删除|修改)|how (?:to|do)", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(question, "^我(?:今天|昨天|刚才|刚刚|已经|之前)?(?:创建|保存|修改|删除|记录|加上|追加|添加)了", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(question, "^(?:建议|比如|假设|考虑|介绍|解释).*(?:记录|计划|故事线)", RegexOptions.IgnoreCase)) return false;
        return Regex.IsMatch(question, verbs, RegexOptions.IgnoreCase);
    }
}
