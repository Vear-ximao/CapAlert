using ScreenRemind.Core;
using ScreenRemind.Platform;
using ScreenRemind.UI;

namespace ScreenRemind;

/// <summary>
/// 应用中枢：装配各服务、承载托盘、串起「截图 → 定提醒 → 到点弹窗 → 处理」的完整链路。
/// UI 只收集用户意图，落盘与状态流转都在这里做，避免逻辑散落在各个窗体的点击事件里。
/// </summary>
public sealed class AppController : ApplicationContext
{
    public AppConfig Config { get; private set; }
    public TaskRepository Repo { get; }
    public Scheduler Scheduler { get; }

    private readonly HotkeyManager _hotkey = new();
    private readonly NotifyIcon _tray;
    private readonly Queue<RemindTask> _queue = new();

    private bool _showingReminder;
    private bool _capturing;
    private bool _unlocked;

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

        _hotkey.Pressed += (_, _) => StartCapture();
        _hotkey.WakeRequested += (_, _) => ShowTaskList();

        _tray = BuildTray();
    }

    public void Start()
    {
        if (!ApplyHotkeyTo(Config.Hotkey, out var error))
        {
            Log.Warn("启动时快捷键注册失败：" + error);
            Notify("截图快捷键没能注册成功：" + error + "　请到「设置」里换一个组合", ToolTipIcon.Warning);
        }

        AutoStart.RepairIfStale();
        Repo.PruneDone();
        Log.Cleanup();

        Scheduler.Start();
        Log.Info($"启动完成，数据目录：{AppPaths.Root}");

        if (!Config.FirstRunDone)
        {
            Config.FirstRunDone = true;
            SaveConfig();
            Notify("已就绪。按 " + HotkeyText.Describe(Config.Hotkey) + " 截取屏幕区域；右键托盘图标可打开任务列表。", ToolTipIcon.Info);
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

        var pauseItem = new ToolStripMenuItem("暂停全部提醒");
        pauseItem.Click += (_, _) =>
        {
            Scheduler.Paused = !Scheduler.Paused;
            pauseItem.Text = Scheduler.Paused ? "恢复全部提醒" : "暂停全部提醒";
            Notify(Scheduler.Paused ? "已暂停全部提醒" : "已恢复提醒");
        };
        menu.Items.Add(pauseItem);

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

    private static Icon LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon != null) return icon;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("读取程序图标失败：" + ex.Message);
        }
        return SystemIcons.Application;
    }

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

    // ---------------- 截图 ----------------

    public void StartCapture()
    {
        if (_capturing) return;
        _capturing = true;
        var hidden = new List<Form>();

        try
        {
            // 抓屏前先把自己抢焦点的窗口藏起来，否则会截到自己
            foreach (Form form in Application.OpenForms)
            {
                if (form is { Visible: true } && form.ContainsFocus && form != _taskListForm)
                {
                    form.Hide();
                    hidden.Add(form);
                }
            }
            if (_taskListForm is { Visible: true, ContainsFocus: true })
            {
                _taskListForm.Hide();
                hidden.Add(_taskListForm);
            }

            Application.DoEvents();
            Thread.Sleep(150);

            // 先记下前台窗口矩形，等框选窗口盖上来之后就取不到了
            var windowRect = ScreenCapture.ForegroundWindowBounds();

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
            foreach (var form in hidden)
            {
                if (!form.IsDisposed) form.Show();
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
                    TriggerAt = result.TriggerAt,
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
                Notify($"已排定 {task.TriggerAt:MM-dd HH:mm} 提醒（{RepeatEngine.Describe(rule)}）", ToolTipIcon.Info);
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
            while (_queue.Count > 0)
            {
                var task = _queue.Dequeue();
                HandleOneReminder(task);
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

    private void HandleOneReminder(RemindTask task)
    {
        // 任务可能在排队期间已被删除或改期
        var live = Repo.Get(task.Id);
        if (live == null || live.State == TaskState.Done)
        {
            Scheduler.MarkHandled(task.Id);
            return;
        }

        var chain = Repo.ChainImages(live.ChainId);
        using var form = new ReminderForm(live, Config.PopupStrength, chain);
        form.ShowDialog();

        AppendHistory(live, "fire");

        switch (form.Action)
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
            default:
                Snooze(live.Id, Config.SnoozeMinutes);
                break;
        }

        RefreshOpenForms();
    }

    public void Acknowledge(RemindTask task)
    {
        var now = DateTime.Now;
        var rule = task.Repeat.Clone();
        rule.DoneCount += 1;

        task.State = TaskState.Done;
        task.AckAt = now;
        task.SnoozeUntil = null;
        task.Repeat = rule;
        Repo.Update(task);
        Scheduler.MarkHandled(task.Id);
        AppendHistory(task, "ack");

        // 重复规则：算出下一次并新建一条，原任务留档
        var next = RepeatEngine.NextAfter(rule, task.TriggerAt);
        if (next != null)
        {
            var follow = task.Clone();
            follow.Id = Repo.NewTaskId();
            follow.State = TaskState.Pending;
            follow.TriggerAt = next.Value;
            follow.AckAt = null;
            follow.SnoozeUntil = null;
            follow.PreNotifiedAt = null;
            follow.Repeat = rule.Clone();
            Repo.Add(follow);
            Log.Info($"重复提醒已排下一次：{next:yyyy-MM-dd HH:mm}（{task.Id} → {follow.Id}）");
        }
    }

    public void Snooze(string taskId, int minutes)
    {
        var task = Repo.Get(taskId);
        if (task == null) return;

        task.State = TaskState.Snoozed;
        task.SnoozeUntil = DateTime.Now.AddMinutes(Math.Clamp(minutes, 1, 10080));
        Repo.Update(task);
        Scheduler.MarkHandled(task.Id);
        AppendHistory(task, "snooze");
        Notify($"将在 {task.SnoozeUntil:HH:mm} 再次提醒");
        RefreshOpenForms();
    }

    private void Renew(RemindTask task)
    {
        var suggested = RepeatEngine.NextAfter(task.Repeat, task.TriggerAt) ?? DateTime.Now.AddDays(1);
        using var dlg = new ReminderSetupForm(Config, task.Repeat, suggested);
        if (dlg.ShowDialog() != DialogResult.OK)
        {
            // 用户按了取消：别把这次提醒弄丢，先顺延 5 分钟
            Snooze(task.Id, 5);
            return;
        }

        task.State = TaskState.Done;
        task.AckAt = DateTime.Now;
        task.SnoozeUntil = null;
        Repo.Update(task);
        Scheduler.MarkHandled(task.Id);
        AppendHistory(task, "renew");

        var follow = task.Clone();
        follow.Id = Repo.NewTaskId();
        follow.State = TaskState.Pending;
        follow.TriggerAt = dlg.TriggerAt;
        follow.Repeat = dlg.Repeat;
        follow.Note = dlg.NoteText;
        follow.PreNotifyMinutes = dlg.PreNotifyMinutes;
        follow.AckAt = null;
        follow.SnoozeUntil = null;
        follow.PreNotifiedAt = null;
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

    public void ReloadAll()
    {
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

    private void SaveConfig() => JsonStore.Save(AppPaths.ConfigFile, Config);

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
