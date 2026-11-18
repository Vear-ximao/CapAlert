using ScreenRemind.Core;

namespace ScreenRemind.UI;

/// <summary>
/// 设置提醒：相对/指定时间 + 6 种重复规则 + 结束条件 + 预提醒 + 备注。
/// 版面按「从上到下顺序堆叠」实时计算：不重复时结束条件整行隐藏、重复选项区高度按类型收缩，
/// 不会留一大片空白。窗口可拖动放大，内容整体等比缩放。
/// </summary>
public sealed class ReminderSetupForm : Form
{
    private const int DesignWidth = 520;
    private const int Pad = 20;
    private const int InnerWidth = DesignWidth - Pad * 2;

    private readonly AppConfig _config;
    private readonly DateTime _defaultNext;

    private readonly Panel _content = new();
    private UiZoom? _zoom;
    private bool _autoFit = true;
    private bool _suppressResize;
    private Size _autoClient;

    private readonly Label _lblTime = new();
    private readonly TabControl _tabs = new();
    private readonly NumericUpDown _numRelative = new();
    private readonly ComboBox _unitRelative = new();
    private readonly DateTimePicker _absolute = new();

    private readonly Label _lblRepeat = new();
    private readonly ComboBox _repeatType = new();
    private readonly Panel _repeatHost = new();

    private readonly Label _lblEnd = new();
    private readonly ComboBox _endType = new();
    private readonly DateTimePicker _untilPicker = new();
    private readonly NumericUpDown _countNum = new();

    private readonly Label _lblPre = new();
    private readonly NumericUpDown _preNotify = new();
    private readonly Label _lblPreHint = new();

    private readonly Label _lblNote = new();
    private readonly TextBox _note = new();
    private readonly Label _preview = new();
    private readonly Label _zoomHint = new();

    private readonly Button _ok = Theme.PrimaryButton("确定");
    private readonly Button _cancel = Theme.GhostButton("取消");

    private readonly Panel _pNone = new();
    private readonly Panel _pInterval = new();
    private readonly Panel _pDaily = new();
    private readonly Panel _pWeekly = new();
    private readonly Panel _pMonthly = new();
    private readonly Panel _pYearMonths = new();

    private readonly NumericUpDown _intervalValue = new();
    private readonly ComboBox _intervalUnit = new();
    private readonly CheckBox[] _weekdayBoxes = new CheckBox[7];
    private readonly NumericUpDown _monthDay = new();
    private readonly CheckBox[] _monthBoxes = new CheckBox[12];
    private readonly NumericUpDown _yearMonthDay = new();

    public DateTime TriggerAt { get; private set; }
    public RepeatRule Repeat { get; private set; } = new();
    public string NoteText => _note.Text.Trim();
    public int PreNotifyMinutes => (int)_preNotify.Value;

    /// <param name="config">当前配置（用于预提醒默认值）</param>
    /// <param name="presetRule">沿用上一次的重复规则（设下一次时传入）</param>
    /// <param name="presetTrigger">预设触发时间；null 表示「10 分钟后」</param>
    public ReminderSetupForm(AppConfig config, RepeatRule? presetRule = null, DateTime? presetTrigger = null)
    {
        _config = config;
        _defaultNext = presetTrigger ?? DateTime.Now.AddMinutes(10);

        Theme.ApplyForm(this);
        Text = presetRule == null ? "设置提醒" : "设置下一次提醒";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Bg;

        _content.BackColor = Theme.Bg;
        _content.Bounds = new Rectangle(0, 0, DesignWidth, 560);
        Controls.Add(_content);

        BuildTimeSection();
        BuildRepeatSection();
        BuildEndSection();
        BuildMiscSection();
        BuildButtons();

        ApplyPreset(presetRule, presetTrigger);

        _zoom = new UiZoom(this, _content);
        Relayout();
        UpdatePreview();

        // 窗口尺寸跟随内容；一旦用户自己拉过窗口，就交给 UiZoom 做等比缩放
        Resize += (_, _) =>
        {
            if (_suppressResize) return;
            if (_autoFit && ClientSize == _autoClient) return;
            _autoFit = false;
            _zoom?.FitToWindow();
        };
    }

    // ---------------- 时间 ----------------

    private void BuildTimeSection()
    {
        _lblTime.Text = "提醒时间";
        _lblTime.Font = Theme.Subtitle;
        _lblTime.ForeColor = Theme.Text;
        _lblTime.AutoSize = true;
        _content.Controls.Add(_lblTime);

        _tabs.Font = Theme.Base;

        var pageRelative = new TabPage("多少分钟 / 小时后") { BackColor = Theme.Card };
        var pageAbsolute = new TabPage("指定具体时间") { BackColor = Theme.Card };

        _numRelative.Minimum = 1;
        _numRelative.Maximum = 99999;
        _numRelative.Value = 10;
        _numRelative.Font = new Font(Theme.FontFamily, 12F);
        _numRelative.ValueChanged += (_, _) => UpdatePreview();

        _unitRelative.DropDownStyle = ComboBoxStyle.DropDownList;
        _unitRelative.Items.AddRange(new object[] { "分钟", "小时" });
        _unitRelative.SelectedIndex = 0;
        _unitRelative.SelectedIndexChanged += (_, _) => UpdatePreview();

        pageRelative.Controls.Add(_numRelative);
        pageRelative.Controls.Add(_unitRelative);

        var quick = new[] { ("+5 分钟", 5, 0), ("+15 分钟", 15, 0), ("+30 分钟", 30, 0), ("+1 小时", 1, 1), ("+3 小时", 3, 1) };
        var qx = 20;
        foreach (var (text, value, unit) in quick)
        {
            var b = Theme.GhostButton(text);
            b.SetBounds(qx, 62, 84, 30);
            b.Click += (_, _) =>
            {
                _tabs.SelectedIndex = 0;
                _numRelative.Value = value;
                _unitRelative.SelectedIndex = unit;
                UpdatePreview();
            };
            pageRelative.Controls.Add(b);
            qx += 92;
        }

        pageRelative.Controls.Add(new Label
        {
            Text = "到点后按下面的「重复」规则继续",
            ForeColor = Theme.SubText,
            AutoSize = true,
            Location = new Point(20, 100)
        });

        _numRelative.SetBounds(20, 20, 92, 30);
        _unitRelative.SetBounds(120, 20, 86, 30);

        _absolute.Format = DateTimePickerFormat.Custom;
        _absolute.CustomFormat = "yyyy-MM-dd  HH:mm";
        _absolute.ShowUpDown = false;
        _absolute.Font = new Font(Theme.FontFamily, 11F);
        _absolute.Value = _defaultNext > DateTime.Now ? _defaultNext : DateTime.Now.AddMinutes(10);
        _absolute.ValueChanged += (_, _) => UpdatePreview();
        _absolute.SetBounds(20, 20, 250, 30);
        pageAbsolute.Controls.Add(_absolute);

        pageAbsolute.Controls.Add(new Label
        {
            Text = "可点日历选日期，时刻用键盘改（24 小时制，精确到分）",
            ForeColor = Theme.SubText,
            AutoSize = true,
            Location = new Point(20, 56)
        });

        var tomorrow = Theme.GhostButton("明天此时");
        tomorrow.SetBounds(20, 84, 96, 30);
        tomorrow.Click += (_, _) =>
        {
            _tabs.SelectedIndex = 1;
            _absolute.Value = DateTime.Now.AddDays(1);
            UpdatePreview();
        };
        pageAbsolute.Controls.Add(tomorrow);

        var tonight = Theme.GhostButton("今天 20:00");
        tonight.SetBounds(124, 84, 100, 30);
        tonight.Click += (_, _) =>
        {
            _tabs.SelectedIndex = 1;
            var t = DateTime.Today.AddHours(20);
            _absolute.Value = t > DateTime.Now ? t : t.AddDays(1);
            UpdatePreview();
        };
        pageAbsolute.Controls.Add(tonight);

        _tabs.TabPages.Add(pageRelative);
        _tabs.TabPages.Add(pageAbsolute);
        _tabs.SelectedIndexChanged += (_, _) => UpdatePreview();
        _content.Controls.Add(_tabs);
    }

    // ---------------- 重复 ----------------

    private void BuildRepeatSection()
    {
        _lblRepeat.Text = "重复";
        _lblRepeat.Font = Theme.Subtitle;
        _lblRepeat.ForeColor = Theme.Text;
        _lblRepeat.AutoSize = true;
        _content.Controls.Add(_lblRepeat);

        _repeatType.DropDownStyle = ComboBoxStyle.DropDownList;
        _repeatType.Items.AddRange(new object[]
        {
            "不重复（只提醒这一次）",
            "每隔 N 分钟 / 小时 / 天",
            "每天",
            "每周（选星期几）",
            "每月（选几号）",
            "指定月份（每年哪几个月）"
        });
        _repeatType.SelectedIndex = 0;
        _repeatType.SelectedIndexChanged += (_, _) =>
        {
            ApplyRepeatVisibility();
            Relayout();
            UpdatePreview();
        };
        _content.Controls.Add(_repeatType);

        // ---- 不重复 ----
        _pNone.Controls.Add(new Label
        {
            Text = "到点提醒一次，点「已知晓」后这条就结束了。",
            ForeColor = Theme.SubText,
            AutoSize = true,
            Location = new Point(2, 4)
        });

        // ---- 每隔 N ----
        _intervalValue.Minimum = 1;
        _intervalValue.Maximum = 99999;
        _intervalValue.Value = 1;
        _intervalValue.ValueChanged += (_, _) => { UpdatePreview(); };
        _intervalValue.SetBounds(2, 2, 80, 26);

        _intervalUnit.DropDownStyle = ComboBoxStyle.DropDownList;
        _intervalUnit.Items.AddRange(new object[] { "分钟", "小时", "天" });
        _intervalUnit.SelectedIndex = 2;
        _intervalUnit.SelectedIndexChanged += (_, _) => UpdatePreview();
        _intervalUnit.SetBounds(90, 2, 80, 26);

        _pInterval.Controls.AddRange(new Control[]
        {
            _intervalValue,
            _intervalUnit,
            new Label { Text = "从第一次提醒的时刻起算，之后每次都在同一时刻", ForeColor = Theme.SubText, AutoSize = true, Location = new Point(180, 6) }
        });

        // ---- 每天 ----
        _pDaily.Controls.Add(new Label
        {
            Text = "每天都在上面设定的那个时刻提醒。",
            ForeColor = Theme.SubText,
            AutoSize = true,
            Location = new Point(2, 4)
        });

        // ---- 每周 ----
        var weekNames = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        for (var i = 0; i < 7; i++)
        {
            var cb = new CheckBox
            {
                Text = weekNames[i],
                AutoSize = true,
                Location = new Point(2 + i * 66, 4),
                ForeColor = Theme.Text
            };
            cb.CheckedChanged += (_, _) => UpdatePreview();
            _weekdayBoxes[i] = cb;
            _pWeekly.Controls.Add(cb);
        }

        // ---- 每月 ----
        _monthDay.Minimum = 1;
        _monthDay.Maximum = 31;
        _monthDay.Value = DateTime.Now.Day;
        _monthDay.ValueChanged += (_, _) => UpdatePreview();
        _monthDay.SetBounds(44, 2, 64, 26);
        _pMonthly.Controls.AddRange(new Control[]
        {
            new Label { Text = "每月", AutoSize = true, Location = new Point(2, 6) },
            _monthDay,
            new Label
            {
                Text = "号（该月没有这一天时，自动用当月最后一天）",
                ForeColor = Theme.SubText,
                AutoSize = true,
                Location = new Point(118, 6)
            }
        });

        // ---- 指定月份 ----
        for (var i = 0; i < 12; i++)
        {
            var cb = new CheckBox
            {
                Text = $"{i + 1} 月",
                AutoSize = true,
                Location = new Point(2 + i % 6 * 78, 2 + i / 6 * 28),
                ForeColor = Theme.Text
            };
            cb.CheckedChanged += (_, _) => UpdatePreview();
            _monthBoxes[i] = cb;
            _pYearMonths.Controls.Add(cb);
        }

        _yearMonthDay.Minimum = 1;
        _yearMonthDay.Maximum = 31;
        _yearMonthDay.Value = DateTime.Now.Day;
        _yearMonthDay.ValueChanged += (_, _) => UpdatePreview();
        _yearMonthDay.SetBounds(82, 62, 64, 26);
        _pYearMonths.Controls.AddRange(new Control[]
        {
            new Label { Text = "以上月份的", AutoSize = true, Location = new Point(2, 66) },
            _yearMonthDay,
            new Label { Text = "号", AutoSize = true, Location = new Point(154, 66) }
        });

        _repeatHost.BackColor = Theme.Bg;
        // C-21 保守体验：给重复选项区统一内边距（Panel 的 Padding 会作用于 Dock.Fill 子面板），
        // 不改变控件相对坐标，只整体内缩，缓解长表单的拥挤感；深度美化留给第三波。
        _repeatHost.Padding = new Padding(12, 4, 12, 4);
        _repeatHost.Controls.AddRange(new Control[] { _pNone, _pInterval, _pDaily, _pWeekly, _pMonthly, _pYearMonths });
        foreach (Control panel in _repeatHost.Controls)
        {
            panel.BackColor = Theme.Bg;
            panel.Dock = DockStyle.Fill;
        }
        _content.Controls.Add(_repeatHost);
    }

    private void ApplyRepeatVisibility()
    {
        var panels = new[] { _pNone, _pInterval, _pDaily, _pWeekly, _pMonthly, _pYearMonths };
        for (var i = 0; i < panels.Length; i++)
        {
            panels[i].Visible = i == _repeatType.SelectedIndex;
        }
    }

    private int RepeatPanelHeight() => _repeatType.SelectedIndex switch
    {
        1 => 44,          // 每隔 N（含 8px 内边距）
        3 => 38,          // 每周
        4 => 40,          // 每月
        5 => 106,         // 指定月份（两行月份 + 日）
        _ => 34           // 不重复 / 每天
    };

    // ---------------- 结束条件 ----------------

    private void BuildEndSection()
    {
        _lblEnd.Text = "结束条件";
        _lblEnd.Font = Theme.Base;
        _lblEnd.ForeColor = Theme.Text;
        _lblEnd.AutoSize = true;
        _content.Controls.Add(_lblEnd);

        _endType.DropDownStyle = ComboBoxStyle.DropDownList;
        _endType.Items.AddRange(new object[] { "永不结束", "截止到某天", "共执行 N 次" });
        _endType.SelectedIndex = 0;
        _endType.SelectedIndexChanged += (_, _) =>
        {
            _untilPicker.Visible = _endType.SelectedIndex == 1;
            _countNum.Visible = _endType.SelectedIndex == 2;
            UpdatePreview();
        };
        _content.Controls.Add(_endType);

        _untilPicker.Format = DateTimePickerFormat.Custom;
        _untilPicker.CustomFormat = "yyyy-MM-dd";
        _untilPicker.Value = DateTime.Now.AddMonths(1);
        _untilPicker.Visible = false;
        _untilPicker.ValueChanged += (_, _) => UpdatePreview();
        _content.Controls.Add(_untilPicker);

        _countNum.Minimum = 1;
        _countNum.Maximum = 9999;
        _countNum.Value = 3;
        _countNum.Visible = false;
        _countNum.ValueChanged += (_, _) => UpdatePreview();
        _content.Controls.Add(_countNum);
    }

    // ---------------- 预提醒 / 备注 / 预览 ----------------

    private void BuildMiscSection()
    {
        _lblPre.Text = "提前预提醒";
        _lblPre.Font = Theme.Base;
        _lblPre.ForeColor = Theme.Text;
        _lblPre.AutoSize = true;
        _content.Controls.Add(_lblPre);

        _preNotify.Minimum = 0;
        _preNotify.Maximum = 1440;
        _preNotify.Value = Math.Clamp(_config.DefaultPreNotifyMinutes, 0, 1440);
        _preNotify.ValueChanged += (_, _) => UpdatePreview();
        _content.Controls.Add(_preNotify);

        _lblPreHint.Text = "分钟（0 = 不预提醒）";
        _lblPreHint.ForeColor = Theme.SubText;
        _lblPreHint.AutoSize = true;
        _content.Controls.Add(_lblPreHint);

        _lblNote.Text = "备注";
        _lblNote.Font = Theme.Base;
        _lblNote.ForeColor = Theme.Text;
        _lblNote.AutoSize = true;
        _content.Controls.Add(_lblNote);

        _note.Multiline = true;
        _note.BorderStyle = BorderStyle.FixedSingle;
        _note.ScrollBars = ScrollBars.Vertical;
        _note.PlaceholderText = "例如：看这个红色块，是不是配错了（可不填）";
        _content.Controls.Add(_note);

        _preview.ForeColor = Theme.Accent;
        _preview.Font = Theme.BaseBold;
        _preview.AutoSize = false;
        _preview.BackColor = Color.FromArgb(238, 244, 255);
        _preview.Padding = new Padding(10, 6, 10, 6);
        _preview.TextAlign = ContentAlignment.TopLeft;
        _content.Controls.Add(_preview);

        _zoomHint.Text = "小提示：拖动窗口边缘可以把界面放大";
        _zoomHint.ForeColor = Theme.SubText;
        _zoomHint.Font = Theme.Small;
        _zoomHint.AutoSize = true;
        _content.Controls.Add(_zoomHint);
    }

    private void BuildButtons()
    {
        _ok.Click += (_, _) => Confirm();
        _content.Controls.Add(_ok);

        _cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        _content.Controls.Add(_cancel);

        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    // ---------------- 版面重排 ----------------

    /// <summary>从上到下顺序摆放；隐藏的分组自动不占位置，避免大片空白。</summary>
    private void Relayout()
    {
        _zoom?.Apply(1f, true);   // 回到设计尺度再改坐标，防止在放大状态下改版面

        var y = 14;

        _lblTime.Location = new Point(Pad, y);
        y += _lblTime.Height + 6;

        _tabs.SetBounds(Pad, y, InnerWidth, 152);
        y += 152 + 14;

        _lblRepeat.Location = new Point(Pad, y + 6);
        _repeatType.SetBounds(Pad + 52, y, 210, 28);
        y += 36;

        var panelHeight = RepeatPanelHeight();
        _repeatHost.SetBounds(Pad, y, InnerWidth, panelHeight);
        y += panelHeight + 12;

        var showEnd = _repeatType.SelectedIndex != 0;
        _lblEnd.Visible = showEnd;
        _endType.Visible = showEnd;
        _untilPicker.Visible = showEnd && _endType.SelectedIndex == 1;
        _countNum.Visible = showEnd && _endType.SelectedIndex == 2;

        if (showEnd)
        {
            _lblEnd.Location = new Point(Pad, y + 6);
            _endType.SetBounds(Pad + 76, y, 150, 28);
            _untilPicker.SetBounds(Pad + 240, y, 150, 28);
            _countNum.SetBounds(Pad + 240, y, 80, 28);
            y += 36 + 8;
        }

        _lblPre.Location = new Point(Pad, y + 6);
        _preNotify.SetBounds(Pad + 76, y, 70, 26);
        _lblPreHint.Location = new Point(Pad + 156, y + 6);
        y += 34 + 6;

        _lblNote.Location = new Point(Pad, y + 2);
        y += 24;

        _note.SetBounds(Pad, y, InnerWidth, 56);
        y += 66;

        // 预览框高度按「不重复 2 行 / 有重复最多 5 行」留，避免大片空白或文字被切
        var previewHeight = (_repeatType.SelectedIndex == 0 ? 2 : 5) * 18 + 16;
        _preview.SetBounds(Pad, y, InnerWidth, previewHeight);
        y += previewHeight + 8;

        _cancel.SetBounds(DesignWidth - Pad - 84, y, 84, 36);
        _ok.SetBounds(DesignWidth - Pad - 84 - 8 - 84, y, 84, 36);
        y += 36 + 10;

        _zoomHint.Location = new Point(Pad, y);
        y += _zoomHint.Height + 8;

        var height = y;
        _content.Size = new Size(DesignWidth, height);

        _zoom?.Capture();
        ApplyAutoSize();
        if (!_autoFit) _zoom?.FitToWindow();
    }

    private void ApplyAutoSize()
    {
        if (!_autoFit) return;

        // 这里会动窗口大小，必须屏蔽自己的 Resize 逻辑，否则会把「自动适配」误判成「用户拉伸」
        _suppressResize = true;
        try
        {
            var frame = Size - ClientSize;                 // 标题栏与边框占用的部分
            MinimumSize = new Size(_content.Width + frame.Width, _content.Height + frame.Height);
            ClientSize = new Size(_content.Width, _content.Height);
            _autoClient = ClientSize;
        }
        finally
        {
            _suppressResize = false;
        }
    }

    // ---------------- 预设与校验 ----------------

    private void ApplyPreset(RepeatRule? preset, DateTime? presetTrigger)
    {
        if (presetTrigger.HasValue)
        {
            _tabs.SelectedIndex = 1;
            _absolute.Value = presetTrigger.Value;
        }

        if (preset == null)
        {
            ApplyRepeatVisibility();
            return;
        }

        var safe = RepeatEngine.Sanitize(preset);   // C-4：脏数据防御，消除 foreach(null) NRE
        _repeatType.SelectedIndex = (int)safe.Type;
        _intervalValue.Value = Math.Clamp(safe.IntervalValue, 1, 99999);
        _intervalUnit.SelectedIndex = (int)safe.IntervalUnit;

        foreach (var d in safe.Weekdays)
        {
            var idx = d == 0 ? 6 : d - 1;
            if (idx is >= 0 and <= 6) _weekdayBoxes[idx].Checked = true;
        }

        _monthDay.Value = Math.Clamp(safe.DayOfMonth, 1, 31);
        foreach (var m in safe.Months)
        {
            if (m is >= 1 and <= 12) _monthBoxes[m - 1].Checked = true;
        }
        _yearMonthDay.Value = Math.Clamp(safe.DayOfMonth, 1, 31);

        if (safe.MaxCount.HasValue)
        {
            _endType.SelectedIndex = 2;
            _countNum.Value = Math.Clamp(safe.MaxCount.Value, 1, 9999);
        }
        else if (safe.Until.HasValue)
        {
            _endType.SelectedIndex = 1;
            _untilPicker.Value = safe.Until.Value;
        }

        ApplyRepeatVisibility();
    }

    private void Confirm()
    {
        DateTime trigger;
        if (_tabs.SelectedIndex == 0)
        {
            var value = (int)_numRelative.Value;
            trigger = _unitRelative.SelectedIndex == 0
                ? DateTime.Now.AddMinutes(value)
                : DateTime.Now.AddHours(value);
        }
        else
        {
            trigger = _absolute.Value;
            if (trigger <= DateTime.Now)
            {
                MessageBox.Show(this, "指定的时间已经过去了，请重新选择。", "时间无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        trigger = new DateTime(trigger.Year, trigger.Month, trigger.Day, trigger.Hour, trigger.Minute, 0);

        var rule = BuildRule();
        var error = RepeatEngine.Validate(rule);
        if (error != null)
        {
            MessageBox.Show(this, error, "重复设置不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (rule.Until.HasValue && rule.Until.Value.Date < trigger.Date)
        {
            MessageBox.Show(this, "截止日期早于提醒日期，换个日期吧。", "日期冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        TriggerAt = trigger;
        Repeat = rule;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>从界面控件组装规则对象（预览与提交共用，保证所见即所得）。</summary>
    private RepeatRule BuildRule()
    {
        var rule = new RepeatRule { Type = (RepeatType)_repeatType.SelectedIndex };
        rule.IntervalValue = (int)_intervalValue.Value;
        rule.IntervalUnit = (IntervalUnit)_intervalUnit.SelectedIndex;

        for (var i = 0; i < 7; i++)
        {
            if (!_weekdayBoxes[i].Checked) continue;
            rule.Weekdays.Add((i + 1) % 7);      // 界面 0=周一 … 6=周日 → DayOfWeek 1..6,0
        }

        rule.DayOfMonth = (int)_monthDay.Value;
        for (var i = 0; i < 12; i++)
        {
            if (_monthBoxes[i].Checked) rule.Months.Add(i + 1);
        }

        if (rule.Type == RepeatType.YearMonths) rule.DayOfMonth = (int)_yearMonthDay.Value;

        if (rule.Type != RepeatType.None)
        {
            if (_endType.SelectedIndex == 1) rule.Until = _untilPicker.Value.Date;
            if (_endType.SelectedIndex == 2) rule.MaxCount = (int)_countNum.Value;
        }

        return rule;
    }

    private DateTime PreviewTrigger()
    {
        if (_tabs.SelectedIndex == 0)
        {
            var value = (int)_numRelative.Value;
            var t = _unitRelative.SelectedIndex == 0 ? DateTime.Now.AddMinutes(value) : DateTime.Now.AddHours(value);
            return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);
        }
        return _absolute.Value;
    }

    /// <summary>底部预览：把「第一次什么时候、之后按什么规则、下一次是几号几点」写死成看得懂的话。</summary>
    private void UpdatePreview()
    {
        if (_repeatType.Items.Count == 0) return;

        var trigger = PreviewTrigger();
        var rule = BuildRule();

        var lines = new List<string> { $"第一次提醒：{trigger:yyyy-MM-dd HH:mm}" };

        if (rule.Type == RepeatType.None)
        {
            lines.Add("之后：不再提醒（点「已知晓」后这条结束）");
        }
        else
        {
            lines.Add("之后：" + DescribeRepeat(rule, trigger));

            var next = RepeatEngine.NextAfter(rule, trigger);
            lines.Add(next.HasValue
                ? $"下一次：{next.Value:yyyy-MM-dd HH:mm}"
                : "下一次：没有（已到截止条件）");

            if (rule.Until.HasValue) lines.Add($"结束：至 {rule.Until.Value:yyyy-MM-dd}");
            else if (rule.MaxCount.HasValue) lines.Add($"结束：共提醒 {rule.MaxCount.Value} 次（含这次）");
        }

        if (_preNotify.Value > 0) lines.Add($"另：每次提前 {_preNotify.Value} 分钟在托盘轻提示一次");

        _preview.Text = string.Join("\n", lines);
    }

    private static string DescribeRepeat(RepeatRule rule, DateTime trigger)
    {
        var weekend = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

        return rule.Type switch
        {
            RepeatType.Interval => rule.IntervalUnit switch
            {
                IntervalUnit.Minute => $"每隔 {rule.IntervalValue} 分钟提醒一次（从 {trigger:HH:mm} 起算）",
                IntervalUnit.Hour => $"每隔 {rule.IntervalValue} 小时提醒一次（从 {trigger:yyyy-MM-dd HH:mm} 起算）",
                _ => $"每隔 {rule.IntervalValue} 天提醒一次，每次都固定在 {trigger:HH:mm} 这个时刻"
            },
            RepeatType.Daily => $"每天 {trigger:HH:mm} 提醒",
            RepeatType.Weekly => rule.Weekdays.Count == 0
                ? "每周（还没勾选星期）"
                : "每周 " + string.Join("、", rule.Weekdays.OrderBy(d => d).Select(d => weekend[Math.Clamp(d, 0, 6)])) + $" 的 {trigger:HH:mm} 提醒",
            RepeatType.Monthly => $"每月 {rule.DayOfMonth} 日的 {trigger:HH:mm} 提醒（该月没有 {rule.DayOfMonth} 号时用当月最后一天）",
            RepeatType.YearMonths => rule.Months.Count == 0
                ? "每年（还没勾选月份）"
                : $"每年 {string.Join("、", rule.Months.OrderBy(m => m))} 月的 {rule.DayOfMonth} 日 {trigger:HH:mm} 提醒",
            _ => "只提醒这一次"
        };
    }
}
