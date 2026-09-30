using System.Text.RegularExpressions;

namespace PassingTrace.Events.Api.Ai.Tools.Mutations;

internal static class MutationIntentPolicy
{
    public static bool MayWrite(string question) => HasIntent(question, "create") || HasIntent(question, "update") || HasIntent(question, "delete");

    public static bool HasIntent(string question, string operation)
    {
        var verbs = operation switch
        {
            "create" => "保存|创建|新建|记下来|记下|记一笔|记录一下|(?:请|帮我)记录|^记录|写一条|写(?:记录|计划)|整理成(?:记录|计划|故事线)|新增|添加|建立|\\b(?:save|create|add)\\b",
            "update" => "编辑|修改|更新|改成|改为|调整|同步|移除.*节点|节点.*移除|挪.*(?:时间|计划)|(?:时间|计划).*挪|\\b(?:edit|update|change)\\b",
            _ => "删除|删掉|删了|删去|移除|\\b(?:delete|remove)\\b",
        };
        // A negative instruction or a question about capability is not a grant to execute it.
        if (Regex.IsMatch(question, $"(?:不要|别|不用|无需|不想|不需要|禁止|do not|don't).{{0,12}}(?:{verbs})", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(question, "(?:能否|是否|可以|能不能).*(?:功能|能力)|如何|怎么(?:创建|删除|修改)|how (?:to|do)", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(question, "^我(?:今天|昨天|刚才|刚刚|已经|之前)?(?:创建|保存|修改|删除|记录)了", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(question, "^(?:建议|比如|假设|考虑|介绍|解释).*(?:记录|计划|故事线)", RegexOptions.IgnoreCase)) return false;
        return Regex.IsMatch(question, verbs, RegexOptions.IgnoreCase);
    }

}
