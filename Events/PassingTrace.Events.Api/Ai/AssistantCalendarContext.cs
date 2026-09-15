using System.Globalization;
using System.Text.RegularExpressions;

namespace PassingTrace.Events.Api.Ai;

/// <summary>
/// A request-scoped, server-clock calendar. Device time zones affect calendar boundaries,
/// never the authoritative current instant; missing device settings use the product default.
/// </summary>
public sealed partial class AssistantCalendarContext
{
    public const string DefaultTimezone = "Asia/Shanghai";

    private AssistantCalendarContext(DateTimeOffset utcNow, TimeZoneInfo timezone, bool usedDefaultTimezone)
    {
        LocalNow = TimeZoneInfo.ConvertTime(utcNow, timezone);
        Timezone = timezone.Id;
        UsedDefaultTimezone = usedDefaultTimezone;
        var firstDay = new DateTime(LocalNow.Year, LocalNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var start = StartOfDay(firstDay, timezone);
        // PostgreSQL timestamps have microsecond precision; do not round into the next month.
        var end = TimeZoneInfo.ConvertTime(StartOfDay(firstDay.AddMonths(1), timezone).AddTicks(-10), timezone);
        CurrentMonth = new AssistantCalendarRange(start, end);
    }

    public DateTimeOffset LocalNow { get; }
    public string Timezone { get; }
    public bool UsedDefaultTimezone { get; }
    public AssistantCalendarRange CurrentMonth { get; }

    // Invalidate relative-date answers at the user's midnight, including across year/month changes.
    public string CacheValue => FormattableString.Invariant($"calendar-v1:{Timezone}:{LocalNow:yyyy-MM-dd}");

    public string Instructions => FormattableString.Invariant($"""
        本轮的权威当前时间由服务器时钟提供：{LocalNow:yyyy-MM-dd HH:mm:ss zzz}；用户日历时区：{Timezone}。
        {(UsedDefaultTimezone ? "客户端尚未提供有效时区，本轮按北京时间理解相对日期；如时区会影响结论，说明这一假设。" : "客户端提供了有效时区，按该时区解释相对日期。")}
        今天是 {LocalNow:yyyy-MM-dd}；本月/这个月/this month 指 {LocalNow:yyyy年MM月}，不是模型训练日期，也不是历史回答中猜测的月份。
        本月工具查询的起点为 {CurrentMonth.FromText}，终点为 {CurrentMonth.ToText}（含终点）；今天、昨天、今年等也必须从此当前时间推算。
        当前时间优先于历史会话中的过时或错误日期；用户明确给出的历史日期或此前确认的历史范围仍须保留，不得擅自替换为本月。
        查询和统计后按工具返回的实际时间范围作答；无可核实的金额数据只表示无法确认消费合计，不能解释为没有消费或消费为零。
        """);

    public static AssistantCalendarContext Create(DateTimeOffset utcNow, string? requestedTimezone = null)
    {
        TimeZoneInfo? timezone = null;
        if (!string.IsNullOrWhiteSpace(requestedTimezone) && requestedTimezone.Length <= 128)
        {
            try { timezone = TimeZoneInfo.FindSystemTimeZoneById(requestedTimezone.Trim()); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        var usedDefault = timezone is null;
        timezone ??= TimeZoneInfo.FindSystemTimeZoneById(DefaultTimezone);
        return new AssistantCalendarContext(utcNow, timezone, usedDefault);
    }

    /// <summary>
    /// Only fix an unambiguous explicit current-month request. Mixed periods, exact dates and
    /// references back to a historical scope remain under the conversation's explicit scope.
    /// </summary>
    public AssistantCalendarRange? ResolveCurrentMonth(string question)
    {
        if (!CurrentMonthPhrase().IsMatch(question) || ExplicitOrHistoricalPeriod().IsMatch(question)) return null;
        return CurrentMonth;
    }

    private static DateTimeOffset StartOfDay(DateTime date, TimeZoneInfo timezone)
    {
        // Some zones advance their clocks at midnight. Use the first real instant of that day.
        while (timezone.IsInvalidTime(date)) date = date.AddMinutes(1);
        var offset = timezone.IsAmbiguousTime(date)
            ? timezone.GetAmbiguousTimeOffsets(date).Max()
            : timezone.GetUtcOffset(date);
        return new DateTimeOffset(date, offset);
    }

    [GeneratedRegex(@"这个月|本月|这月|\bthis\s+month\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrentMonthPhrase();

    [GeneratedRegex(@"\d{4}|\d{1,2}\s*(?:月|日|号|[-/])|[一二三四五六七八九十]+月|去年|前年|明年|上个月|上月|下个月|下月|今天|昨天|本周|这周|上周|刚才|之前|前面|此前|上次|那个月|那个范围|截至|截止|不含|除了|除去|以后|之前|比较|对比|上旬|中旬|下旬|(?:前|后|近)[一二三四五六七八九十\d]+天|\b(?:january|february|march|april|may|june|july|august|september|october|november|december|last\s+month|next\s+month|today|yesterday|previous|earlier|compare|until|before|after|except|first\s+\d+\s+days|last\s+\d+\s+days)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitOrHistoricalPeriod();
}

public sealed record AssistantCalendarRange(DateTimeOffset From, DateTimeOffset To)
{
    public string FromText => From.ToString("O", CultureInfo.InvariantCulture);
    public string ToText => To.ToString("O", CultureInfo.InvariantCulture);
}
