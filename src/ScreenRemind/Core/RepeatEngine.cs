namespace ScreenRemind.Core;

/// <summary>
/// 重复规则引擎：纯函数，零 UI 依赖，也不碰文件，便于单独自检。
/// 约定：anchor 是「上一次触发时刻」，返回严格晚于 anchor 的下一次触发时刻；返回 null 表示这条链走完。
/// 已刻意处理的边界：月末不足取当月最后一天、闰年 2 月、跨年回绕、次数/截止日终止。
/// </summary>
public static class RepeatEngine
{
    private static readonly string[] WeekdayNames = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

    public static DateTime? NextAfter(RepeatRule rule, DateTime anchor)
    {
        if (rule == null) return null;

        // 次数已用满 → 结束。DoneCount 在「本次触发」时递增，所以这里判断的是「还能不能再有一次」
        if (rule.MaxCount.HasValue && rule.DoneCount >= rule.MaxCount.Value) return null;

        DateTime? next = rule.Type switch
        {
            RepeatType.None => null,
            RepeatType.Interval => AddInterval(anchor, rule),
            RepeatType.Daily => anchor.AddDays(1),
            RepeatType.Weekly => NextWeekday(anchor, rule.Weekdays),
            RepeatType.Monthly => NextMonthDay(anchor, rule.DayOfMonth),
            RepeatType.YearMonths => NextYearMonthDay(anchor, rule.Months, rule.DayOfMonth),
            _ => null
        };

        if (next == null) return null;

        if (rule.Until.HasValue && next.Value.Date > rule.Until.Value.Date) return null;

        return next;
    }

    private static DateTime? AddInterval(DateTime anchor, RepeatRule rule)
    {
        var value = Math.Max(1, rule.IntervalValue);
        try
        {
            return rule.IntervalUnit switch
            {
                IntervalUnit.Minute => anchor.AddMinutes(value),
                IntervalUnit.Hour => anchor.AddHours(value),
                IntervalUnit.Day => anchor.AddDays(value),
                _ => null
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            // 值大到超出 DateTime 范围，视为不再有下一次
            return null;
        }
    }

    private static DateTime? NextWeekday(DateTime anchor, List<int> weekdays)
    {
        if (weekdays == null || weekdays.Count == 0) return null;
        var set = weekdays.Where(d => d is >= 0 and <= 6).ToHashSet();
        if (set.Count == 0) return null;

        // 最多往后找 7 天一定命中
        for (var i = 1; i <= 7; i++)
        {
            var date = anchor.Date.AddDays(i);
            if (set.Contains((int)date.DayOfWeek))
            {
                return date + anchor.TimeOfDay;
            }
        }
        return null;
    }

    private static DateTime? NextMonthDay(DateTime anchor, int dayOfMonth)
    {
        var day = Math.Clamp(dayOfMonth, 1, 31);
        var year = anchor.Year;
        var month = anchor.Month + 1;
        if (month > 12)
        {
            month = 1;
            year++;
        }
        // 关键边界：31 号在小月自动落到 30 号，2 月落到 28/29 号
        var safeDay = Math.Min(day, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, safeDay, anchor.Hour, anchor.Minute, 0);
    }

    private static DateTime? NextYearMonthDay(DateTime anchor, List<int> months, int dayOfMonth)
    {
        if (months == null || months.Count == 0) return null;
        var set = months.Where(m => m is >= 1 and <= 12).ToHashSet();
        if (set.Count == 0) return null;

        var day = Math.Clamp(dayOfMonth, 1, 31);
        var year = anchor.Year;
        var month = anchor.Month;

        // 一个月一个月往后走，最多 4 年一定命中（勾选月份非空时 12 个月内必命中）
        for (var i = 0; i < 48; i++)
        {
            month++;
            if (month > 12)
            {
                month = 1;
                year++;
            }
            if (!set.Contains(month)) continue;

            var safeDay = Math.Min(day, DateTime.DaysInMonth(year, month));
            return new DateTime(year, month, safeDay, anchor.Hour, anchor.Minute, 0);
        }
        return null;
    }

    /// <summary>把规则描述成一句中文，用于列表与弹窗展示。</summary>
    public static string Describe(RepeatRule rule)
    {
        if (rule == null) return "不重复";

        var time = string.Empty;
        var body = rule.Type switch
        {
            RepeatType.None => "不重复",
            RepeatType.Interval => rule.IntervalUnit switch
            {
                IntervalUnit.Minute => $"每隔 {rule.IntervalValue} 分钟",
                IntervalUnit.Hour => $"每隔 {rule.IntervalValue} 小时",
                _ => $"每隔 {rule.IntervalValue} 天"
            },
            RepeatType.Daily => "每天",
            RepeatType.Weekly => rule.Weekdays.Count == 0
                ? "每周（未选星期）"
                : "每周 " + string.Join("、", rule.Weekdays.OrderBy(d => d).Select(d => WeekdayNames[Math.Clamp(d, 0, 6)].Replace("周", ""))),
            RepeatType.Monthly => $"每月 {rule.DayOfMonth} 日",
            RepeatType.YearMonths => rule.Months.Count == 0
                ? "每年（未选月份）"
                : "每年 " + string.Join("、", rule.Months.OrderBy(m => m)) + $" 月的 {rule.DayOfMonth} 日",
            _ => "不重复"
        };

        if (rule.Type is RepeatType.Daily or RepeatType.Weekly or RepeatType.Monthly or RepeatType.YearMonths)
        {
            time = "（按本次设定时刻）";
        }

        var tail = string.Empty;
        if (rule.Until.HasValue) tail += $"，至 {rule.Until.Value:yyyy-MM-dd}";
        if (rule.MaxCount.HasValue) tail += $"，共 {rule.MaxCount.Value} 次";
        if (rule.MaxCount.HasValue && rule.DoneCount > 0) tail += $"（已完成 {rule.DoneCount}）";

        return body + time + tail;
    }

    /// <summary>校验规则是否可用，返回 null 表示通过。</summary>
    public static string? Validate(RepeatRule rule)
    {
        if (rule == null) return "规则为空";
        switch (rule.Type)
        {
            case RepeatType.Weekly when rule.Weekdays.Count == 0:
                return "请至少勾选一个星期几";
            case RepeatType.YearMonths when rule.Months.Count == 0:
                return "请至少勾选一个月份";
            case RepeatType.Monthly or RepeatType.YearMonths when rule.DayOfMonth is < 1 or > 31:
                return "日期需在 1–31 之间";
            case RepeatType.Interval when rule.IntervalValue < 1:
                return "间隔必须大于 0";
        }
        if (rule.MaxCount is <= 0) return "总次数必须大于 0";
        return null;
    }
}
