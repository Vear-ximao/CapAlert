using ScreenRemind.Core;

namespace ScreenRemind.UI;

/// <summary>日历视图（O-06）：一眼看到哪几天有提醒，点某天看当天清单。</summary>
public sealed class CalendarForm : Form
{
    private readonly AppController _app;
    private readonly MonthGrid _grid = new();
    private readonly ListBox _dayList = new();
    private readonly Label _monthTitle = new();
    private DateTime _cursor = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public CalendarForm(AppController app)
    {
        _app = app;
        Theme.ApplyForm(this);
        Text = "截图提醒 · 日历";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(980, 660);
        MinimumSize = new Size(860, 560);

        var bar = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Theme.Card, Padding = new Padding(12, 10, 12, 10) };

        var prev = Theme.GhostButton("‹");
        prev.SetBounds(12, 10, 40, 34);
        prev.Click += (_, _) => { _cursor = _cursor.AddMonths(-1); RefreshCalendar(); };
        bar.Controls.Add(prev);

        var next = Theme.GhostButton("›");
        next.SetBounds(56, 10, 40, 34);
        next.Click += (_, _) => { _cursor = _cursor.AddMonths(1); RefreshCalendar(); };
        bar.Controls.Add(next);

        _monthTitle.SetBounds(108, 18, 200, 24);
        _monthTitle.Font = new Font(Theme.FontFamily, 12F, FontStyle.Bold);
        _monthTitle.ForeColor = Theme.Text;
        bar.Controls.Add(_monthTitle);

        var today = Theme.GhostButton("回到今天");
        today.SetBounds(0, 10, 92, 34);
        today.Click += (_, _) =>
        {
            _cursor = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            _grid.SelectedDate = DateTime.Today;
            RefreshCalendar();
        };
        bar.Controls.Add(today);
        Theme.PinRight(today, 12);

        _grid.Dock = DockStyle.Fill;
        _grid.DateClicked += (_, date) => { ShowDay(date); };

        var side = new Panel { Dock = DockStyle.Right, Width = 300, BackColor = Theme.Card, Padding = new Padding(12) };
        var sideTitle = new Label
        {
            Text = "当天提醒",
            Font = Theme.Subtitle,
            ForeColor = Theme.Text,
            Dock = DockStyle.Top,
            Height = 28
        };
        _dayList.Dock = DockStyle.Fill;
        _dayList.Font = Theme.Base;
        _dayList.BorderStyle = BorderStyle.None;
        _dayList.IntegralHeight = false;
        _dayList.DoubleClick += (_, _) =>
        {
            if (_dayList.SelectedItem is TaskRow row) _app.ShowTaskListAndSelect(row.TaskId);
        };
        side.Controls.Add(_dayList);
        side.Controls.Add(sideTitle);

        Controls.Add(_grid);
        Controls.Add(side);
        Controls.Add(bar);

        _grid.SelectedDate = DateTime.Today;
        RefreshCalendar();
    }

    public void RefreshCalendar()
    {
        _monthTitle.Text = _cursor.ToString("yyyy 年 M 月");
        _grid.Tasks = _app.Repo.All().Where(t => t.State != TaskState.Done).ToList();
        _grid.FirstDay = _cursor;
        _grid.Invalidate();
        ShowDay(_grid.SelectedDate ?? DateTime.Today);
    }

    private void ShowDay(DateTime date)
    {
        _dayList.Items.Clear();
        var items = _app.Repo.All()
            .Where(t => t.State != TaskState.Done && t.EffectiveTime.Date == date.Date)
            .OrderBy(t => t.EffectiveTime)
            .ToList();

        if (items.Count == 0)
        {
            _dayList.Items.Add(new TaskRow { Display = $"{date:MM-dd} 没有提醒", TaskId = string.Empty });
            return;
        }

        foreach (var task in items)
        {
            _dayList.Items.Add(new TaskRow
            {
                TaskId = task.Id,
                Display = $"{task.EffectiveTime:HH:mm}  {(string.IsNullOrWhiteSpace(task.Note) ? "（无备注）" : task.Note)}"
            });
        }
    }

    private sealed class TaskRow
    {
        public string TaskId { get; init; } = string.Empty;
        public string Display { get; init; } = string.Empty;
        public override string ToString() => Display;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RefreshCalendar();
    }

    /// <summary>月历网格：自己画，比 TableLayoutPanel 轻且好控样式。</summary>
    private sealed class MonthGrid : Control
    {
        public DateTime FirstDay { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        public DateTime? SelectedDate { get; set; } = DateTime.Today;
        public List<RemindTask> Tasks { get; set; } = new();

        public event EventHandler<DateTime>? DateClicked;

        private const int HeaderHeight = 36;

        public MonthGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        private Rectangle DayCell(int index, out DateTime date)
        {
            var offset = ((int)FirstDay.DayOfWeek + 6) % 7;   // 周一列起
            date = FirstDay.AddDays(index - offset);

            var col = index % 7;
            var row = index / 7;
            var cellW = (Width - 16) / 7;
            var cellH = (Height - HeaderHeight - 12) / 6;
            return new Rectangle(8 + col * cellW + 3, HeaderHeight + 4 + row * cellH + 3, cellW - 6, cellH - 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var names = new[] { "一", "二", "三", "四", "五", "六", "日" };
            var cellW = (Width - 16) / 7;
            using var headFont = new Font(Theme.FontFamily, 9F, FontStyle.Bold);
            using var headBrush = new SolidBrush(Theme.SubText);
            for (var i = 0; i < 7; i++)
            {
                var text = "周" + names[i];
                var size = g.MeasureString(text, headFont);
                g.DrawString(text, headFont, headBrush, 8 + i * cellW + (cellW - size.Width) / 2, 10);
            }

            using var dayFont = new Font(Theme.FontFamily, 10F);
            using var todayFont = new Font(Theme.FontFamily, 10F, FontStyle.Bold);

            for (var i = 0; i < 42; i++)
            {
                var rect = DayCell(i, out var date);
                if (rect.Height <= 0) continue;

                var inMonth = date.Month == FirstDay.Month;
                var isToday = date.Date == DateTime.Today;
                var isSelected = SelectedDate.HasValue && date.Date == SelectedDate.Value.Date;

                using (var bg = new SolidBrush(inMonth ? Theme.Card : Theme.CalendarOff))
                {
                    g.FillRectangle(bg, rect);
                }

                if (isSelected)
                {
                    using var pen = new Pen(Theme.Accent, 2);
                    g.DrawRectangle(pen, rect);
                }

                using var fg = new SolidBrush(inMonth ? Theme.Text : Theme.CalendarMuted);
                g.DrawString(date.Day.ToString(), isToday ? todayFont : dayFont, fg, rect.X + 8, rect.Y + 6);

                if (isToday)
                {
                    using var dot = new SolidBrush(Theme.Accent);
                    g.FillEllipse(dot, rect.X + 8, rect.Y + 26, 5, 5);
                }

                var dayTasks = Tasks.Where(t => t.EffectiveTime.Date == date.Date).Take(3).ToList();
                var dy = rect.Bottom - 12;
                for (var k = 0; k < dayTasks.Count; k++)
                {
                    using var dot = new SolidBrush(k == 0 ? Theme.Accent : Color.FromArgb(150, 175, 220));
                    g.FillEllipse(dot, rect.X + 8 + k * 10, dy, 6, 6);
                }

                if (dayTasks.Count > 0)
                {
                    using var cntFont = new Font(Theme.FontFamily, 7.5F);
                    using var cntBrush = new SolidBrush(Theme.SubText);
                    g.DrawString($"{dayTasks.Count}", cntFont, cntBrush, rect.Right - 16, rect.Bottom - 16);
                }
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (var i = 0; i < 42; i++)
            {
                var rect = DayCell(i, out var date);
                if (!rect.Contains(e.Location)) continue;
                SelectedDate = date;
                Invalidate();
                DateClicked?.Invoke(this, date);
                return;
            }
        }
    }
}
