using System.Windows.Forms;

namespace ScreenRemind.Core;

public enum RepeatType
{
    /// <summary>只提醒一次</summary>
    None,
    /// <summary>每隔 N 分钟 / 小时 / 天</summary>
    Interval,
    /// <summary>每天同一时刻</summary>
    Daily,
    /// <summary>每周勾选的星期几</summary>
    Weekly,
    /// <summary>每月某日（不足则取当月最后一天）</summary>
    Monthly,
    /// <summary>每年勾选的若干月份的某日</summary>
    YearMonths
}

public enum IntervalUnit { Minute, Hour, Day }

public enum TaskState
{
    /// <summary>等待触发</summary>
    Pending,
    /// <summary>已稍后提醒，按 SnoozeUntil 再次触发</summary>
    Snoozed,
    /// <summary>已处理完毕</summary>
    Done
}

public enum PopupStrength
{
    /// <summary>普通置顶</summary>
    Normal,
    /// <summary>强制置顶 + 任务栏闪烁</summary>
    Strong,
    /// <summary>全屏遮罩，必须点了才消失</summary>
    Mask
}

public enum CaptureSource { Region, FullScreen, Window }

/// <summary>重复规则。作为纯数据对象，交给 RepeatEngine 计算下一次时间。</summary>
public sealed class RepeatRule
{
    public RepeatType Type { get; set; } = RepeatType.None;
    public int IntervalValue { get; set; } = 1;
    public IntervalUnit IntervalUnit { get; set; } = IntervalUnit.Day;
    /// <summary>0=周日 … 6=周六（与 DayOfWeek 枚举一致）</summary>
    public List<int> Weekdays { get; set; } = new();
    public int DayOfMonth { get; set; } = 1;
    /// <summary>1–12</summary>
    public List<int> Months { get; set; } = new();
    /// <summary>截止日期（含当天），null = 永不结束</summary>
    public DateTime? Until { get; set; }
    /// <summary>总次数上限，null = 不限</summary>
    public int? MaxCount { get; set; }
    /// <summary>已经触发过的次数</summary>
    public int DoneCount { get; set; }

    public RepeatRule Clone() => new()
    {
        Type = Type,
        IntervalValue = IntervalValue,
        IntervalUnit = IntervalUnit,
        Weekdays = new List<int>(Weekdays),
        DayOfMonth = DayOfMonth,
        Months = new List<int>(Months),
        Until = Until,
        MaxCount = MaxCount,
        DoneCount = DoneCount
    };
}

public sealed class RemindTask
{
    public string Id { get; set; } = string.Empty;
    /// <summary>相对根目录的图片路径，统一正斜杠</summary>
    public string Image { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>计划提醒时间（不含稍后提醒）</summary>
    public DateTime TriggerAt { get; set; } = DateTime.Now;
    public RepeatRule Repeat { get; set; } = new();
    public TaskState State { get; set; } = TaskState.Pending;
    /// <summary>同一条提醒链（「继续设下一次」沿用同一 ChainId）</summary>
    public string ChainId { get; set; } = string.Empty;
    public DateTime? AckAt { get; set; }
    public DateTime? SnoozeUntil { get; set; }
    /// <summary>预提醒已发出的时间，避免重复轻提示</summary>
    public DateTime? PreNotifiedAt { get; set; }
    /// <summary>正式提醒前多少分钟轻提示，0 = 关闭</summary>
    public int PreNotifyMinutes { get; set; }
    public CaptureSource Source { get; set; } = CaptureSource.Region;

    public DateTime EffectiveTime => SnoozeUntil ?? TriggerAt;

    public RemindTask Clone() => new()
    {
        Id = Id,
        Image = Image,
        Note = Note,
        CreatedAt = CreatedAt,
        TriggerAt = TriggerAt,
        Repeat = Repeat.Clone(),
        State = State,
        ChainId = ChainId,
        AckAt = AckAt,
        SnoozeUntil = SnoozeUntil,
        PreNotifiedAt = PreNotifiedAt,
        PreNotifyMinutes = PreNotifyMinutes,
        Source = Source
    };
}

public sealed class TasksFile
{
    public int Version { get; set; } = 1;
    public List<RemindTask> Tasks { get; set; } = new();
}

public sealed class HistoryEntry
{
    public DateTime At { get; set; } = DateTime.Now;
    public string TaskId { get; set; } = string.Empty;
    public string ChainId { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTime TriggerAt { get; set; }
    public string Note { get; set; } = string.Empty;
}

public sealed class HotkeySetting
{
    /// <summary>MOD_ALT=1 / MOD_CONTROL=2 / MOD_SHIFT=4 / MOD_WIN=8，可叠加</summary>
    public uint Modifiers { get; set; } = NativeHotkey.ModControl | NativeHotkey.ModAlt;
    /// <summary>主键虚拟键码</summary>
    public uint Key { get; set; } = (uint)System.Windows.Forms.Keys.S;
    /// <summary>true = 走低级键盘钩子的「任意两键」模式（旧配置字段，保留兼容）</summary>
    public bool UseRawCombo { get; set; }
    public uint RawKey1 { get; set; } = (uint)System.Windows.Forms.Keys.A;
    public uint RawKey2 { get; set; } = (uint)System.Windows.Forms.Keys.S;

    /// <summary>完整按键组合（按录制顺序）。新配置以它为准，不再限制两个键。</summary>
    public List<uint> Keys { get; set; } = new();

    /// <summary>取实际生效的按键集合；旧配置自动从 Modifiers/Key/RawKey 合成。</summary>
    public List<uint> EffectiveKeys()
    {
        if (Keys is { Count: > 0 })
        {
            return Keys.Select(HotkeyKeys.Normalize).Distinct().ToList();
        }

        if (UseRawCombo)
        {
            return new[] { RawKey1, RawKey2 }
                .Where(k => k != 0)
                .Select(HotkeyKeys.Normalize)
                .Distinct()
                .ToList();
        }

        var list = new List<uint>();
        if ((Modifiers & NativeHotkey.ModControl) != 0) list.Add(HotkeyKeys.Control);
        if ((Modifiers & NativeHotkey.ModAlt) != 0) list.Add(HotkeyKeys.Alt);
        if ((Modifiers & NativeHotkey.ModShift) != 0) list.Add(HotkeyKeys.Shift);
        if ((Modifiers & NativeHotkey.ModWin) != 0) list.Add(HotkeyKeys.Win);
        if (Key != 0) list.Add(HotkeyKeys.Normalize(Key));
        return list.Distinct().ToList();
    }

    /// <summary>把一组按键写进旧字段，保证旧版本回退时也能用。</summary>
    public void SyncLegacyFields()
    {
        var keys = EffectiveKeys();
        var plain = keys.Where(k => !HotkeyKeys.IsModifier(k)).ToList();
        var mods = HotkeyKeys.ToRegisterModifiers(keys);

        Keys = keys;
        Modifiers = mods;
        UseRawCombo = !(mods != 0 && plain.Count == 1);
        if (plain.Count == 1)
        {
            Key = plain[0];
        }
        else if (plain.Count >= 2)
        {
            RawKey1 = plain[0];
            RawKey2 = plain[1];
        }
    }

    public HotkeySetting Clone() => new()
    {
        Modifiers = Modifiers,
        Key = Key,
        UseRawCombo = UseRawCombo,
        RawKey1 = RawKey1,
        RawKey2 = RawKey2,
        Keys = new List<uint>(Keys)
    };
}

/// <summary>虚拟键码的归一化与分类，热键录制、注册、显示共用。</summary>
public static class HotkeyKeys
{
    public const uint Shift = 0x10;
    public const uint Control = 0x11;
    public const uint Alt = 0x12;
    public const uint Win = 0x5B;

    /// <summary>左右键统一成同一个码，避免「左Ctrl+Alt」和「右Ctrl+Alt」被当成两套。</summary>
    public static uint Normalize(uint vk) => vk switch
    {
        0xA0 or 0xA1 => Shift,
        0xA2 or 0xA3 => Control,
        0xA4 or 0xA5 => Alt,
        0x5C => Win,
        _ => vk
    };

    public static bool IsModifier(uint vk) => vk is Shift or Control or Alt or Win;

    public static uint ToRegisterModifiers(IEnumerable<uint> keys)
    {
        var mods = 0u;
        foreach (var key in keys)
        {
            mods |= key switch
            {
                Control => NativeHotkey.ModControl,
                Alt => NativeHotkey.ModAlt,
                Shift => NativeHotkey.ModShift,
                Win => NativeHotkey.ModWin,
                _ => 0u
            };
        }
        return mods;
    }
}

/// <summary>RegisterHotKey 的修饰键常量（与 Win32 保持一致）</summary>
public static class NativeHotkey
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public HotkeySetting Hotkey { get; set; } = new();
    public PopupStrength PopupStrength { get; set; } = PopupStrength.Normal;
    public bool CopyToClipboard { get; set; } = true;
    public bool AutoStart { get; set; }
    public bool QuietEnabled { get; set; }
    public string QuietStart { get; set; } = "23:00";
    public string QuietEnd { get; set; } = "08:00";
    public int DefaultPreNotifyMinutes { get; set; }
    public int SnoozeMinutes { get; set; } = 15;
    /// <summary>「下班前提醒」用的时刻，24 小时制 HH:mm</summary>
    public string OffWorkTime { get; set; } = "18:00";
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
    public bool FirstRunDone { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.Now;

    public bool HasPassword => !string.IsNullOrEmpty(PasswordHash) && !string.IsNullOrEmpty(PasswordSalt);
}
