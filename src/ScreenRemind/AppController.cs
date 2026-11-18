using ScreenRemind.Core;
using ScreenRemind.Platform;
using ScreenRemind.UI;

namespace ScreenRemind;

/// <summary>
/// 应用中枢：装配各服务、承载托盘、串起「截图 → 定提醒 → 到点弹窗 → 处理」的完整链路。
/// UI 只收集用户意图，落盘与状态流转都在这里做，避免逻辑散落在各个窗体的点击事件里。
/// 所有出口的字段变更与下一次计算统一委托给 Core/RepeatFlow 纯函数（C-1）。
/// </summary>
public sealed class AppController : ApplicationContext
{
    public AppConfig Config { get; private set; }
    public TaskRepository Repo { get; }
    public Scheduler Scheduler { get; }

    /// <summary>装配自检时跳过 PruneDone 等有副作用的维护动作（C-20）。</summary>
    internal bool SkipMaintenance { get; set; }

    private readonly HotkeyManager _hotkey = new();
    private readonly NotifyIcon _tray;
    private readonly Queue<RemindTask> _queue = new();
    private ToolStripMenuItem? _pauseItem;

    private bool _showingReminder;
    private bool _capturing;
    private bool _unlocked;
    private bool _unlockNotified;
    private string? _lastCreatedTaskId;   // C-13：快捷提醒落定后可从托盘撤销

    private TaskListForm? _taskListForm;
    private CalendarForm? _calendarForm;
    private SettingsForm? _settingsForm;

    public AppController()
    {
        Config = JsonStore.Load(AppPaths.ConfigFile, () => new AppConfig());
        Repo = new TaskRepository();
        Scheduler = new Scheduler(Repo, () => Config);

        Scheduler.Due += OnDue;
        Scheduler.PreNotify += OnPreNotify;
        Scheduler.NotifyDefer += (_, d) =>
            Notify($"任务落在免打扰时段，已顺延到 {d.Until:HH:mm}（计划时刻 {d.Task.PlanAnchor:HH:mm}）", ToolTipIcon.Info);

        _hotkey.Pressed += (_, _) => StartCapture();
        _hotkey.WakeRequested += (_, _) => ShowTaskList();

        _tray = BuildTray();

        // QA-P2-R3：落盘失败从 Core 以事件上抛，这里转成托盘红字，用户能立刻知道没存上。
        // 放在 BuildTray 之后，确保处理器触发时 _tray 已就绪。
        Repo.PersistFailed += (_, path) =>
            Notify($"任务数据保存失败，本次更改未写入磁盘！请检查数据目录（{path}）是否可写", ToolTipIcon.Error);
    }

    public void Start()
    {
        if (!ApplyHotkeyTo(Config.Hotkey, out var error))
        {
            Log.Warn("启动时快捷键注册失败：" + error);
            Notify("截图快捷键没能注册成功：" + error + "　请到「设置」里换一个组合", ToolTipIcon.Warning);
        }

        // C-20/QA-P2-R2：--check-ui 用 SkipMaintenance=true 做只读装配自检，
        // 绝不能触发自启修复（RepairIfStale 带删除语义，会动真实快捷方式）。
        if (!SkipMaintenance) AutoStart.RepairIfStale(Config.AutoStart);
        if (!SkipMaintenance) Repo.PruneDone();
        Log.Cleanup();

        Scheduler.Start();
        Log.Info($"启动完成，数据目录：{AppPaths.Root}");

        if (!Config.FirstRunDone)
        {
            Config.FirstRunDone = true;
            SaveConfig();
            Notify("已就绪。按 " + HotkeyText.Describe(Config.Hotkey) + " 截取屏幕区域；右键托盘图标可打开任务列表。", ToolTipIcon.Info);

            // C-12：首次选盘后给一次可关闭的三步上手提示（--check-ui 自检时抑制）
            if (!SkipMaintenance)
            {
                MessageBox.Show(
                    "3 步上手：\n" +
                    "1. 按 " + HotkeyText.Describe(Config.Hotkey) + " 截取屏幕区域\n" +
                    "2. 在面板上标注，点「设置提醒」或选一个快捷时刻\n" +
                    "3. 到点会弹窗，点「已知晓」即完成；稍后/免打扰都不会丢\n\n" +
                    "右键托盘图标可打开任务列表、日历和设置。",
                    "截图提醒 · 快速上手", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        else
        {
            Notify("已在后台运行，按 " + HotkeyText.Describe(Config.Hotkey) + " 截图", ToolTipIcon.None);
        }
    }

    // ---------------- 托盘 ----------------

    private NotifyIcon BuildTray()
    {
        var menu = new ContextMenuStrip { Font = Theme.Base };
        menu.Items.Add("立即截图", null, (_, _) => StartCapture());
        menu.Items.Add("任务列表", null, (_, _) => ShowTaskList());
        menu.Items.Add("日历视图", null, (_, _) => ShowCalendar());
        menu.Items.Add("设置", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());

        _pauseItem = new ToolStripMenuItem("暂停全部提醒");
        _pauseItem.Click += (_, _) =>
        {
            Scheduler.Paused = !Scheduler.Paused;
            _pauseItem.Text = Scheduler.Paused ? "恢复全部提醒" : "暂停全部提醒";
            Notify(Scheduler.Paused ? "已暂停全部提醒" : "已恢复提醒");
        };
        menu.Items.Add(_pauseItem);

        // C-13：快捷提醒落定后，可从这里撤销刚建的提醒（图片文件保留）
        var undoItem = new ToolStripMenuItem("撤销上一条提醒") { Enabled = false, Tag = "undo" };
        undoItem.Click += (_, _) => UndoLastCreated();
        menu.Items.Add(undoItem);

        menu.Items.Add("打开数据目录", null, (_, _) => NativeShell.OpenFolder(AppPaths.Root));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());

        var tray = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "截图提醒（便携版）",
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => ShowTaskList();
        return tray;
    }

    /// <summary>品牌图标由 Theme 从嵌入资源加载并与所有窗口共用（托盘不再依赖从 exe 提取）。</summary>
    private static Icon LoadAppIcon() => Theme.AppIcon;

    public void Notify(string message, ToolTipIcon icon = ToolTipIcon.None)
    {
        try
        {
            _tray.ShowBalloonTip(3200, "截图提醒", message, icon);
        }
        catch (Exception ex)
        {
            Log.Warn("托盘提示失败：" + ex.Message);
        }
    }

    /// <summary>C-13：撤销刚创建的提醒（含快捷提醒），图片文件保留。</summary>
    private void UndoLastCreated()
    {
        if (string.IsNullOrEmpty(_lastCreatedTaskId)) return;
        if (MessageBox.Show("撤销刚创建的提醒？图片文件会保留在 shots 目录。", "撤销提醒",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        Repo.Remove(_lastCreatedTaskId);
        Scheduler.MarkHandled(_lastCreatedTaskId);
        _lastCreatedTaskId = null;
        var undoItem = _tray.ContextMenuStrip?.Items.OfType<ToolStripMenuItem>()
            .FirstOrDefault(i => i.Tag as string == "undo");
        if (undoItem != null) undoItem.Enabled = false;
        Notify("已撤销刚创建的提醒，图片仍在 shots 目录");
        RefreshOpenForms();
    }

    // ---------------- 截图 ----------------

    public void StartCapture()
    {
        if (_capturing) return;
        _capturing = true;
        var hidden = new List<(Form Form, FormWindowState State)>();

        try
        {
            // C-9：先记下前台窗口矩形。等框选窗口盖上来之后就取不到了。
            var windowRect = ScreenCapture.ForegroundWindowBounds();

            // 抓屏前先把自己最小化的窗口让开，否则会截到自己。
            // 不能用 Hide()：它会破坏模态拥有者关系，导致后续 ShowDialog 行为异常。
            foreach (Form form in Application.OpenForms)
            {
                if (form is { Visible: true } && form.ContainsFocus && form != _taskListForm && !form.IsDisposed)
                {
                    hidden.Add((form, form.WindowState));
                    form.WindowState = FormWindowState.Minimized;
                }
            }
            if (_taskListForm is { IsDisposed: false, Visible: true, ContainsFocus: true })
            {
                hidden.Add((_taskListForm, _taskListForm.WindowState));
                _taskListForm.WindowState = FormWindowState.Minimized;
            }

            Application.DoEvents();
            Thread.Sleep(150);

            var screen = ScreenCapture.CaptureVirtualScreen(out var bounds);
            Bitmap? result = null;
            var source = CaptureSource.Region;

            using (var picker = new RegionSelectorForm(screen, bounds, windowRect))
            {
                if (picker.ShowDialog() == DialogResult.OK)
                {
                    result = picker.Result;
                    source = picker.ResultSource;
                }
            }
            screen.Dispose();

            if (result == null) return;

            ShotPanelResult shotResult;
            using (var panel = new ShotPanelForm(result, Config))
            {
                panel.ShowDialog();
                shotResult = panel.Result;
            }

            HandleShot(shotResult, source);
        }
        catch (Exception ex)
        {
            Log.Error("截图流程异常", ex);
            MessageBox.Show("截图失败：" + ex.Message + "\n\n详情见 logs 目录下的日志。", "截图提醒",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            foreach (var (form, state) in hidden)
            {
                if (form.IsDisposed) continue;
                form.WindowState = state;
                if (!form.Visible) form.Show();
            }
            _capturing = false;
        }
    }

    private void HandleShot(ShotPanelResult result, CaptureSource source)
    {
        try
        {
            if (result.Outcome == ShotOutcome.Discard || result.FinalImage == null)
            {
                result.FinalImage?.Dispose();
                result.OriginalImage?.Dispose();
                return;
            }

            var now = DateTime.Now;
            var path = AppPaths.NewShotAbsolutePath(now, out var relative);

            ScreenCapture.SavePng(result.FinalImage, path);

            // 标注过的图：原图另存一份 *.orig.png，方便以后回溯「我当初截的是什么」
            if (result.Annotated && result.OriginalImage != null)
            {
                var origPath = path[..^4] + ".orig.png";
                try
                {
                    ScreenCapture.SavePng(result.OriginalImage, origPath);
                }
                catch (Exception ex)
                {
                    Log.Error("保存标注前原图失败", ex);
                }
            }

            Log.Info($"截图已保存：{relative}");

            if (result.Outcome == ShotOutcome.SaveAndRemind)
            {
                var rule = result.Rule ?? new RepeatRule();
                var task = new RemindTask
                {
                    Id = Repo.NewTaskId(),
                    Image = relative,
                    Note = result.Note ?? string.Empty,
                    CreatedAt = now,
                    TriggerAt = result.TriggerAt,   // 建任务：计划锚点的合法写入点
                    Repeat = rule,
                    State = TaskState.Pending,
                    ChainId = "c_" + Guid.NewGuid().ToString("N")[..8],
                    PreNotifyMinutes = result.PreNotifyMinutes,
                    Source = source
                };
                Repo.Add(task);
                Repo.AppendHistory(new HistoryEntry
                {
                    At = now,
                    TaskId = task.Id,
                    ChainId = task.ChainId,
                    Image = task.Image,
                    Action = "create",
                    TriggerAt = task.TriggerAt,
                    Note = task.Note
                });
                Scheduler.Poke();
                // C-13：记录本次建的任务，托盘右键可撤销（NotifyWithAction 不存在，用菜单入口保守实现）
                _lastCreatedTaskId = task.Id;
                var undoItem = _tray.ContextMenuStrip?.Items.OfType<ToolStripMenuItem>()
                    .FirstOrDefault(i => i.Tag as string == "undo");
                if (undoItem != null) undoItem.Enabled = true;
                Notify($"已排定 {task.TriggerAt:MM-dd HH:mm} 提醒（{RepeatEngine.Describe(rule)}）　右键托盘可撤销", ToolTipIcon.Info);
            }
            else
            {
                Notify("已保存 " + Path.GetFileName(path));
            }
        }
        catch (Exception ex)
        {
            Log.Error("保存截图失败", ex);
            MessageBox.Show("保存截图失败：" + ex.Message, "截图提醒", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            result.FinalImage?.Dispose();
            if (result.OriginalImage != null && !ReferenceEquals(result.OriginalImage, result.FinalImage))
            {
                result.OriginalImage.Dispose();
            }
        }
    }

    // ---------------- 提醒 ----------------

    private void OnDue(object? sender, RemindTask task)
    {
        _queue.Enqueue(task);
        ShowNextReminder();
    }

    private void OnPreNotify(object? sender, RemindTask task)
    {
        var note = string.IsNullOrWhiteSpace(task.Note) ? "有一张截图" : task.Note;
        Notify($"{task.PreNotifyMinutes} 分钟后要提醒你：{note}", ToolTipIcon.Info);
    }

    private void ShowNextReminder()
    {
        if (_showingReminder) return;
        _showingReminder = true;
        try
        {
            var total = _queue.Count;
            var index = 0;
            while (_queue.Count > 0)
            {
                var task = _queue.Dequeue();
                index++;
                HandleOneReminder(task, index, Math.Max(total, index));
            }
        }
        catch (Exception ex)
        {
            Log.Error("提醒弹窗异常", ex);
        }
        finally
        {
            _showingReminder = false;
        }
    }

    private void HandleOneReminder(RemindTask task, int queueIndex, int queueTotal)
    {
        // 任务可能在排队期间已被删除或改期
        var live = Repo.Get(task.Id);
        if (live == null || live.State == TaskState.Done)
        {
            Scheduler.MarkHandled(task.Id);
            return;
        }

        var chain = Repo.ChainImages(live.ChainId);

        // C-6：队列里不止一个时，全屏遮罩会让人看不到还有几个，降级为强提醒
        var strength = queueTotal > 1 && Config.PopupStrength == PopupStrength.Mask
            ? PopupStrength.Strong
            : Config.PopupStrength;

        using var form = new ReminderForm(live, strength, chain, Config.SnoozeMinutes, queueIndex, queueTotal);
        form.ShowDialog();

        AppendHistory(live, "fire");

        // C-6：批量稍后——当前任务 + 仍在队列里的全部任务，统一走 RepeatFlow.Snooze
        if (form.SnoozeAll)
        {
            var batch = new List<RemindTask> { live };
            while (_queue.Count > 0) batch.Add(_queue.Dequeue());
            var minutes = form.SnoozeMinutes > 0 ? form.SnoozeMinutes : Config.SnoozeMinutes;
            foreach (var item in batch)
            {
                var latest = Repo.Get(item.Id);
                if (latest == null || latest.State == TaskState.Done)
                {
                    Scheduler.MarkHandled(item.Id);
                    continue;
                }
                Snooze(latest.Id, minutes, notify: false);
            }
            Notify($"已把 {batch.Count} 个提醒全部顺延 {minutes} 分钟");
            RefreshOpenForms();
            return;
        }

        // C-1：唯一的出口路由入口，默认（关闭弹窗）由 ResolveAction 兜底为稍后
        var action = RepeatFlow.ResolveAction(form.Action, form.RenewRequested);
        switch (action)
        {
            case ReminderAction.Acknowledged:
                Acknowledge(live);
                break;
            case ReminderAction.Snoozed:
                Snooze(live.Id, form.SnoozeMinutes <= 0 ? Config.SnoozeMinutes : form.SnoozeMinutes);
                break;
            case ReminderAction.Renew:
                Renew(live);
                break;
        }

        RefreshOpenForms();
    }

    public void Acknowledge(RemindTask task)
    {
        var now = DateTime.Now;
        var next = RepeatFlow.Acknowledge(task, now);   // 计数、清顺延、算下一次全在纯函数里

        Repo.Update(task);
        Scheduler.MarkHandled(task.Id);
        AppendHistory(task, "ack");

        // 重复规则：沿计划锚点算出下一次并新建一条，原任务留档
        if (next != null)
        {
            var follow = task.Clone();
            follow.Id = Repo.NewTaskId();
            follow.TriggerAt = next.Value;             // 新计划锚点（合法写入点）
            follow.State = TaskState.Pending;
            follow.SnoozeUntil = null;
            follow.DeferKind = DeferKind.None;
            follow.LastFiredAt = null;
            follow.PreNotifiedAt = null;
            follow.AckAt = null;
            follow.Repeat = task.Repeat.Clone();
            Repo.Add(follow);
            Log.Info($"重复提醒已排下一次：{next:yyyy-MM-dd HH:mm}（{task.Id} → {follow.Id}）");
        }
    }

    public void Snooze(string taskId, int minutes, bool notify = true)
    {
        var task = Repo.Get(taskId);
        if (task == null) return;

        RepeatFlow.Snooze(task, DateTime.Now, minutes, DeferKind.UserSnooze);
        Repo.Update(task);
        Scheduler.MarkHandled(task.Id);
        AppendHistory(task, "snooze");
        if (notify) Notify($"将在 {task.SnoozeUntil:HH:mm} 再次提醒");
        RefreshOpenForms();
    }

    private void Renew(RemindTask task)
    {
        // 三参数重载：避免建议一个过去的时刻
        var suggested = RepeatEngine.NextAfter(task.Repeat, task.PlanAnchor, DateTime.Now) ?? DateTime.Now.AddDays(1);
        using var dlg = new ReminderSetupForm(Config, task.Repeat, suggested);
        if (dlg.ShowDialog() != DialogResult.OK)
        {
            // C-8：取消设置不再静默顺延 5 分钟，改成明确文案
            Snooze(task.Id, Config.SnoozeMinutes, notify: false);
            Notify($"已取消设置，将在 {Config.SnoozeMinutes} 分钟后再次提醒");
            return;
        }

        var newAnchor = RepeatFlow.Renew(task, dlg.Repeat, dlg.TriggerAt, DateTime.Now);
        Repo.Update(task);
        Scheduler.MarkHandled(task.Id);
        AppendHistory(task, "renew");

        var follow = task.Clone();
        follow.Id = Repo.NewTaskId();
        follow.TriggerAt = newAnchor;                  // 用户新选时刻 = 新计划锚点
        follow.State = TaskState.Pending;
        follow.Repeat = task.Repeat.Clone();
        follow.Note = dlg.NoteText;
        follow.PreNotifyMinutes = dlg.PreNotifyMinutes;
        follow.SnoozeUntil = null;
        follow.DeferKind = DeferKind.None;
        follow.LastFiredAt = null;
        follow.PreNotifiedAt = null;
        follow.AckAt = null;
        Repo.Add(follow);
        Scheduler.Poke();

        Notify($"下一次提醒已设为 {dlg.TriggerAt:MM-dd HH:mm}（{RepeatEngine.Describe(dlg.Repeat)}）");
        RefreshOpenForms();
    }

    private void AppendHistory(RemindTask task, string action)
    {
        Repo.AppendHistory(new HistoryEntry
        {
            At = DateTime.Now,
            TaskId = task.Id,
            ChainId = task.ChainId,
            Image = task.Image,
            Action = action,
            TriggerAt = task.TriggerAt,
            Note = task.Note
        });
    }

    // ---------------- 窗口 ----------------

    private bool EnsureUnlocked()
    {
        if (!Config.HasPassword || _unlocked) return true;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var pwd = InputDialog.Prompt(null, "需要密码", attempt == 0 ? "请输入主界面密码：" : "密码不对，再试一次：", string.Empty, true);
            if (pwd == null) return false;
            if (SecurityService.Verify(pwd, Config.PasswordHash!, Config.PasswordSalt!))
            {
                _unlocked = true;
                // C-21：只补会话语义文案，不改变锁定行为
                if (!_unlockNotified)
                {
                    _unlockNotified = true;
                    Notify("本次运行已解锁主界面；退出程序后密码重新生效。", ToolTipIcon.Info);
                }
                return true;
            }
        }

        MessageBox.Show("密码连续错误，已取消打开。", "截图提醒", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    public void ShowTaskList()
    {
        if (!EnsureUnlocked()) return;

        if (_taskListForm == null || _taskListForm.IsDisposed)
        {
            _taskListForm = new TaskListForm(this);
            _taskListForm.FormClosed += (_, _) => _taskListForm = null;
        }
        _taskListForm.Show();
        if (_taskListForm.WindowState == FormWindowState.Minimized) _taskListForm.WindowState = FormWindowState.Normal;
        _taskListForm.Activate();
        _taskListForm.Reload();
    }

    public void ShowTaskListAndSelect(string taskId)
    {
        ShowTaskList();
        _taskListForm?.SelectTask(taskId);
    }

    public void ShowCalendar()
    {
        if (!EnsureUnlocked()) return;

        if (_calendarForm == null || _calendarForm.IsDisposed)
        {
            _calendarForm = new CalendarForm(this);
            _calendarForm.FormClosed += (_, _) => _calendarForm = null;
        }
        _calendarForm.Show();
        _calendarForm.Activate();
    }

    public void ShowSettings()
    {
        if (!EnsureUnlocked()) return;

        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(this);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.ShowDialog();
    }

    private void RefreshOpenForms()
    {
        if (_taskListForm is { IsDisposed: false, Visible: true }) _taskListForm.Reload();
        if (_calendarForm is { IsDisposed: false, Visible: true }) _calendarForm.RefreshCalendar();
    }

    /// <summary>从磁盘整体重载配置（C-10）：还原备份 / 换数据目录后调用。</summary>
    public void ReloadConfig()
    {
        Config = JsonStore.Load(AppPaths.ConfigFile, () => new AppConfig());
        _hotkey.Apply(Config.Hotkey, out var error);
        if (error != null) Log.Warn("重载配置后热键注册失败：" + error);
        if (_pauseItem != null) _pauseItem.Text = Scheduler.Paused ? "恢复全部提醒" : "暂停全部提醒";
    }

    public void ReloadAll()
    {
        ReloadConfig();
        Repo.Reload();
        Scheduler.MarkAllHandled();
        RefreshOpenForms();
    }

    // ---------------- 配置 ----------------

    public bool ApplyHotkeyTo(HotkeySetting setting, out string? error)
    {
        if (!_hotkey.Apply(setting, out error))
        {
            // 失败时把原热键装回去，避免「改坏了就没热键可用」
            _hotkey.Apply(Config.Hotkey, out _);
            return false;
        }

        Config.Hotkey = setting;
        SaveConfig();
        return true;
    }

    public void CommitConfig(AppConfig draft)
    {
        Config.PopupStrength = draft.PopupStrength;
        Config.CopyToClipboard = draft.CopyToClipboard;
        Config.AutoStart = draft.AutoStart;
        Config.QuietEnabled = draft.QuietEnabled;
        Config.QuietStart = draft.QuietStart;
        Config.QuietEnd = draft.QuietEnd;
        Config.DefaultPreNotifyMinutes = draft.DefaultPreNotifyMinutes;
        Config.SnoozeMinutes = draft.SnoozeMinutes;
        Config.OffWorkTime = draft.OffWorkTime;
        Config.PasswordHash = draft.PasswordHash;
        Config.PasswordSalt = draft.PasswordSalt;
        SaveConfig();
        Notify("设置已保存");
    }

    private void SaveConfig()
    {
        if (!JsonStore.Save(AppPaths.ConfigFile, Config))
            Notify("设置保存失败，本次更改未写入磁盘！请检查数据目录是否可写", ToolTipIcon.Error);
    }

    // ---------------- 退出 ----------------

    public void Shutdown()
    {
        try
        {
            _tray.Visible = false;
            _tray.Dispose();
            Scheduler.Dispose();
            _hotkey.Dispose();
            Log.Info("程序退出");
        }
        catch (Exception ex)
        {
            Log.Error("退出清理异常", ex);
        }
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            Scheduler.Dispose();
            _hotkey.Dispose();
        }
        base.Dispose(disposing);
    }
}
