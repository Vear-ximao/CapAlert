using ScreenRemind.Core;
using ScreenRemind.Platform;

namespace ScreenRemind.UI;

/// <summary>
/// 设置：快捷键录制、弹窗强度、免打扰、剪贴板、自启、密码、数据位置。
/// 快捷键不再是「模式二选一」，而是直接录一串按键，内部自动挑注册方式。
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppController _app;
    private readonly AppConfig _draft;

    private readonly Label _hotkeyLabel = new();
    private readonly Button _recordBtn;
    private readonly Label _recordHint = new();
    private readonly List<uint> _recordKeys = new();
    private bool _recording;

    private readonly RadioButton _rNormal = new();
    private readonly RadioButton _rStrong = new();
    private readonly RadioButton _rMask = new();

    private readonly CheckBox _quietEnabled = new();
    private readonly TextBox _quietStart = new();
    private readonly TextBox _quietEnd = new();

    private readonly CheckBox _clipboard = new();
    private readonly NumericUpDown _preNotify = new();
    private readonly NumericUpDown _snooze = new();
    private readonly TextBox _offWork = new();
    private readonly CheckBox _autoStart = new();
    private readonly Button _passwordBtn;
    private readonly Label _dataPathLabel = new();

    private readonly Panel _content = new();
    private UiZoom? _zoom;
    private bool _autoFit = true;
    private Size _autoClient;

    public SettingsForm(AppController app)
    {
        _app = app;

        // 在副本上改，取消即丢弃
        _draft = new AppConfig
        {
            Version = app.Config.Version,
            Hotkey = new HotkeySetting { Keys = new List<uint>(app.Config.Hotkey.EffectiveKeys()) },
            PopupStrength = app.Config.PopupStrength,
            CopyToClipboard = app.Config.CopyToClipboard,
            AutoStart = app.Config.AutoStart,
            QuietEnabled = app.Config.QuietEnabled,
            QuietStart = app.Config.QuietStart,
            QuietEnd = app.Config.QuietEnd,
            DefaultPreNotifyMinutes = app.Config.DefaultPreNotifyMinutes,
            SnoozeMinutes = app.Config.SnoozeMinutes,
            OffWorkTime = app.Config.OffWorkTime,
            PasswordHash = app.Config.PasswordHash,
            PasswordSalt = app.Config.PasswordSalt
        };

        Theme.ApplyForm(this);
        Text = "截图提醒 · 设置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        var y = 16;

        // ---------------- 快捷键 ----------------
        AddSection("截图快捷键", ref y);

        _hotkeyLabel.SetBounds(28, y, 300, 30);
        _hotkeyLabel.Font = new Font(Theme.FontFamily, 14F, FontStyle.Bold);
        _hotkeyLabel.ForeColor = Theme.Accent;
        Controls.Add(_hotkeyLabel);

        _recordBtn = Theme.PrimaryButton("录制新快捷键");
        _recordBtn.SetBounds(348, y - 2, 140, 34);
        _recordBtn.Click += (_, _) =>
        {
            if (_recording) FinishRecording();
            else StartRecording();
        };
        Controls.Add(_recordBtn);

        _recordHint.SetBounds(28, y + 38, 540, 44);
        _recordHint.ForeColor = Theme.SubText;
        _recordHint.AutoSize = false;
        Controls.Add(_recordHint);
        y += 92;

        // ---------------- 弹窗强度 ----------------
        AddSection("提醒弹窗强度", ref y);
        _rNormal.SetBounds(28, y, 420, 24);
        _rNormal.Text = "普通置顶（默认，不打断全屏）";
        _rStrong.SetBounds(28, y + 26, 420, 24);
        _rStrong.Text = "强制置顶 + 任务栏闪烁";
        _rMask.SetBounds(28, y + 52, 480, 24);
        _rMask.Text = "全屏遮罩（必须处理才消失，最不容易漏看）";
        Controls.AddRange(new Control[] { _rNormal, _rStrong, _rMask });
        switch (_draft.PopupStrength)
        {
            case PopupStrength.Mask: _rMask.Checked = true; break;
            case PopupStrength.Strong: _rStrong.Checked = true; break;
            default: _rNormal.Checked = true; break;
        }
        y += 84;

        // ---------------- 免打扰 ----------------
        AddSection("免打扰时段", ref y);
        _quietEnabled.SetBounds(28, y, 280, 24);
        _quietEnabled.Text = "启用（时段内到点自动顺延到结束）";
        _quietEnabled.Checked = _draft.QuietEnabled;
        Controls.Add(_quietEnabled);

        _quietStart.SetBounds(316, y, 70, 24);
        _quietStart.Text = _draft.QuietStart;
        _quietEnd.SetBounds(412, y, 70, 24);
        _quietEnd.Text = _draft.QuietEnd;
        Controls.Add(_quietStart);
        Controls.Add(new Label { Text = "至", AutoSize = true, Location = new Point(394, y + 3) });
        Controls.Add(_quietEnd);
        Controls.Add(new Label { Text = "（24 小时制）", AutoSize = true, ForeColor = Theme.SubText, Location = new Point(490, y + 3) });
        y += 42;

        // ---------------- 其他 ----------------
        AddSection("其他", ref y);
        _clipboard.SetBounds(28, y, 300, 24);
        _clipboard.Text = "截图后自动复制到剪贴板";
        _clipboard.Checked = _draft.CopyToClipboard;
        Controls.Add(_clipboard);

        Controls.Add(new Label { Text = "默认提前预提醒", AutoSize = true, Location = new Point(28, y + 32) });
        _preNotify.SetBounds(150, y + 28, 70, 24);
        _preNotify.Minimum = 0;
        _preNotify.Maximum = 1440;
        _preNotify.Value = Math.Clamp(_draft.DefaultPreNotifyMinutes, 0, 1440);
        Controls.Add(_preNotify);
        Controls.Add(new Label { Text = "分钟（0 = 不预提醒）", AutoSize = true, ForeColor = Theme.SubText, Location = new Point(228, y + 32) });

        Controls.Add(new Label { Text = "关闭弹窗默认顺延", AutoSize = true, Location = new Point(28, y + 64) });
        _snooze.SetBounds(150, y + 60, 70, 24);
        _snooze.Minimum = 1;
        _snooze.Maximum = 1440;
        _snooze.Value = Math.Clamp(_draft.SnoozeMinutes, 1, 1440);
        Controls.Add(_snooze);
        Controls.Add(new Label { Text = "分钟", AutoSize = true, ForeColor = Theme.SubText, Location = new Point(228, y + 64) });

        Controls.Add(new Label { Text = "下班时间", AutoSize = true, Location = new Point(28, y + 96) });
        _offWork.SetBounds(150, y + 92, 70, 24);
        _offWork.Text = _draft.OffWorkTime;
        Controls.Add(_offWork);
        Controls.Add(new Label
        {
            Text = "（截图面板「下班前提醒」用这个时刻，24 小时制）",
            AutoSize = true,
            ForeColor = Theme.SubText,
            Location = new Point(228, y + 96)
        });
        y += 128;

        // ---------------- 系统 ----------------
        AddSection("系统", ref y);
        _autoStart.SetBounds(28, y, 320, 24);
        _autoStart.Text = "开机自动启动（在启动文件夹放快捷方式）";
        _autoStart.Checked = AutoStart.Exists;
        Controls.Add(_autoStart);

        _passwordBtn = Theme.GhostButton(_draft.HasPassword ? "修改/清除密码" : "设置主界面密码");
        _passwordBtn.SetBounds(28, y + 30, 160, 32);
        _passwordBtn.Click += (_, _) => ManagePassword();
        Controls.Add(_passwordBtn);
        Controls.Add(new Label
        {
            Text = "只锁界面，不加密 shots 目录里的图片文件",
            AutoSize = true,
            ForeColor = Theme.SubText,
            Location = new Point(200, y + 38)
        });
        y += 76;

        // ---------------- 数据 ----------------
        AddSection("数据", ref y);

        var openData = Theme.GhostButton("打开数据目录");
        openData.SetBounds(28, y, 120, 32);
        openData.Click += (_, _) => NativeShell.OpenFolder(AppPaths.Root);
        Controls.Add(openData);

        var backup = Theme.GhostButton("立即备份");
        backup.SetBounds(156, y, 100, 32);
        backup.Click += (_, _) => DoBackup();
        Controls.Add(backup);

        var restore = Theme.GhostButton("从备份还原…");
        restore.SetBounds(264, y, 130, 32);
        restore.Click += (_, _) => RestoreBackup();
        Controls.Add(restore);
        y += 40;

        Controls.Add(new Label { Text = "数据位置", AutoSize = true, ForeColor = Theme.Text, Location = new Point(28, y + 11) });
        _dataPathLabel.SetBounds(96, y + 8, 360, 22);
        _dataPathLabel.ForeColor = Theme.SubText;
        _dataPathLabel.AutoSize = false;
        _dataPathLabel.AutoEllipsis = true;
        Controls.Add(_dataPathLabel);

        var changeDir = Theme.GhostButton("更改位置…");
        changeDir.SetBounds(464, y + 6, 100, 30);
        changeDir.Click += (_, _) => ChangeDataLocation();
        Controls.Add(changeDir);
        y += 44;

        // ---------------- 底部按钮 ----------------
        var ok = Theme.PrimaryButton("保存");
        ok.SetBounds(384, y, 84, 36);
        ok.Click += (_, _) => Save();
        Controls.Add(ok);

        var cancel = Theme.GhostButton("取消");
        cancel.SetBounds(476, y, 84, 36);
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.Add(cancel);

        var hint = new Label
        {
            Text = "小提示：拖动窗口边缘可以把界面放大",
            AutoSize = true,
            ForeColor = Theme.SubText,
            Font = Theme.Small,
            Location = new Point(28, y + 12)
        };
        Controls.Add(hint);

        ClientSize = new Size(580, y + 56);

        AcceptButton = ok;
        CancelButton = cancel;

        UpdateHotkeyLabel();
        _dataPathLabel.Text = AppPaths.Root;

        // 把所有控件搬进一个内容面板，这样才能整体等比缩放（搬家不改变相对坐标）
        _content.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
        _content.BackColor = Theme.Bg;
        var children = Controls.Cast<Control>().ToList();
        Controls.Clear();
        foreach (var child in children) _content.Controls.Add(child);
        Controls.Add(_content);

        _zoom = new UiZoom(this, _content);
        _zoom.Capture();

        MinimumSize = new Size(Size.Width + 40, Size.Height + 40);
        _autoClient = ClientSize;
        Resize += (_, _) =>
        {
            if (_autoFit && ClientSize == _autoClient) return;
            _autoFit = false;
            _zoom?.FitToWindow();
        };
    }

    private void AddSection(string title, ref int y)
    {
        var label = new Label
        {
            Text = title,
            Font = Theme.Subtitle,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(20, y)
        };
        Controls.Add(label);

        var line = new Panel
        {
            BackColor = Theme.Border,
            Bounds = new Rectangle(20, y + 24, 540, 1)
        };
        Controls.Add(line);
        y += 36;
    }

    // ---------------- 快捷键录制 ----------------

    private void UpdateHotkeyLabel()
    {
        _hotkeyLabel.Text = HotkeyText.Describe(_draft.Hotkey);

        if (_recording) return;

        var keys = _draft.Hotkey.EffectiveKeys();
        var engine = HotkeyManager.ChooseEngine(keys);
        if (engine == HotkeyEngine.RegisterHotKey)
        {
            _recordHint.ForeColor = Theme.SubText;
            _recordHint.Text = "当前组合走系统注册，最稳，保存前会自动检测是否被其他程序占用。";
        }
        else
        {
            _recordHint.ForeColor = Theme.WarnText;
            _recordHint.Text = "当前组合需要键盘钩子：个别游戏反作弊 / 安全软件可能拦截，且无法事前判断是否被占用。\n" +
                               "想更稳就换成「Ctrl 或 Alt + 一个字母」这种形式。";
        }
    }

    private void StartRecording()
    {
        _recording = true;
        _recordKeys.Clear();
        _recordBtn.Text = "完成录制";
        _recordHint.ForeColor = Theme.Accent;
        _recordHint.Text = "请依次按下要用的按键（最多 4 个，含 Ctrl/Alt/Shift）。\n按完点「完成录制」或按回车确认；按 Esc 取消。";
        RefreshRecordingLabel();
        Focus();
        Activate();
    }

    private void RefreshRecordingLabel()
    {
        if (!_recording) return;
        _hotkeyLabel.Text = _recordKeys.Count == 0
            ? "请按键…"
            : HotkeyText.Describe(_recordKeys) + $"　（已录 {_recordKeys.Count} 个）";
    }

    private void CancelRecording()
    {
        _recording = false;
        _recordKeys.Clear();
        _recordBtn.Text = "录制新快捷键";
        UpdateHotkeyLabel();
    }

    private void FinishRecording()
    {
        if (_recordKeys.Count < 2)
        {
            MessageBox.Show(this, "至少要两个键。\n例如：Ctrl+Alt、Ctrl+Alt+S、A+S、Ctrl+Shift+F1。", "按键太少",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var engine = HotkeyManager.ChooseEngine(_recordKeys);
        if (!HotkeyManager.Probe(_recordKeys, out _))
        {
            MessageBox.Show(this, "这个组合已经被其他程序占用了，换一个试试。", "快捷键冲突",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _draft.Hotkey = new HotkeySetting { Keys = new List<uint>(_recordKeys) };
        _draft.Hotkey.SyncLegacyFields();

        _recording = false;
        _recordKeys.Clear();
        _recordBtn.Text = "录制新快捷键";
        UpdateHotkeyLabel();

        if (engine == HotkeyEngine.KeyboardHook)
        {
            MessageBox.Show(this,
                "这个组合需要走键盘钩子（不是系统注册）。\n\n" +
                "· 个别游戏反作弊或安全软件可能拦截；\n" +
                "· 无法事前判断是否已被其他程序占用；\n" +
                "· 它会被前台程序同时收到，介意的话换成 Ctrl/Alt + 单个字母。\n\n" +
                "点「保存」后生效。",
                "这个组合的代价", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnKeyDown(e);
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode == Keys.Escape)
        {
            CancelRecording();
            return;
        }
        if (e.KeyCode == Keys.Enter)
        {
            FinishRecording();
            return;
        }

        if (_recordKeys.Count >= 4) return;

        // 先把当时按住的修饰键记下来（Alt 单独按下的 KeyDown 有时拿不到）
        var modifiers = ModifierKeys;
        if ((modifiers & Keys.Control) != 0) AddRecordKey(HotkeyKeys.Control);
        if ((modifiers & Keys.Alt) != 0) AddRecordKey(HotkeyKeys.Alt);
        if ((modifiers & Keys.Shift) != 0) AddRecordKey(HotkeyKeys.Shift);

        var vk = HotkeyKeys.Normalize((uint)e.KeyCode);
        if (HotkeyKeys.IsModifier(vk) || HotkeyText.IsRecordable(vk))
        {
            AddRecordKey(vk);
        }

        RefreshRecordingLabel();
    }

    private void AddRecordKey(uint vk)
    {
        if (vk == 0 || _recordKeys.Contains(vk) || _recordKeys.Count >= 4) return;
        _recordKeys.Add(vk);
    }

    // ---------------- 密码 ----------------

    private void ManagePassword()
    {
        if (_draft.HasPassword)
        {
            var action = MessageBox.Show(this, "已有密码。\n\n「是」= 修改密码　「否」= 清除密码", "主界面密码",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (action == DialogResult.Cancel) return;

            if (action == DialogResult.No)
            {
                var current = InputDialog.Prompt(this, "清除密码", "请输入当前密码：", string.Empty, true);
                if (current == null) return;
                if (!SecurityService.Verify(current, _draft.PasswordHash!, _draft.PasswordSalt!))
                {
                    MessageBox.Show(this, "密码不正确。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _draft.PasswordHash = null;
                _draft.PasswordSalt = null;
                MessageBox.Show(this, "密码已清除，保存后生效。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        var pwd = InputDialog.Prompt(this, "设置密码", "输入新密码（建议 4 位以上）：", string.Empty, true);
        if (string.IsNullOrEmpty(pwd)) return;
        var again = InputDialog.Prompt(this, "设置密码", "再输入一次确认：", string.Empty, true);
        if (again != pwd)
        {
            MessageBox.Show(this, "两次输入不一致。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var (hash, salt) = SecurityService.Create(pwd);
        _draft.PasswordHash = hash;
        _draft.PasswordSalt = salt;
        MessageBox.Show(this, "密码已设置，保存后生效。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---------------- 数据 ----------------

    private void DoBackup()
    {
        try
        {
            var path = BackupService.CreateBackup(out var files, out var bytes);
            MessageBox.Show(this, $"已备份到：\n{path}\n{files} 个文件，{bytes / 1024.0 / 1024.0:F1} MB",
                "备份完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "备份失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestoreBackup()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择备份包",
            InitialDirectory = AppPaths.Backups,
            Filter = "备份包 (*.zip)|*.zip"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (MessageBox.Show(this, "还原会覆盖当前任务与同名图片，确定继续？", "还原备份",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

        var count = BackupService.Restore(dlg.FileName, out var error);
        if (error != null)
        {
            MessageBox.Show(this, "还原失败：" + error, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _app.ReloadAll();
        MessageBox.Show(this, $"已还原 {count} 个文件。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ChangeDataLocation()
    {
        using var dlg = new DataLocationForm(false, AppPaths.Root);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var target = dlg.SelectedPath;
        if (string.Equals(Path.GetFullPath(target).TrimEnd('\\'),
                Path.GetFullPath(AppPaths.Root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "和当前位置是同一个文件夹。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var size = LegacyMigration.CountFiles(AppPaths.Data, AppPaths.Shots, AppPaths.Backups);
        if (MessageBox.Show(this,
                $"会把现有 {size} 个文件复制到：\n{target}\n\n原位置的文件不会删除。继续？",
                "更改数据位置", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            var copied = LegacyMigration.CopyFrom(AppPaths.Root, target);
            AppPaths.SetRoot(target);
            AppPaths.EnsureAll();
            _dataPathLabel.Text = AppPaths.Root;
            _app.ReloadAll();
            MessageBox.Show(this,
                $"已切换，复制了 {copied} 个文件。\n\n新位置：{AppPaths.Root}\n\n原位置的文件保留未删，确认无误后可自行清理。",
                "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "更改位置失败：" + ex.Message + "\n\n当前数据目录没有改变。", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    // ---------------- 保存 ----------------

    private void Save()
    {
        if (_recording)
        {
            MessageBox.Show(this, "快捷键还在录制中，先点「完成录制」。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!Scheduler.TryParseTime(_quietStart.Text, out _) || !Scheduler.TryParseTime(_quietEnd.Text, out _))
        {
            MessageBox.Show(this, "免打扰时间格式不对，应形如 23:00。", "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!Scheduler.TryParseTime(_offWork.Text, out _))
        {
            MessageBox.Show(this, "下班时间格式不对，应形如 18:00。", "格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _draft.CopyToClipboard = _clipboard.Checked;
        _draft.QuietEnabled = _quietEnabled.Checked;
        _draft.QuietStart = _quietStart.Text.Trim();
        _draft.QuietEnd = _quietEnd.Text.Trim();
        _draft.OffWorkTime = _offWork.Text.Trim();
        _draft.DefaultPreNotifyMinutes = (int)_preNotify.Value;
        _draft.SnoozeMinutes = (int)_snooze.Value;
        _draft.PopupStrength = _rMask.Checked ? PopupStrength.Mask
            : _rStrong.Checked ? PopupStrength.Strong : PopupStrength.Normal;

        // 热键是真的要生效的动作，放最后；失败就整体不保存
        if (!_app.ApplyHotkeyTo(_draft.Hotkey, out var hotkeyError))
        {
            MessageBox.Show(this, hotkeyError ?? "快捷键设置失败", "快捷键", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var wantAuto = _autoStart.Checked;
        var currentlyOn = AutoStart.Exists;
        if (wantAuto && !currentlyOn)
        {
            if (!AutoStart.Enable(out var enableError))
            {
                ShowError(enableError ?? "开启开机自启失败");
                wantAuto = false;
            }
        }
        else if (!wantAuto && currentlyOn)
        {
            if (!AutoStart.Disable(out var disableError))
            {
                ShowError(disableError ?? "关闭开机自启失败");
                wantAuto = true;
            }
        }
        _draft.AutoStart = wantAuto;

        _app.CommitConfig(_draft);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
