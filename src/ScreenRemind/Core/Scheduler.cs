using System.Windows.Forms;

namespace ScreenRemind.Core;

/// <summary>
/// 调度器：1 秒心跳。不用「睡到下一个时间点」的长延时，是因为机器休眠/改系统时间会让长延时失真，
/// 而按墙钟逐秒比对天然能把「睡过去的那次」补回来。
/// </summary>
public sealed class Scheduler : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly TaskRepository _repo;
    private readonly Func<AppConfig> _config;
    private readonly Func<DateTime> _now;
    /// <summary>已弹出但用户还没处理的任务，避免每秒重复弹</summary>
    private readonly HashSet<string> _inflight = new();
    private DateTime _lastTick = DateTime.Now;
    private bool _ticking;

    /// <summary>到点需要弹出提醒</summary>
    public event EventHandler<RemindTask>? Due;

    /// <summary>预提醒（正式提醒前的轻提示）</summary>
    public event EventHandler<RemindTask>? PreNotify;

    /// <summary>一次性顺延发生（供托盘文案/统计订阅）；Core 不直接碰 UI。</summary>
    internal event EventHandler<(RemindTask Task, DateTime Until, DeferKind Kind)>? NotifyDefer;

    public bool Paused { get; set; }

    /// <param name="nowProvider">时钟注入缝；自检可注入确定性时刻，默认使用系统墙钟。</param>
    public Scheduler(TaskRepository repo, Func<AppConfig> config, Func<DateTime>? nowProvider = null)
    {
        _repo = repo;
        _config = config;
        _now = nowProvider ?? (() => DateTime.Now);
        _lastTick = _now();
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => OnTimerTick();
    }

    public void Start()
    {
        _lastTick = _now();
        _timer.Start();
        TickCore(_now());   // 启动立刻扫一次，补偿上次关机期间漏掉的
    }

    public void Stop() => _timer.Stop();

    /// <summary>用户已处理（知晓/顺延/改期），允许该任务再次进入调度。</summary>
    public void MarkHandled(string taskId)
    {
        _inflight.Remove(taskId);
    }

    /// <summary>数据被整体替换（如还原备份）后，清空已弹记录，重新按磁盘状态调度。</summary>
    public void MarkAllHandled()
    {
        _inflight.Clear();
    }

    /// <summary>新任务创建后立即评估一次，避免「刚建完等下一秒」的观感延迟。</summary>
    public void Poke() => TickCore(_now());

    /// <summary>Timer 回调入口：从注入时钟取当前时刻。</summary>
    private void OnTimerTick() => TickCore(_now());

    /// <summary>自检注入时刻的测试缝；与 Timer 回调共用同一套逻辑。</summary>
    internal void Tick(DateTime now) => TickCore(now);

    private void TickCore(DateTime now)
    {
        if (_ticking) return;
        _ticking = true;
        try
        {
            var jump = (now - _lastTick).TotalSeconds;
            var clockWentBack = now < _lastTick.AddMinutes(-2);   // C-17：墙钟回拨只告警，不强行改写调度
            _lastTick = now;

            if (jump > 90)
            {
                Log.Info($"时间跳跃 {jump:F0} 秒（可能刚从睡眠/休眠恢复），开始检查漏提醒");
            }
            if (clockWentBack)
            {
                Log.Warn($"检测到系统时间回拨 {(-jump / 60):F0} 分钟，按墙钟继续调度");
            }

            if (Paused) return;

            var cfg = _config();
            var pending = _repo.All()
                .Where(t => t.State != TaskState.Done)
                .OrderBy(t => t.EffectiveTime)
                .ToList();

            foreach (var task in pending)
            {
                if (_inflight.Contains(task.Id)) continue;

                var fireAt = task.EffectiveTime;

                if (fireAt <= now)
                {
                    // 免打扰时段：不弹窗，顺延「本次生效时刻」，绝不改写计划锚点 TriggerAt（INV-1）
                    if (cfg.QuietEnabled && IsInQuiet(now, cfg))
                    {
                        var resume = QuietEnd(now, cfg);
                        task.SnoozeUntil = resume;                 // 唯一写入点
                        task.DeferKind = DeferKind.Quiet;
                        task.PreNotifiedAt = null;
                        // 【禁止】task.TriggerAt = resume;        旧实现会污染计划锚点
                        // 【禁止】task.SnoozeUntil = null;        旧实现会抹掉本次顺延
                        _repo.Update(task);
                        Log.Info($"任务 {task.Id} 落在免打扰时段，本次顺延至 {resume:yyyy-MM-dd HH:mm}（计划锚点仍为 {task.PlanAnchor:HH:mm}）");
                        NotifyDefer?.Invoke(this, (task, resume, DeferKind.Quiet));
                        continue;
                    }

                    // 过期补弹：仅标记来源，不计 DoneCount、不写 TriggerAt（INV-3）
                    if (fireAt < now.AddMinutes(-1))
                    {
                        task.DeferKind = DeferKind.Missed;
                        Log.Info($"任务 {task.Id} 为过期补弹（计划 {fireAt:MM-dd HH:mm}，现 {now:MM-dd HH:mm}）");
                    }

                    _inflight.Add(task.Id);
                    Due?.Invoke(this, task);
                    continue;
                }

                // 预提醒：只在正式提醒之前触发一次；顺延后锚点可能已过去，必须用本次生效时刻计算
                if (task.PreNotifyMinutes > 0 && task.State != TaskState.Done && task.PreNotifiedAt == null)
                {
                    var minutesLeft = (task.EffectiveTime - now).TotalMinutes;
                    if (minutesLeft <= task.PreNotifyMinutes)
                    {
                        task.PreNotifiedAt = now;
                        _repo.Update(task);
                        PreNotify?.Invoke(this, task);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("调度心跳异常", ex);
        }
        finally
        {
            _ticking = false;
        }
    }

    public static bool TryParseTime(string text, out TimeSpan span)
    {
        span = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return TimeSpan.TryParse(text.Trim(), out span) && span >= TimeSpan.Zero && span < TimeSpan.FromDays(1);
    }

    /// <summary>是否处于免打扰时段（支持跨零点，如 23:00–08:00）。</summary>
    public static bool IsInQuiet(DateTime now, AppConfig cfg)
    {
        if (!cfg.QuietEnabled) return false;
        if (!TryParseTime(cfg.QuietStart, out var start) || !TryParseTime(cfg.QuietEnd, out var end)) return false;
        if (start == end) return false;

        var t = now.TimeOfDay;
        return start < end
            ? t >= start && t < end
            : t >= start || t < end;
    }

    /// <summary>免打扰时段的结束时刻（今天未到则今天，否则明天）。</summary>
    public static DateTime QuietEnd(DateTime now, AppConfig cfg)
    {
        if (!TryParseTime(cfg.QuietEnd, out var end)) return now.AddMinutes(30);
        var today = now.Date + end;
        return today > now ? today : today.AddDays(1);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
