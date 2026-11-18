using System.Text.RegularExpressions;
using ScreenRemind.Core;

namespace SelfTest;

internal static class RepeatEngineTests
{
    public static void Run()
    {
        // ---- 每月固定某日：月末不足要落到当月最后一天 ----
        var monthly31 = new RepeatRule { Type = RepeatType.Monthly, DayOfMonth = 31 };
        Program.Equal("每月31号：2026-01-31 → 2026-02-28（平年）",
            new DateTime(2026, 2, 28, 9, 0, 0),
            RepeatEngine.NextAfter(monthly31, new DateTime(2026, 1, 31, 9, 0, 0)));
        Program.Equal("每月31号：2024-01-31 → 2024-02-29（闰年）",
            new DateTime(2024, 2, 29, 9, 0, 0),
            RepeatEngine.NextAfter(monthly31, new DateTime(2024, 1, 31, 9, 0, 0)));
        Program.Equal("每月31号：2026-03-31 → 2026-04-30（小月）",
            new DateTime(2026, 4, 30, 9, 0, 0),
            RepeatEngine.NextAfter(monthly31, new DateTime(2026, 3, 31, 9, 0, 0)));
        Program.Equal("每月31号：2026-12-31 → 2027-01-31（跨年）",
            new DateTime(2027, 1, 31, 9, 0, 0),
            RepeatEngine.NextAfter(monthly31, new DateTime(2026, 12, 31, 9, 0, 0)));

        var monthly15 = new RepeatRule { Type = RepeatType.Monthly, DayOfMonth = 15 };
        Program.Equal("每月15号：2026-01-15 → 2026-02-15",
            new DateTime(2026, 2, 15, 8, 30, 0),
            RepeatEngine.NextAfter(monthly15, new DateTime(2026, 1, 15, 8, 30, 0)));

        // ---- 每周多选 ----
        var weekly135 = new RepeatRule { Type = RepeatType.Weekly, Weekdays = new List<int> { 1, 3, 5 } };
        var nextFriday = RepeatEngine.NextAfter(weekly135, new DateTime(2026, 11, 13, 10, 0, 0));
        Program.Equal("每周一三五：2026-11-13(周五) → 2026-11-16(周一)",
            new DateTime(2026, 11, 16, 10, 0, 0), nextFriday);
        Program.Check("每周一三五：结果确实是周一", nextFriday?.DayOfWeek == DayOfWeek.Monday);

        var weeklySunday = new RepeatRule { Type = RepeatType.Weekly, Weekdays = new List<int> { 0 } };
        Program.Equal("每周日：2026-11-13(周五) → 2026-11-15",
            new DateTime(2026, 11, 15, 10, 0, 0),
            RepeatEngine.NextAfter(weeklySunday, new DateTime(2026, 11, 13, 10, 0, 0)));

        var weeklyEmpty = new RepeatRule { Type = RepeatType.Weekly, Weekdays = new List<int>() };
        Program.Equal("每周但没勾星期 → 无下一次", null, RepeatEngine.NextAfter(weeklyEmpty, DateTime.Now));
        Program.Check("每周但没勾星期 → 校验报错", RepeatEngine.Validate(weeklyEmpty) != null);

        // ---- 每天 / 周期 ----
        var daily = new RepeatRule { Type = RepeatType.Daily };
        Program.Equal("每天：2026-11-16 09:30 → 2026-11-17 09:30",
            new DateTime(2026, 11, 17, 9, 30, 0),
            RepeatEngine.NextAfter(daily, new DateTime(2026, 11, 16, 9, 30, 0)));

        var interval90 = new RepeatRule { Type = RepeatType.Interval, IntervalValue = 90, IntervalUnit = IntervalUnit.Minute };
        Program.Equal("每隔90分钟：23:30 → 次日 01:00",
            new DateTime(2026, 11, 17, 1, 0, 0),
            RepeatEngine.NextAfter(interval90, new DateTime(2026, 11, 16, 23, 30, 0)));

        // ---- 指定月份 ----
        var yearMonths = new RepeatRule
        {
            Type = RepeatType.YearMonths,
            Months = new List<int> { 3, 6, 9, 12 },
            DayOfMonth = 15
        };
        Program.Equal("每年3/6/9/12月15日：2026-11-16 → 2026-12-15",
            new DateTime(2026, 12, 15, 14, 0, 0),
            RepeatEngine.NextAfter(yearMonths, new DateTime(2026, 11, 16, 14, 0, 0)));
        Program.Equal("每年3/6/9/12月15日：2026-12-15 → 2027-03-15（跨年回绕）",
            new DateTime(2027, 3, 15, 14, 0, 0),
            RepeatEngine.NextAfter(yearMonths, new DateTime(2026, 12, 15, 14, 0, 0)));

        var yearMarch31 = new RepeatRule { Type = RepeatType.YearMonths, Months = new List<int> { 3 }, DayOfMonth = 31 };
        Program.Equal("每年3月31日：2026-04-01 → 2027-03-31",
            new DateTime(2027, 3, 31, 7, 0, 0),
            RepeatEngine.NextAfter(yearMarch31, new DateTime(2026, 4, 1, 7, 0, 0)));

        var yearEmpty = new RepeatRule { Type = RepeatType.YearMonths, Months = new List<int>(), DayOfMonth = 1 };
        Program.Equal("指定月份但没勾月份 → 无下一次", null, RepeatEngine.NextAfter(yearEmpty, DateTime.Now));
        Program.Check("指定月份但没勾月份 → 校验报错", RepeatEngine.Validate(yearEmpty) != null);

        // ---- 结束条件 ----
        var untilRule = new RepeatRule { Type = RepeatType.Daily, Until = new DateTime(2026, 11, 18) };
        Program.Equal("截止日当天之后 → 结束",
            null, RepeatEngine.NextAfter(untilRule, new DateTime(2026, 11, 18, 9, 0, 0)));
        Program.Equal("截止日前一天 → 还有下一次",
            new DateTime(2026, 11, 18, 9, 0, 0),
            RepeatEngine.NextAfter(untilRule, new DateTime(2026, 11, 17, 9, 0, 0)));

        var countRule = new RepeatRule { Type = RepeatType.Daily, MaxCount = 2, DoneCount = 1 };
        Program.Check("总次数2次、已1次 → 还有下一次", RepeatEngine.NextAfter(countRule, new DateTime(2026, 11, 16, 9, 0, 0)) != null);
        countRule.DoneCount = 2;
        Program.Equal("总次数2次、已2次 → 结束", null, RepeatEngine.NextAfter(countRule, new DateTime(2026, 11, 16, 9, 0, 0)));

        var noneRule = new RepeatRule { Type = RepeatType.None };
        Program.Equal("不重复 → 无下一次", null, RepeatEngine.NextAfter(noneRule, DateTime.Now));

        // ---- 描述文本 ----
        Program.Check("描述包含「每月 31 日」", RepeatEngine.Describe(monthly31).Contains("每月 31 日"),
            RepeatEngine.Describe(monthly31));
        Program.Check("描述包含「每年」月份", RepeatEngine.Describe(yearMonths).Contains("每年"),
            RepeatEngine.Describe(yearMonths));

        // ---- 严格递增：下一次必须晚于上一次 ----
        var anchors = new[]
        {
            new DateTime(2026, 1, 31, 0, 0, 0), new DateTime(2026, 2, 28, 23, 59, 0),
            new DateTime(2026, 12, 31, 12, 0, 0), new DateTime(2024, 2, 29, 6, 0, 0)
        };
        var ok = true;
        foreach (var anchor in anchors)
        {
            var next = RepeatEngine.NextAfter(monthly31, anchor);
            if (next == null || next <= anchor) ok = false;
        }
        Program.Check("每月规则在月末节点始终严格递增", ok);
    }
}

internal static class StorageTests
{
    public static void Run()
    {
        var path = Path.Combine(AppPaths.Data, "unit_tasks.json");
        Directory.CreateDirectory(AppPaths.Data);

        var file = new TasksFile();
        file.Tasks.Add(new RemindTask { Id = "t1", Image = "shots/x.png", TriggerAt = new DateTime(2026, 11, 16, 12, 0, 0) });
        Program.Check("首次写入成功", JsonStore.Save(path, file));
        Program.Check("首次写入不产生 .bak", !File.Exists(path + ".bak"));

        var loaded = JsonStore.Load(path, () => new TasksFile());
        Program.Equal("读回任务数", 1, loaded.Tasks.Count);
        Program.Equal("读回任务 ID", "t1", loaded.Tasks[0].Id);

        // 第二次写入会生成 .bak（File.Replace 的行为）
        file.Tasks.Add(new RemindTask { Id = "t2", Image = "shots/y.png", TriggerAt = new DateTime(2026, 11, 17, 12, 0, 0) });
        JsonStore.Save(path, file);
        Program.Check("第二次写入生成 .bak", File.Exists(path + ".bak"));

        var reloaded = JsonStore.Load(path, () => new TasksFile());
        Program.Equal("读回任务数（第二次）", 2, reloaded.Tasks.Count);

        // 故意写坏主文件 → 应从 .bak 恢复
        File.WriteAllText(path, "{ 这不是合法 JSON ");
        var recovered = JsonStore.Load(path, () => new TasksFile());
        Program.Check("主文件损坏时自动恢复（不少于 1 条）", recovered.Tasks.Count >= 1,
            $"实际 {recovered.Tasks.Count} 条");
        Program.Check("损坏文件被隔离留证",
            Directory.GetFiles(AppPaths.Data, "unit_tasks.json.corrupt_*").Length == 1);

        // 主文件与备份都坏 → 返回空数据而不是抛异常
        File.WriteAllText(path, "@@@");
        File.WriteAllText(path + ".bak", "###");
        var fallback = JsonStore.Load(path, () => new TasksFile());
        Program.Equal("双损坏时降级为空数据", 0, fallback.Tasks.Count);

        // 任务仓库：新增后重新加载仍在
        var repo = new TaskRepository();
        var task = new RemindTask
        {
            Id = repo.NewTaskId(),
            Image = "shots/2026-11/test.png",
            TriggerAt = DateTime.Now.AddHours(1),
            Repeat = new RepeatRule { Type = RepeatType.Daily }
        };
        repo.Add(task);
        var repo2 = new TaskRepository();
        Program.Check("任务落盘后可被重新加载", repo2.All().Any(t => t.Id == task.Id));

        repo2.AppendHistory(new HistoryEntry { TaskId = task.Id, Action = "create", Note = "测试" });
        Program.Check("历史可追加并读回", repo2.ReadHistory(1).Any(h => h.TaskId == task.Id));
    }
}

internal static class PathTests
{
    public static void Run()
    {
        var sandbox = Environment.GetEnvironmentVariable("SCREENREMIND_HOME")!;
        Program.Equal("根目录跟随 SCREENREMIND_HOME",
            Path.GetFullPath(sandbox).ToLowerInvariant(), AppPaths.Root.ToLowerInvariant());

        var now = new DateTime(2026, 11, 16, 11, 26, 43);
        var abs = AppPaths.NewShotAbsolutePath(now, out var rel);

        Program.Check("截图文件名符合 YYYYMMDD_HHMMSS_序号.png",
            Regex.IsMatch(Path.GetFileName(abs), @"^\d{8}_\d{6}_\d{3}\.png$"), Path.GetFileName(abs));
        Program.Check("截图按年月分目录", rel.StartsWith("shots/2026-11/"), rel);
        Program.Check("截图目录已真实创建", Directory.Exists(Path.GetDirectoryName(abs)));

        // 同秒第二张序号递增
        File.WriteAllText(abs, "x");
        AppPaths.NewShotAbsolutePath(now, out var rel2);
        Program.Check("同秒多张自动递增序号", rel2.EndsWith("_002.png"), rel2);
        File.Delete(abs);

        Program.Equal("相对路径与绝对路径可互转", abs, AppPaths.Resolve(AppPaths.ToRelative(abs)));
    }
}

internal static class QuietTests
{
    public static void Run()
    {
        var cfg = new AppConfig { QuietEnabled = true, QuietStart = "23:00", QuietEnd = "08:00" };

        Program.Check("跨零点：23:30 处于免打扰", Scheduler.IsInQuiet(new DateTime(2026, 11, 16, 23, 30, 0), cfg));
        Program.Check("跨零点：07:59 处于免打扰", Scheduler.IsInQuiet(new DateTime(2026, 11, 17, 7, 59, 0), cfg));
        Program.Check("跨零点：12:00 不在免打扰", !Scheduler.IsInQuiet(new DateTime(2026, 11, 16, 12, 0, 0), cfg));
        Program.Check("跨零点：22:59 不在免打扰", !Scheduler.IsInQuiet(new DateTime(2026, 11, 16, 22, 59, 0), cfg));

        Program.Equal("23:30 的免打扰结束时刻 = 次日 08:00",
            new DateTime(2026, 11, 17, 8, 0, 0),
            Scheduler.QuietEnd(new DateTime(2026, 11, 16, 23, 30, 0), cfg));
        Program.Equal("07:00 的免打扰结束时刻 = 当天 08:00",
            new DateTime(2026, 11, 17, 8, 0, 0),
            Scheduler.QuietEnd(new DateTime(2026, 11, 17, 7, 0, 0), cfg));

        var sameDay = new AppConfig { QuietEnabled = true, QuietStart = "12:00", QuietEnd = "13:30" };
        Program.Check("同日时段：12:30 命中", Scheduler.IsInQuiet(new DateTime(2026, 11, 16, 12, 30, 0), sameDay));
        Program.Check("同日时段：13:31 不命中", !Scheduler.IsInQuiet(new DateTime(2026, 11, 16, 13, 31, 0), sameDay));

        cfg.QuietEnabled = false;
        Program.Check("关闭免打扰后不拦截", !Scheduler.IsInQuiet(new DateTime(2026, 11, 16, 23, 30, 0), cfg));

        Program.Check("非法时间格式不崩溃且视为不在免打扰",
            !Scheduler.IsInQuiet(DateTime.Now, new AppConfig { QuietEnabled = true, QuietStart = "abc", QuietEnd = "xyz" }));
    }
}

internal static class HotkeyTests
{
    public static void Run()
    {
        // 旧配置（只有 Modifiers + Key）要能还原成完整组合
        var legacy = new HotkeySetting { Modifiers = NativeHotkey.ModControl | NativeHotkey.ModAlt, Key = (uint)System.Windows.Forms.Keys.S };
        var keys = legacy.EffectiveKeys();
        Program.Equal("旧配置还原成 3 个键", 3, keys.Count);
        Program.Check("旧配置含 Ctrl", keys.Contains(HotkeyKeys.Control));
        Program.Check("旧配置含 Alt", keys.Contains(HotkeyKeys.Alt));
        Program.Check("旧配置含 S", keys.Contains((uint)System.Windows.Forms.Keys.S));

        // 旧「任意两键」配置
        var legacyRaw = new HotkeySetting { UseRawCombo = true, RawKey1 = (uint)System.Windows.Forms.Keys.A, RawKey2 = (uint)System.Windows.Forms.Keys.S };
        var rawKeys = legacyRaw.EffectiveKeys();
        Program.Equal("旧任意两键配置还原成 2 个键", 2, rawKeys.Count);

        // 新配置：纯修饰键组合 Ctrl+Alt
        var combo = new HotkeySetting { Keys = new List<uint> { HotkeyKeys.Control, HotkeyKeys.Alt } };
        Program.Equal("纯修饰键组合 2 个键", 2, combo.EffectiveKeys().Count);
        combo.SyncLegacyFields();
        Program.Check("纯修饰键组合需要钩子（UseRawCombo=true）", combo.UseRawCombo);

        // 新配置：Ctrl+Alt+S → 系统注册
        var normal = new HotkeySetting { Keys = new List<uint> { HotkeyKeys.Control, HotkeyKeys.Alt, (uint)System.Windows.Forms.Keys.S } };
        normal.SyncLegacyFields();
        Program.Check("Ctrl+Alt+S 不需要钩子", !normal.UseRawCombo);
        Program.Equal("Ctrl+Alt+S 的注册修饰位", NativeHotkey.ModControl | NativeHotkey.ModAlt, normal.Modifiers);
        Program.Equal("Ctrl+Alt+S 的主键", (uint)System.Windows.Forms.Keys.S, normal.Key);
        Program.Check("同步后仍能还原成 3 个键", normal.EffectiveKeys().Count == 3);

        // 新配置：A+S → 钩子
        var twoPlain = new HotkeySetting { Keys = new List<uint> { (uint)System.Windows.Forms.Keys.A, (uint)System.Windows.Forms.Keys.S } };
        twoPlain.SyncLegacyFields();
        Program.Check("A+S 需要钩子", twoPlain.UseRawCombo);
        Program.Equal("A+S 记录到旧字段 RawKey1", (uint)System.Windows.Forms.Keys.A, twoPlain.RawKey1);

        // 左右修饰键归一化：左Ctrl+右Alt 不应变成 4 个键
        var mixed = new HotkeySetting { Keys = new List<uint> { 0xA2, 0xA5, 0xA3, 0xA4 } };
        Program.Equal("左右 Ctrl/Alt 归一化成 2 个键", 2, mixed.EffectiveKeys().Count);

        // 去重
        var dup = new HotkeySetting { Keys = new List<uint> { HotkeyKeys.Control, HotkeyKeys.Control, HotkeyKeys.Alt } };
        Program.Equal("重复按键去重", 2, dup.EffectiveKeys().Count);

        // 描述文本
        Program.Equal("描述为 Ctrl+Alt+S", "Ctrl+Alt+S", HotkeyTextFactory.Describe(normal));
        Program.Equal("纯修饰键描述为 Ctrl+Alt", "Ctrl+Alt", HotkeyTextFactory.Describe(combo));
    }
}

/// <summary>HotkeyText 在 Platform 里，测试工程只编译 Core，这里用同样的规则做等价校验。</summary>
internal static class HotkeyTextFactory
{
    public static string Describe(HotkeySetting setting) =>
        string.Join("+", setting.EffectiveKeys().Select(Name));

    private static string Name(uint vk) => vk switch
    {
        HotkeyKeys.Control => "Ctrl",
        HotkeyKeys.Alt => "Alt",
        HotkeyKeys.Shift => "Shift",
        HotkeyKeys.Win => "Win",
        _ => ((char)vk).ToString()
    };
}

internal static class PasswordTests{
    public static void Run()
    {
        var (hash, salt) = SecurityService.Create("my-pass-123");
        Program.Check("正确密码通过", SecurityService.Verify("my-pass-123", hash, salt));
        Program.Check("错误密码拒绝", !SecurityService.Verify("my-pass-124", hash, salt));
        Program.Check("空密码拒绝", !SecurityService.Verify("", hash, salt));

        var (hash2, salt2) = SecurityService.Create("my-pass-123");
        Program.Check("相同密码两次加盐结果不同（盐随机）", hash != hash2 && salt != salt2);
        Program.Check("不同盐仍能校验通过", SecurityService.Verify("my-pass-123", hash2, salt2));
    }
}

internal static class BackupTests
{
    public static void Run()
    {
        AppPaths.EnsureAll();

        var shotDir = Path.Combine(AppPaths.Shots, "2026-11");
        Directory.CreateDirectory(shotDir);
        var shot = Path.Combine(shotDir, "20261116_120000_001.png");
        File.WriteAllBytes(shot, new byte[] { 1, 2, 3, 4, 5 });

        var configPath = AppPaths.ConfigFile;
        File.WriteAllText(configPath, "{\"version\":1}");

        var zip = BackupService.CreateBackup(out var count, out var bytes);
        Program.Check("备份包已生成", File.Exists(zip));
        Program.Check("备份包含文件", count >= 2, $"实际 {count} 个");
        Program.Check("备份包非空", bytes > 0);

        // 删掉原文件再还原，验证真的从包里恢复
        File.Delete(shot);
        File.Delete(configPath);
        var restored = BackupService.Restore(zip, out var error);
        Program.Check("还原无错误", error == null, error);
        Program.Check("还原后图片回来了", File.Exists(shot), $"还原了 {restored} 个文件");
        Program.Check("还原后配置回来了", File.Exists(configPath));
    }
}

/// <summary>
/// R1–R7：锚点重构后的回归断言。设计文档第 7 章。
/// 注：Interval 规则的「相位」是起算点 + N×间隔，其 TimeOfDay 天然逐点变化，
/// 因此 R1 对 Interval 用「差值是间隔的整数倍」判定，其余日历类规则用 TimeOfDay 相等判定。
/// </summary>
internal static class AnchorFlowTests
{
    public static void Run()
    {
        R1_ThreeArgPhase();
        R2_ExitRouting();
        R3_QuietAnchor();
        R4_DoneCount();
        R5_DirtyData();
        R6_TimeJump();
        R7_BackupSafety();
    }

    private static void R1_ThreeArgPhase()
    {
        var cases = new (RepeatRule Rule, DateTime Anchor, DateTime Now, string Label)[]
        {
            (new RepeatRule { Type = RepeatType.Interval, IntervalValue = 5, IntervalUnit = IntervalUnit.Minute },
                new DateTime(2026, 11, 16, 9, 0, 0), new DateTime(2026, 11, 16, 9, 15, 0), "Interval 5min 09:00→09:20"),
            (new RepeatRule { Type = RepeatType.Daily },
                new DateTime(2026, 11, 16, 13, 52, 0), new DateTime(2026, 11, 18, 15, 0, 0), "Daily 13:52 → 11-19 13:52"),
            (new RepeatRule { Type = RepeatType.Weekly, Weekdays = new List<int> { 1, 3, 5 } },
                new DateTime(2026, 11, 16, 9, 0, 0), new DateTime(2026, 11, 28, 10, 0, 0), "Weekly 一三五 09:00"),
            (new RepeatRule { Type = RepeatType.Monthly, DayOfMonth = 31 },
                new DateTime(2026, 1, 31, 9, 0, 0), new DateTime(2026, 3, 15, 10, 0, 0), "Monthly 31 号 09:00"),
            (new RepeatRule { Type = RepeatType.YearMonths, Months = new List<int> { 3, 6, 9, 12 }, DayOfMonth = 15 },
                new DateTime(2026, 11, 16, 9, 0, 0), new DateTime(2026, 12, 20, 10, 0, 0), "YearMonths 15 号 09:00")
        };

        foreach (var (rule, anchor, now, label) in cases)
        {
            var next = RepeatEngine.NextAfter(rule, anchor, now);
            Program.Check("R1 " + label + "：next 存在且 > now", next.HasValue && next.Value > now,
                $"next={next?.ToString("yyyy-MM-dd HH:mm") ?? "null"}, now={now:yyyy-MM-dd HH:mm}");
            if (!next.HasValue) continue;

            if (rule.Type == RepeatType.Interval)
            {
                var stepTicks = TimeSpan.FromMinutes(rule.IntervalValue).Ticks;
                Program.Check("R1 " + label + "：相位不漂（整倍间隔）",
                    (next.Value - anchor).Ticks % stepTicks == 0, $"delta={(next.Value - anchor)}");
            }
            else
            {
                Program.Equal("R1 " + label + "：相位不漂（TimeOfDay）", anchor.TimeOfDay, next.Value.TimeOfDay);
            }
        }

        // 文档给出的两条具体期望值
        var interval = new RepeatRule { Type = RepeatType.Interval, IntervalValue = 5, IntervalUnit = IntervalUnit.Minute };
        Program.Equal("R1 Interval 09:00 + now 09:15 → 09:20", new DateTime(2026, 11, 16, 9, 20, 0),
            RepeatEngine.NextAfter(interval, new DateTime(2026, 11, 16, 9, 0, 0), new DateTime(2026, 11, 16, 9, 15, 0)));
        var daily = new RepeatRule { Type = RepeatType.Daily };
        Program.Equal("R1 Daily 11-16 13:52 + now 11-18 15:00 → 11-19 13:52", new DateTime(2026, 11, 19, 13, 52, 0),
            RepeatEngine.NextAfter(daily, new DateTime(2026, 11, 16, 13, 52, 0), new DateTime(2026, 11, 18, 15, 0, 0)));

        var doneRule = new RepeatRule { Type = RepeatType.Daily, MaxCount = 1, DoneCount = 1 };
        Program.Check("R1 次数耗尽返回 null",
            RepeatEngine.NextAfter(doneRule, DateTime.Now, DateTime.Now.AddDays(1)) == null);
    }

    private static void R2_ExitRouting()
    {
        var none = ScreenRemind.UI.ReminderAction.None;
        Program.Equal("R2 ResolveAction(None,true)=Renew", ScreenRemind.UI.ReminderAction.Renew,
            RepeatFlow.ResolveAction(none, true));
        Program.Equal("R2 ResolveAction(None,false)=Snoozed", ScreenRemind.UI.ReminderAction.Snoozed,
            RepeatFlow.ResolveAction(none, false));
        Program.Equal("R2 ResolveAction(Acknowledged,true)=Acknowledged", ScreenRemind.UI.ReminderAction.Acknowledged,
            RepeatFlow.ResolveAction(ScreenRemind.UI.ReminderAction.Acknowledged, true));
        Program.Equal("R2 ResolveAction(Snoozed,false)=Snoozed", ScreenRemind.UI.ReminderAction.Snoozed,
            RepeatFlow.ResolveAction(ScreenRemind.UI.ReminderAction.Snoozed, false));

        var t0 = new DateTime(2026, 11, 16, 13, 52, 0);
        var task = new RemindTask { Id = "r2", TriggerAt = t0, Repeat = new RepeatRule { Type = RepeatType.Daily } };
        var next1 = RepeatFlow.Acknowledge(task, t0);
        Program.Equal("R2 Ack next=11-17 13:52", new DateTime(2026, 11, 17, 13, 52, 0), next1);
        Program.Equal("R2 Ack 后 DoneCount=1", 1, task.Repeat.DoneCount);
        Program.Equal("R2 Ack 后 State=Done", TaskState.Done, task.State);
        Program.Equal("R2 Ack 后 LastFiredAt", t0, task.LastFiredAt);

        var follow = task.Clone();
        follow.TriggerAt = next1!.Value;   // 模拟 AppController 建 follow
        follow.State = TaskState.Pending;

        var snoozeAt = new DateTime(2026, 11, 17, 13, 52, 0);
        RepeatFlow.Snooze(follow, snoozeAt, 15, DeferKind.UserSnooze);
        Program.Equal("R2 Snooze 后 PlanAnchor 不变（INV-1）", snoozeAt, follow.PlanAnchor);
        Program.Equal("R2 Snooze 后 EffectiveTime=14:07", new DateTime(2026, 11, 17, 14, 7, 0), follow.EffectiveTime);
        Program.Equal("R2 Snooze 不加 DoneCount", 1, follow.Repeat.DoneCount);
        Program.Equal("R2 Snooze 来源=UserSnooze", DeferKind.UserSnooze, follow.DeferKind);

        var next2 = RepeatFlow.Acknowledge(follow, new DateTime(2026, 11, 17, 14, 7, 0));
        Program.Equal("R2 稍后确认仍回 13:52 相位（未被 14:07 带偏）", new DateTime(2026, 11, 18, 13, 52, 0), next2);

        var follow2 = follow.Clone();
        follow2.TriggerAt = next2!.Value;
        follow2.SnoozeUntil = null;
        var wake = new DateTime(2026, 11, 20, 15, 0, 0);
        var n3 = RepeatFlow.Acknowledge(follow2, wake);
        Program.Check("R2 休眠唤醒只补一次（next>now）", n3.HasValue && n3.Value > wake, $"n3={n3}");
        Program.Equal("R2 休眠补弹后仍是 13:52 相位", new TimeSpan(13, 52, 0), n3!.Value.TimeOfDay);
    }

    private static void R3_QuietAnchor()
    {
        var cfg = new AppConfig { QuietEnabled = true, QuietStart = "23:00", QuietEnd = "08:00" };
        var repo = new TaskRepository();
        var task = new RemindTask
        {
            Id = "r3",
            TriggerAt = new DateTime(2026, 11, 16, 23, 30, 0),
            Repeat = new RepeatRule { Type = RepeatType.Daily }
        };
        repo.Add(task);
        using var scheduler = new Scheduler(repo, () => cfg, () => new DateTime(2026, 11, 16, 23, 30, 0));
        var due = 0;
        scheduler.Due += (_, _) => due++;

        scheduler.Tick(new DateTime(2026, 11, 16, 23, 30, 0));
        var live = repo.Get("r3")!;
        Program.Equal("R3 免打扰后 PlanAnchor 不变（INV-1）", new DateTime(2026, 11, 16, 23, 30, 0), live.PlanAnchor);
        Program.Equal("R3 免打扰后 EffectiveTime=次日08:00", new DateTime(2026, 11, 17, 8, 0, 0), live.EffectiveTime);
        Program.Equal("R3 免打扰来源=Quiet", DeferKind.Quiet, live.DeferKind);
        Program.Equal("R3 免打扰当次不弹窗", 0, due);

        scheduler.Tick(new DateTime(2026, 11, 17, 8, 0, 0));
        Program.Equal("R3 顺延到期后弹一次", 1, due);

        var live2 = repo.Get("r3")!;
        var followAt = RepeatFlow.Acknowledge(live2, new DateTime(2026, 11, 17, 8, 0, 0));
        Program.Equal("R3 确认后 follow 锚点仍是 23:30", new DateTime(2026, 11, 17, 23, 30, 0), followAt);
    }

    private static void R4_DoneCount()
    {
        var task = new RemindTask
        {
            Id = "r4",
            TriggerAt = new DateTime(2026, 11, 16, 9, 0, 0),
            Repeat = new RepeatRule { Type = RepeatType.Daily, MaxCount = 3 }
        };
        var n1 = RepeatFlow.Acknowledge(task, new DateTime(2026, 11, 16, 9, 0, 0));
        Program.Equal("R4 Ack1 DoneCount=1", 1, task.Repeat.DoneCount);
        Program.Check("R4 Ack1 仍有下一次", n1.HasValue);
        var n2 = RepeatFlow.Acknowledge(task, new DateTime(2026, 11, 17, 9, 0, 0));
        Program.Equal("R4 Ack2 DoneCount=2", 2, task.Repeat.DoneCount);
        Program.Check("R4 Ack2 仍有下一次", n2.HasValue);
        var n3 = RepeatFlow.Acknowledge(task, new DateTime(2026, 11, 18, 9, 0, 0));
        Program.Equal("R4 Ack3 DoneCount=3", 3, task.Repeat.DoneCount);
        Program.Check("R4 Ack3 触顶 → next=null", n3 == null);

        var skip = new RemindTask
        {
            Id = "r4skip",
            TriggerAt = new DateTime(2026, 11, 16, 9, 0, 0),
            Repeat = new RepeatRule { Type = RepeatType.Daily }
        };
        var sn = RepeatFlow.SkipNext(skip, new DateTime(2026, 11, 16, 9, 0, 0));
        Program.Equal("R4 SkipNext DoneCount 0→1", 1, skip.Repeat.DoneCount);
        Program.Equal("R4 SkipNext 锚点前进一天", new DateTime(2026, 11, 17, 9, 0, 0), skip.TriggerAt);
        Program.Check("R4 SkipNext 返回非空", sn.HasValue);

        var repo = new TaskRepository();
        repo.Add(new RemindTask
        {
            Id = "r4persist",
            TriggerAt = DateTime.Now.AddDays(1),
            Repeat = new RepeatRule { Type = RepeatType.Daily, DoneCount = 2 }
        });
        var reloaded = new TaskRepository().All().FirstOrDefault(t => t.Id == "r4persist");
        Program.Check("R4 DoneCount 落盘闭环", reloaded != null && reloaded.Repeat.DoneCount == 2);
    }

    private static void R5_DirtyData()
    {
        Program.Check("R5 脏规则 Describe 不抛",
            RepeatEngine.Describe(new RepeatRule { Type = RepeatType.Weekly, Weekdays = null! }) != null);
        Program.Check("R5 脏规则 Validate 不抛且返回错误",
            RepeatEngine.Validate(new RepeatRule { Type = RepeatType.YearMonths, Months = null! }) != null);

        File.WriteAllText(AppPaths.TasksFile,
            "{\"version\":1,\"tasks\":[{\"id\":\"dirty\",\"triggerAt\":\"2026-11-16T13:52:00\"," +
            "\"repeat\":{\"type\":\"Weekly\",\"weekdays\":null,\"months\":null},\"state\":\"Pending\"}]}");
        var loaded = new TaskRepository().All();
        var dirty = loaded.FirstOrDefault(t => t.Id == "dirty");
        Program.Check("R5 脏 tasks.json 不抛且能加载", dirty != null);
        if (dirty != null)
        {
            Program.Equal("R5 weekdays=null 加载后 Count=0", 0, dirty.Repeat.Weekdays.Count);
            Program.Equal("R5 months=null 加载后 Count=0", 0, dirty.Repeat.Months.Count);
        }

        var task = new RemindTask
        {
            Id = "clone",
            DeferKind = DeferKind.Missed,
            LastFiredAt = new DateTime(2026, 11, 16, 9, 0, 0)
        };
        var clone = task.Clone();
        Program.Equal("R5 Clone 含 DeferKind", DeferKind.Missed, clone.DeferKind);
        Program.Equal("R5 Clone 含 LastFiredAt", task.LastFiredAt, clone.LastFiredAt);
    }

    private static void R6_TimeJump()
    {
        var cfg = new AppConfig();
        var repo = new TaskRepository();
        repo.Add(new RemindTask
        {
            Id = "r6",
            TriggerAt = new DateTime(2026, 11, 16, 9, 0, 0),
            Repeat = new RepeatRule { Type = RepeatType.None }
        });
        using var scheduler = new Scheduler(repo, () => cfg, () => new DateTime(2026, 11, 16, 10, 0, 0));
        var due = 0;
        scheduler.Due += (_, _) => due++;

        var now = new DateTime(2026, 11, 16, 10, 0, 0);
        scheduler.Tick(now);
        scheduler.Tick(now);
        Program.Equal("R6 连续两次 Tick 只 Due 一次（_inflight 去重）", 1, due);

        var threw = false;
        try { scheduler.Tick(now.AddDays(-2)); } catch { threw = true; }
        Program.Check("R6 时间回拨不抛异常", !threw);
        Program.Equal("R6 回拨不产生额外 Due", 1, due);
    }

    private static void R7_BackupSafety()
    {
        AppPaths.EnsureAll();
        var evilZip = Path.Combine(AppPaths.Data, "evil.zip");
        using (var fs = new System.IO.FileStream(evilZip, System.IO.FileMode.Create))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            AddZipEntry(zip, "data/../../data_evil/x.txt", "pwned");
            AddZipEntry(zip, "data/../data_evil2/y.txt", "pwned");
        }
        BackupService.Restore(evilZip, out var error1);
        Program.Check("R7 恶意包被拒绝且无 restoring 错误", error1 == null, error1);
        Program.Check("R7 路径穿越未越界（沙箱内）",
            !File.Exists(Path.GetFullPath(Path.Combine(AppPaths.Root, "data_evil", "x.txt"))) &&
            !File.Exists(Path.GetFullPath(Path.Combine(AppPaths.Root, "data_evil2", "y.txt"))));
        Program.Check("R7 路径穿越未越界（父目录）",
            !File.Exists(Path.GetFullPath(Path.Combine(AppPaths.Root, "..", "data_evil", "x.txt"))));

        // 换入阶段制造必然失败：第二个条目的目标是已存在的目录
        var probe = Path.Combine(AppPaths.Data, "rollback_probe.txt");
        File.WriteAllText(probe, "old");
        var blocker = Path.Combine(AppPaths.Data, "blocker_dir");
        Directory.CreateDirectory(blocker);
        var failZip = Path.Combine(AppPaths.Data, "fail.zip");
        using (var fs = new System.IO.FileStream(failZip, System.IO.FileMode.Create))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            AddZipEntry(zip, "data/rollback_probe.txt", "new");
            AddZipEntry(zip, "data/blocker_dir", "make-me-fail");
        }
        BackupService.Restore(failZip, out var error2);
        Program.Check("R7 中途失败被感知", error2 != null, error2);
        Program.Equal("R7 失败后主文件字节未变（回滚成功）", "old", File.ReadAllText(probe));
    }

    private static void AddZipEntry(System.IO.Compression.ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
