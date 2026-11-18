using ScreenRemind.Core;
using ScreenRemind.Platform;

namespace ScreenRemind.UI;

public enum ShotOutcome { Discard, SaveOnly, SaveAndRemind }

public sealed class ShotPanelResult
{
    public ShotOutcome Outcome { get; init; }
    public Bitmap? FinalImage { get; init; }
    public Bitmap? OriginalImage { get; init; }
    public bool Annotated { get; init; }
    public DateTime TriggerAt { get; init; }
    public RepeatRule? Rule { get; init; }
    public string Note { get; init; } = string.Empty;
    public int PreNotifyMinutes { get; init; }
}

/// <summary>
/// 截完图后的处理面板：标注 / 设提醒 / 仅保存 / 复制 / 丢弃。
/// 面板只负责收集意图，落盘与建任务交给控制器，UI 不碰文件。
/// </summary>
public sealed class ShotPanelForm : Form
{
    private readonly AppConfig _config;
    private readonly Bitmap _original;
    private Bitmap _current;
    private bool _annotated;
    private bool _transferred;

    private readonly ImageViewer _viewer = new();
    private readonly Label _status = new();
    private readonly Button _annotateBtn;
    private readonly Button _copyBtn;
    private readonly Button _remindBtn;
    private readonly Button _saveBtn;
    private readonly Button _discardBtn;
    private Button _resetBtn = null!;

    public ShotPanelResult Result { get; private set; } = new() { Outcome = ShotOutcome.Discard };

    public ShotPanelForm(Bitmap image, AppConfig config)
    {
        _original = image;
        _current = image;
        _config = config;

        Theme.ApplyForm(this);
        Text = "截图完成";
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        MinimizeBox = false;
        KeyPreview = true;
        ClientSize = new Size(760, 660);

        var header = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Card, Padding = new Padding(18, 10, 18, 8) };
        header.Controls.Add(new Label
        {
            Text = "截图完成，接下来做什么？",
            Font = new Font(Theme.FontFamily, 12.5F, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(18, 10)
        });

        _status.Text = $"{image.Width} × {image.Height} 像素　无损 PNG";
        _status.ForeColor = Theme.SubText;
        _status.AutoSize = true;
        _status.Location = new Point(20, 36);
        header.Controls.Add(_status);

        var closeHint = new Label
        {
            Text = "关闭窗口 = 丢弃本次截图",
            AutoSize = true,
            ForeColor = Theme.SubText,
            Location = new Point(0, 14)
        };
        header.Controls.Add(closeHint);
        Theme.PinRight(closeHint, 18);

        var viewerHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Theme.Bg };
        _viewer.Dock = DockStyle.Fill;
        _viewer.ZoomChanged += (_, text) => _status.Text = $"{_current.Width} × {_current.Height} 像素　无损 PNG　　缩放 {text}";
        viewerHost.Controls.Add(_viewer);

        // ---- 快捷提醒：点一下直接排好，不再弹设置框 ----
        var quick = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Bg };

        quick.Controls.Add(new Label
        {
            Text = "快捷提醒",
            AutoSize = true,
            ForeColor = Theme.SubText,
            Location = new Point(36, 19)
        });

        var quickSpecs = new (string Text, Func<DateTime> Make)[]
        {
            ("1 小时后", () => DateTime.Now.AddHours(1)),
            ("3 小时后", () => DateTime.Now.AddHours(3)),
            ("24 小时后", () => DateTime.Now.AddHours(24)),
            ($"下班前 {_config.OffWorkTime}", OffWorkTrigger)
        };

        var qx = 110;
        foreach (var (text, make) in quickSpecs)
        {
            var b = Theme.GhostButton(text);
            b.SetBounds(qx, 12, 96, 32);
            b.Click += (_, _) => QuickRemind(make());
            quick.Controls.Add(b);
            qx += 100;
        }

        var quickHint = new Label
        {
            Text = "点一下就直接排好提醒",
            AutoSize = true,
            ForeColor = Theme.SubText,
            Font = Theme.Small,
            Location = new Point(qx + 6, 21)
        };
        quick.Controls.Add(quickHint);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 74, BackColor = Theme.Card, Padding = new Padding(16, 18, 16, 18) };

        _annotateBtn = Theme.GhostButton("框选");
        _annotateBtn.SetBounds(16, 18, 68, 38);
        _annotateBtn.Click += (_, _) => DoAnnotate(AnnotateForm.Tool.Rect);
        footer.Controls.Add(_annotateBtn);

        var arrowBtn = Theme.GhostButton("箭头");
        arrowBtn.SetBounds(92, 18, 68, 38);
        arrowBtn.Click += (_, _) => DoAnnotate(AnnotateForm.Tool.Arrow);
        footer.Controls.Add(arrowBtn);

        var freehandBtn = Theme.GhostButton("圈选");
        freehandBtn.SetBounds(168, 18, 68, 38);
        freehandBtn.Click += (_, _) => DoAnnotate(AnnotateForm.Tool.Freehand);
        footer.Controls.Add(freehandBtn);

        _copyBtn = Theme.GhostButton("复制");
        _copyBtn.SetBounds(244, 18, 68, 38);
        _copyBtn.Click += (_, _) =>
        {
            ScreenCapture.CopyToClipboard(_current);
            _status.Text = "已复制到剪贴板";
        };
        footer.Controls.Add(_copyBtn);

        _resetBtn = Theme.GhostButton("还原");
        _resetBtn.SetBounds(320, 18, 62, 38);
        _resetBtn.Enabled = false;
        _resetBtn.Click += (_, _) => ResetAnnotation();
        footer.Controls.Add(_resetBtn);

        _discardBtn = Theme.DangerButton("丢弃");
        _discardBtn.SetBounds(0, 18, 78, 38);
        _discardBtn.Click += (_, _) =>
        {
            // C-5：有标注时二次确认，避免误点丢失劳动成果
            if (_annotated && !AskDiscard()) return;
            Finish(new ShotPanelResult { Outcome = ShotOutcome.Discard });
        };
        footer.Controls.Add(_discardBtn);
        Theme.PinRight(_discardBtn, 22);

        _saveBtn = Theme.GhostButton("仅保存");
        _saveBtn.SetBounds(0, 18, 88, 38);
        _saveBtn.Click += (_, _) => Finish(new ShotPanelResult
        {
            Outcome = ShotOutcome.SaveOnly,
            FinalImage = _current,
            OriginalImage = _original,
            Annotated = _annotated
        });
        footer.Controls.Add(_saveBtn);
        Theme.PinRight(_saveBtn, 108);

        _remindBtn = Theme.PrimaryButton("设置提醒…");
        _remindBtn.SetBounds(0, 18, 120, 38);
        _remindBtn.Click += (_, _) => DoRemind();
        footer.Controls.Add(_remindBtn);
        Theme.PinRight(_remindBtn, 204);

        Controls.Add(viewerHost);
        Controls.Add(quick);
        Controls.Add(header);
        Controls.Add(footer);

        _viewer.SetImage(_current);

        if (_config.CopyToClipboard)
        {
            ScreenCapture.CopyToClipboard(_current);
            _status.Text = $"{image.Width} × {image.Height} 像素　已自动复制到剪贴板";
        }
    }

    private void DoAnnotate(AnnotateForm.Tool tool)
    {
        using var dlg = new AnnotateForm(_current, tool);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;

        if (!_annotated)
        {
            // 第一次标注：保留未标注原图用于另存 *.orig.png
            _annotated = true;
        }
        else if (!ReferenceEquals(_current, _original))
        {
            _current.Dispose();
        }

        _current = dlg.Result;
        _viewer.SetImage(_current);
        _resetBtn.Enabled = true;
    }

    /// <summary>把标注全部作废，退回未标注的原图（标错了的后悔药）。</summary>
    private void ResetAnnotation()
    {
        if (!_annotated) return;

        var old = _current;
        _current = _original;
        _annotated = false;

        if (!ReferenceEquals(old, _original))
        {
            old.Dispose();
        }

        _viewer.SetImage(_current);
        _resetBtn.Enabled = false;
        _status.Text = "已还原到未标注的原图";
    }

    /// <summary>一键排提醒：不弹设置框，直接把这张图挂到一个具体时刻上。</summary>
    private void QuickRemind(DateTime trigger)
    {
        var at = new DateTime(trigger.Year, trigger.Month, trigger.Day, trigger.Hour, trigger.Minute, 0);
        Finish(new ShotPanelResult
        {
            Outcome = ShotOutcome.SaveAndRemind,
            FinalImage = _current,
            OriginalImage = _original,
            Annotated = _annotated,
            TriggerAt = at,
            Rule = new RepeatRule { Type = RepeatType.None },
            Note = string.Empty,
            PreNotifyMinutes = 0
        });
    }

    /// <summary>「下班前」：取设置里的下班时刻；今天已过就顺延到明天同一时刻。</summary>
    private DateTime OffWorkTrigger()
    {
        var span = Scheduler.TryParseTime(_config.OffWorkTime, out var parsed)
            ? parsed
            : TimeSpan.FromHours(18);

        var today = DateTime.Today + span;
        return today > DateTime.Now ? today : today.AddDays(1);
    }

    private void DoRemind()
    {
        using var dlg = new ReminderSetupForm(_config);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        Finish(new ShotPanelResult
        {
            Outcome = ShotOutcome.SaveAndRemind,
            FinalImage = _current,
            OriginalImage = _original,
            Annotated = _annotated,
            TriggerAt = dlg.TriggerAt,
            Rule = dlg.Repeat,
            Note = dlg.NoteText,
            PreNotifyMinutes = dlg.PreNotifyMinutes
        });
    }

    private void Finish(ShotPanelResult result)
    {
        Result = result;
        // 交给控制器接管位图，本窗体不再释放
        _transferred = result.Outcome != ShotOutcome.Discard;
        DialogResult = DialogResult.OK;
        Close();
    }

    private bool AskDiscard() =>
        MessageBox.Show(this, "已经做了标注，丢弃后无法找回。确定丢弃这次截图吗？", "丢弃截图",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // C-5：直接关窗口/Alt+F4 都走这里。有标注时提供三个选择，而不是静默丢弃。
        if (DialogResult != DialogResult.OK && _annotated)
        {
            var choice = MessageBox.Show(this,
                "这次截图已经做了标注。\n\n「是」= 丢弃截图和标注\n「否」= 存到 shots 但不设提醒\n「取消」= 返回继续编辑",
                "要丢弃这次截图吗？", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

            if (choice == DialogResult.Cancel)
            {
                e.Cancel = true;
                base.OnFormClosing(e);
                return;
            }
            if (choice == DialogResult.No)
            {
                // 第三选项：留下图片，但不进入提醒流程
                Result = new ShotPanelResult
                {
                    Outcome = ShotOutcome.SaveOnly,
                    FinalImage = _current,
                    OriginalImage = _original,
                    Annotated = _annotated
                };
                _transferred = true;
                DialogResult = DialogResult.OK;
                base.OnFormClosing(e);
                return;
            }
        }

        // 直接关窗口 = 丢弃（没有主动选择保存，就不该在磁盘上留垃圾）
        if (DialogResult != DialogResult.OK)
        {
            Result = new ShotPanelResult { Outcome = ShotOutcome.Discard };
            _transferred = false;
        }
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Control && e.KeyCode == Keys.S) _saveBtn.PerformClick();
        else if (e.Control && e.KeyCode == Keys.R) _remindBtn.PerformClick();
        else if (e.Control && e.KeyCode == Keys.A) _annotateBtn.PerformClick();
        else if (e.Control && e.KeyCode == Keys.C) _copyBtn.PerformClick();
        else if (e.KeyCode == Keys.Delete) _discardBtn.PerformClick();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_transferred)
        {
            if (!ReferenceEquals(_current, _original)) _current?.Dispose();
            _original?.Dispose();
        }
        base.Dispose(disposing);
    }
}
