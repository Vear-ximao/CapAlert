using ScreenRemind.UI;

namespace ScreenRemind.Core;

/// <summary>
/// 出口纯函数集：所有弹窗出口的「字段变更 + 下一次计算」都在这里完成。
/// AppController 只负责「取最新对象 → 调本类纯函数 → 落盘 → 通知」，不直接写业务字段。
///
/// 语义与不变量：
/// - INV-1：一次性顺延（Snooze / 免打扰）只写 SnoozeUntil / DeferKind，绝不写 PlanAnchor(TriggerAt)。
/// - INV-2：Acknowledge / SkipNext 返回的 next 由三参数 NextAfter 计算，保证严格 > notBefore。
/// - INV-3：DoneCount 只在真实出口动作（Acknowledge / Renew / SkipNext）时 +1；Snooze 与错过次数不计。
/// - 本类纯逻辑、不碰 UI、不直接落盘；返回计算值供 AppController 消费。
/// </summary>
public static class RepeatFlow
{
    /// <summary>
    /// 已知晓：递增 DoneCount、结束当前任务、沿计划锚点算出未来下一次。
    /// 返回应写入 follow.TriggerAt 的新计划锚点；null 表示规则已走完。
    /// </summary>
    public static DateTime? Acknowledge(RemindTask task, DateTime now)
    {
        var rule = task.Repeat.Clone();
        rule.DoneCount += 1;                     // INV-3：真实确认动作才计数

        task.State = TaskState.Done;
        task.AckAt = now;
        task.SnoozeUntil = null;
        task.DeferKind = DeferKind.None;
        task.LastFiredAt = now;
        task.Repeat = rule;

        // 三参数重载：从计划锚点出发跳过所有错过的网格点，结果严格 > now（INV-1/INV-2）
        return RepeatEngine.NextAfter(rule, task.PlanAnchor, now);
    }

    /// <summary>
    /// 稍后提醒：只写一次性生效时刻与来源，不动计划锚点与 DoneCount。
    /// minutes 被钳制到 1..10080（7 天）；kind 为 None 时按 UserSnooze 处理。
    /// </summary>
    public static void Snooze(RemindTask task, DateTime now, int minutes, DeferKind kind)
    {
        task.State = TaskState.Snoozed;
        task.SnoozeUntil = now.AddMinutes(Math.Clamp(minutes, 1, 10080));  // 唯一写入点
        task.DeferKind = kind == DeferKind.None ? DeferKind.UserSnooze : kind;
        task.PreNotifiedAt = null;                // C-12：稍后后允许重新预提醒
        // 【禁止】task.TriggerAt = ...；【禁止】task.Repeat.DoneCount++（INV-1/INV-3）
    }

    /// <summary>
    /// 跳过下一次：沿计划锚点前进一个网格点，并计入 DoneCount。
    /// notBefore 传 PlanAnchor（语义是“跳过锚点上这一次”，与 Acknowledge 的“补弹到 now 之后”不同）。
    /// 返回新的计划锚点；null 表示规则已走完（此时不修改 task）。
    /// now 保留在签名中供调用方统一上下文，推进网格本身只依赖计划锚点。
    /// </summary>
    public static DateTime? SkipNext(RemindTask task, DateTime now)
    {
        var rule = task.Repeat.Clone();
        rule.DoneCount += 1;                     // INV-3：与 Acknowledge 共用计数语义
        var next = RepeatEngine.NextAfter(rule, task.PlanAnchor, task.PlanAnchor);
        if (next == null) return null;

        task.Repeat = rule;
        task.TriggerAt = next.Value;             // 推进重复链：唯一合法写入点之一
        task.SnoozeUntil = null;
        task.DeferKind = DeferKind.None;
        task.PreNotifiedAt = null;
        return next;
    }

    /// <summary>
    /// 设置下一次（Renew 确认）：沿用新规则并计入 DoneCount，结束当前任务。
    /// 返回应写入 follow.TriggerAt 的新计划锚点（用户选定时刻，保证 > now）。
    /// </summary>
    public static DateTime Renew(RemindTask task, RepeatRule newRule, DateTime chosenAt, DateTime now)
    {
        var rule = RepeatEngine.Sanitize(newRule).Clone();
        rule.DoneCount = task.Repeat.DoneCount + 1;  // C-1 次生问题：Renew 确认也必须 +1

        task.State = TaskState.Done;
        task.AckAt = now;
        task.SnoozeUntil = null;
        task.DeferKind = DeferKind.None;
        task.LastFiredAt = now;
        task.Repeat = rule;

        // 用户手选时刻由 UI 校验 > now；这里兜底防止脏数据产生过去锚点（INV-2）
        return chosenAt > now
            ? chosenAt
            : RepeatEngine.NextAfter(rule, task.PlanAnchor, now) ?? now.AddMinutes(1);
    }

    /// <summary>弹窗出口路由：唯一的 Action 判定入口（C-1）。默认（关闭弹窗）= 稍后。</summary>
    public static ReminderAction ResolveAction(ReminderAction action, bool renewRequested)
    {
        if (action != ReminderAction.None) return action;
        if (renewRequested) return ReminderAction.Renew;
        return ReminderAction.Snoozed;
    }
}
