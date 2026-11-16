using System.Drawing.Imaging;
using ScreenRemind.Core;
using ScreenRemind.Platform;

namespace ScreenRemind.UI;

public enum ReminderAction { None, Acknowledged, Snoozed, Renew }

/// <summary>
/// 到点提醒弹窗：原图 1:1 显示（只在显示层缩放），三个出口。
/// 关闭按钮 = 稍后提醒，避免误点导致「提醒丢了」。遮罩强度下没有关闭按钮，必须选一个。
/// </summary>
public sealed class ReminderForm : Form
{
    private readonly RemindTask _task;
    private readonly PopupStrength _strength;
    private readonly ImageViewer _viewer = new();
    private readonly Label _zoomLabel = new();
    private readonly List<string> _chainImages;
    private int _chainIndex;
    private Bitmap? _image;

    public ReminderAction Action { get; private set; } = ReminderAction.None;
    public int SnoozeMinutes { get; private set; }
    /// <summary>「设置下一次」时由外部接管，弹窗自己关闭</summary>
    public bool RenewRequested { get; private set; }

    public ReminderForm(RemindTask task, PopupStrength strength, List<string>? chainImages = null)
    {
        _task = task;
        _strength = strength;
        _chainImages = chainImages ?? new List<string>();
        _chainIndex = Math.Max(0, _chainImages.IndexOf(task.Image));

        Theme.ApplyForm(this);
        Text = "该看这张图了";
        ShowInTaskbar = true;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;

        if (strength == PopupStrength.Mask)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            var screen = Screen.FromPoint(Cursor.Position);
            Bounds = screen.Bounds;
            BackColor = Theme.MaskBg;
            Padding = new Padding(28);
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            BackColor = Theme.Bg;
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            ClientSize = new Size(Math.Min(980, area.Width - 120), Math.Min(720, area.Height - 120));
            Padding = new Padding(0);
        }

        BuildUi();
        LoadImage(task.Image);
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Theme.Card, Padding = new Padding(20, 12, 20, 10) };

        var title = new Label
        {
            Text = "提醒时间到了，看一眼这张图",
            Font = new Font(Theme.FontFamily, 14F, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(20, 12)
        };
        header.Controls.Add(title);

        var days = (DateTime.Now.Date - _task.CreatedAt.Date).Days;
        var meta = $"触发于 {_task.TriggerAt:yyyy-MM-dd HH:mm}";
        if (days > 0) meta += $"　·　距截图已 {days} 天";

        var metaLabel = new Label
        {
            Text = meta,
            ForeColor = Theme.SubText,
            AutoSize = true,
            Location = new Point(22, 44)
        };
        header.Controls.Add(metaLabel);

        if (!string.IsNullOrWhiteSpace(_task.Note))
        {
            var note = new Label
            {
                Text = "备注：" + _task.Note,
                ForeColor = Theme.WarnText,
                BackColor = Theme.WarnBg,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(0, 14, 340, 48),
                Padding = new Padding(8, 0, 8, 0)
            };
            header.Controls.Add(note);
            Theme.PinRight(note, 120);
        }

        // 查看器（缩放提示做成查看区底部的一条状态栏，避免和备注、按钮抢位置）
        var viewerHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = _strength == PopupStrength.Mask ? Theme.MaskBg : Theme.Bg };

        _zoomLabel.Text = "原图原始像素显示　滚轮缩放 · 拖动平移 · 双击切 1:1 · R 旋转";
        _zoomLabel.Dock = DockStyle.Bottom;
        _zoomLabel.Height = 26;
        _zoomLabel.AutoSize = false;
        _zoomLabel.TextAlign = ContentAlignment.MiddleLeft;
        _zoomLabel.Padding = new Padding(6, 0, 0, 0);
        _zoomLabel.ForeColor = Color.FromArgb(170, 176, 188);
        _zoomLabel.BackColor = Theme.ViewerBg;

        _viewer.Dock = DockStyle.Fill;
        _viewer.ZoomChanged += (_, text) => _zoomLabel.Text = $"{text}　滚轮缩放 · 拖动平移 · 双击切 1:1 · R 旋转";
        _viewer.PreviousRequested += (_, _) => StepImage(-1);
        _viewer.NextRequested += (_, _) => StepImage(1);
        viewerHost.Controls.Add(_viewer);
        viewerHost.Controls.Add(_zoomLabel);

        // 底部操作
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 100, BackColor = Theme.Card, Padding = new Padding(16, 14, 16, 12) };

        var ack = Theme.PrimaryButton("已知晓 / 已完成");
        ack.Width = 160;
        ack.Height = 40;
        ack.BackColor = Theme.Success;
        ack.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 140, 60);
        ack.Location = new Point(20, 14);
        ack.Click += (_, _) =>
        {
            Action = ReminderAction.Acknowledged;
            DialogResult = DialogResult.OK;
            Close();
        };
        footer.Controls.Add(ack);

        var snooze = Theme.GhostButton("稍后提醒 ▾");
        snooze.Width = 130;
        snooze.Height = 40;
        snooze.Location = new Point(190, 14);
        snooze.Click += (_, _) => ShowSnoozeMenu(snooze);
        footer.Controls.Add(snooze);

        var renew = Theme.GhostButton("设置下一次提醒");
        renew.Width = 150;
        renew.Height = 40;
        renew.Location = new Point(330, 14);
        renew.Click += (_, _) =>
        {
            RenewRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        };
        footer.Controls.Add(renew);

        var openDir = new LinkLabel
        {
            Text = "打开原图所在文件夹",
            AutoSize = true,
            Location = new Point(0, 64),
            LinkColor = Theme.SubText
        };
        openDir.LinkClicked += (_, _) => OpenImageFolder();
        footer.Controls.Add(openDir);
        Theme.PinRight(openDir, 20);

        if (_chainImages.Count > 1)
        {
            var next = Theme.GhostButton("下一张 ▶");
            next.SetBounds(0, 14, 88, 40);
            next.Click += (_, _) => StepImage(1);
            footer.Controls.Add(next);
            Theme.PinRight(next, 190);

            var prev = Theme.GhostButton("◀ 上一张");
            prev.SetBounds(0, 14, 88, 40);
            prev.Click += (_, _) => StepImage(-1);
            footer.Controls.Add(prev);
            Theme.PinRight(prev, 286);
        }

        if (_strength == PopupStrength.Mask)
        {
            // 遮罩模式：整体铺满，内部卡片化
            Controls.Add(viewerHost);
            Controls.Add(header);
            Controls.Add(footer);
        }
        else
        {
            Controls.Add(viewerHost);
            Controls.Add(header);
            Controls.Add(footer);
        }
    }

    private void ShowSnoozeMenu(Control anchor)
    {
        var menu = new ContextMenuStrip { Font = Theme.Base };
        var presets = new[] { 5, 15, 30, 60 };
        foreach (var minutes in presets)
        {
            var item = new ToolStripMenuItem($"{minutes} 分钟后");
            item.Click += (_, _) => DoSnooze(minutes);
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        var custom = new ToolStripMenuItem("自定义分钟…");
        custom.Click += (_, _) =>
        {
            var input = InputDialog.Prompt(this, "稍后提醒", "多少分钟后再提醒我？", "45");
            if (input == null) return;
            if (!int.TryParse(input, out var minutes) || minutes < 1 || minutes > 10080)
            {
                MessageBox.Show(this, "请输入 1–10080 之间的分钟数。", "数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DoSnooze(minutes);
        };
        menu.Items.Add(custom);

        var pos = anchor.PointToScreen(new Point(0, anchor.Height));
        menu.Show(pos);
    }

    private void DoSnooze(int minutes)
    {
        SnoozeMinutes = minutes;
        Action = ReminderAction.Snoozed;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OpenImageFolder()
    {
        try
        {
            var path = AppPaths.Resolve(_task.Image);
            if (File.Exists(path))
            {
                NativeShell.ShowInFolder(path);
            }
            else
            {
                MessageBox.Show(this, "原图文件不存在了。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("打开文件夹失败：" + ex.Message);
        }
    }

    private void StepImage(int delta)
    {
        if (_chainImages.Count <= 1) return;
        _chainIndex = (_chainIndex + delta + _chainImages.Count) % _chainImages.Count;
        LoadImage(_chainImages[_chainIndex]);
    }

    private void LoadImage(string relativePath)
    {
        _image?.Dispose();
        _image = null;

        var path = AppPaths.Resolve(relativePath);
        try
        {
            if (File.Exists(path))
            {
                // 用 FileShare.ReadWrite 读，避免文件被其它程序占用时打不开
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var temp = Image.FromStream(fs, true, true);
                _image = new Bitmap(temp);
            }
            else
            {
                Log.Warn("提醒时原图不存在：" + path);
            }
        }
        catch (Exception ex)
        {
            Log.Error("加载原图失败：" + path, ex);
        }

        _viewer.SetImage(_image);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        BringToFront();
        _viewer.Focus();

        if (_strength == PopupStrength.Strong)
        {
            var info = new NativeMethods.FLASHWINFO
            {
                hwnd = Handle,
                dwFlags = NativeMethods.FLASHW_ALL | NativeMethods.FLASHW_TIMERNOFG,
                uCount = 8,
                dwTimeout = 0
            };
            info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(info);
            NativeMethods.FlashWindowEx(ref info);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 常规关闭 = 稍后提醒，杜绝「提醒被误关而丢失」
        if (Action == ReminderAction.None && !RenewRequested)
        {
            Action = ReminderAction.Snoozed;
            SnoozeMinutes = 15;
        }
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape && _strength != PopupStrength.Mask)
        {
            DoSnooze(15);
        }
        else if (e.KeyCode == Keys.Enter && e.Control)
        {
            Action = ReminderAction.Acknowledged;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>打开资源管理器并选中文件。UI 层唯一允许的 shell 调用。</summary>
internal static class NativeShell
{
    public static void ShowInFolder(string path)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);
    }

    public static void OpenFolder(string folder)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
    }

    public static void OpenFile(string path)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
    }
}
