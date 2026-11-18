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

    /// <summary>
    /// 返回「规则相位网格」上严格晚于 notBefore 的第一次触发时刻。
    /// 相位由 anchor 决定（Interval 的起算点；Daily/Weekly/Monthly/YearMonths 的 wall-clock 时刻与日/月相位），
    /// 因此稍后、免打扰、休眠都不会造成漂移；结果恒 &gt; notBefore，杜绝连环补弹。
    /// 返回 null 表示这条链按 Until/MaxCount 已走完。
    /// </summary>
    public static DateTime? NextAfter(RepeatRule rule, DateTime anchor, DateTime notBefore)
    {
        if (rule == null || rule.Type == RepeatType.None) return null;
        if (rule.MaxCount.HasValue && rule.DoneCount >= rule.MaxCount.Value) return null;

        // 快进游标：尽量靠近 notBefore，但「相位」完全取自 anchor，不改变规则语义。
        var cursor = CoarseCursor(rule, anchor, notBefore);
        if (cursor < anchor) cursor = anchor;

        // 最多 96 步：YearMonths 内部已最多 48 个月，留足余量；正常 1~3 步即命中。
        for (var i = 0; i < 96; i++)
        {
            var next = NextAfter(rule, cursor);          // 复用现有单步实现，规则逻辑零重复
            if (next == null) return null;               // Until 越界 / 次数耗尽 / 无匹配
            if (next.Value > notBefore) return next.Value;
            cursor = next.Value;                         // 沿同一相位网格继续推进
        }
        Log.Warn("NextAfter 快进达到步数上限，规则=" + rule.Type);
        return null;                                     // 防御性兜底，不可达
    }

    private static DateTime CoarseCursor(RepeatRule rule, DateTime anchor, DateTime notBefore)
    {
        if (notBefore <= anchor) return anchor;
        return rule.Type switch
        {
            RepeatType.Interval => FastForwardInterval(anchor, rule, notBefore),
            RepeatType.Daily => notBefore.Date.AddDays(-1) + anchor.TimeOfDay,
            RepeatType.Weekly => notBefore.Date.AddDays(-7) + anchor.TimeOfDay,
            RepeatType.Monthly => new DateTime(notBefore.Year, notBefore.Month, 1,
                                                anchor.Hour, anchor.Minute, 0).AddMonths(-2),
            RepeatType.YearMonths => new DateTime(notBefore.Year - 2, 1, 1,
                                                anchor.Hour, anchor.Minute, 0),
            _ => anchor
        };
    }

    private static DateTime FastForwardInterval(DateTime anchor, RepeatRule rule, DateTime notBefore)
    {
        var value = Math.Max(1, rule.IntervalValue);
        var stepTicks = rule.IntervalUnit switch
        {
            IntervalUnit.Minute => TimeSpan.FromMinutes(value).Ticks,
            IntervalUnit.Hour => TimeSpan.FromHours(value).Ticks,
            _ => TimeSpan.FromDays(value).Ticks
        };
        var elapsed = (notBefore - anchor).Ticks;
        var steps = Math.Max(0L, elapsed / stepTicks - 1);          // 留 1 步余量给循环
        var maxSteps = (DateTime.MaxValue.Ticks - anchor.Ticks) / stepTicks;
        if (steps >= maxSteps) return DateTime.MaxValue.AddTicks(-stepTicks); // 触发循环内 NextAfter 返回 null
        return anchor.AddTicks(steps * stepTicks);
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
        rule = Sanitize(rule);
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
        rule = Sanitize(rule);
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

    /// <summary>返回规则的安全版本：null 规则→空规则；集合字段非 null 且过滤非法元素。幂等。</summary>
    /// <remarks>
    /// 只做不影响校验语义的归一化：集合初始化/元素过滤、DoneCount 非负。
    /// IntervalValue / DayOfMonth / MaxCount 的合法性仍交给 Validate 判断（单步引擎内已各自 clamp/兜底），
    /// 避免在此处“自动修正”导致 UI 输入错误被静默吞掉。
    /// </remarks>
    public static RepeatRule Sanitize(RepeatRule? rule)
    {
        rule ??= new RepeatRule();
        rule.Weekdays ??= new List<int>();
        rule.Months ??= new List<int>();
        rule.Weekdays = rule.Weekdays.Where(d => d is >= 0 and <= 6).Distinct().ToList();
        rule.Months = rule.Months.Where(m => m is >= 1 and <= 12).Distinct().ToList();
        if (rule.DoneCount < 0) rule.DoneCount = 0;
        return rule;
    }
}
