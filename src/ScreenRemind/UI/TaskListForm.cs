using ScreenRemind.Core;

namespace ScreenRemind.UI;

/// <summary>任务列表：查看、改期、跳过、删除，以及批量操作与统计。</summary>
public sealed class TaskListForm : Form
{
    private readonly AppController _app;
    private readonly ListView _list = new();
    private readonly ImageList _thumbs = new() { ImageSize = new Size(48, 48), ColorDepth = ColorDepth.Depth32Bit };
    private readonly Label _stats = new();
    private readonly CheckBox _showDone = new() { Text = "显示已完成", AutoSize = true };

    public TaskListForm(AppController app)
    {
        _app = app;
        Theme.ApplyForm(this);
        Text = "截图提醒 · 任务列表";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1020, 640);
        MinimumSize = new Size(820, 460);

        var bar = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Theme.Card, Padding = new Padding(12, 10, 12, 10) };

        var calendar = Theme.GhostButton("日历视图");
        calendar.SetBounds(12, 10, 92, 34);
        calendar.Click += (_, _) => _app.ShowCalendar();
        bar.Controls.Add(calendar);

        var settings = Theme.GhostButton("设置");
        settings.SetBounds(112, 10, 74, 34);
        settings.Click += (_, _) => _app.ShowSettings();
        bar.Controls.Add(settings);

        var pause = Theme.GhostButton("暂停全部");
        pause.SetBounds(194, 10, 92, 34);
        pause.Click += (_, _) => TogglePause(pause);
        bar.Controls.Add(pause);

        var shift = Theme.GhostButton("全部顺延 1 天");
        shift.SetBounds(294, 10, 116, 34);
        shift.Click += (_, _) => ShiftAll(1);
        bar.Controls.Add(shift);

        var backup = Theme.GhostButton("备份");
        backup.SetBounds(418, 10, 74, 34);
        backup.Click += (_, _) => DoBackup();
        bar.Controls.Add(backup);

        var refresh = Theme.GhostButton("刷新");
        refresh.SetBounds(0, 10, 74, 34);
        refresh.Click += (_, _) => Reload();
        bar.Controls.Add(refresh);
        Theme.PinRight(refresh, 16);

        _showDone.Location = new Point(0, 18);
        _showDone.CheckedChanged += (_, _) => Reload();
        bar.Controls.Add(_showDone);
        Theme.PinRight(_showDone, 100);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.SmallImageList = _thumbs;
        _list.MultiSelect = false;
        _list.Font = Theme.Base;
        _list.Columns.Add("截图", 64);
        _list.Columns.Add("备注", 240);
        _list.Columns.Add("下次提醒", 150);
        _list.Columns.Add("重复规则", 380);
        _list.Columns.Add("状态", 90);
        _list.DoubleClick += (_, _) => OpenSelectedImage();
        _list.MouseUp += (_, e) => ShowContextMenu(e);
        _list.Resize += (_, _) => FitColumns();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Theme.Card };
        _stats.Dock = DockStyle.Fill;
        _stats.TextAlign = ContentAlignment.MiddleLeft;
        _stats.Padding = new Padding(14, 0, 0, 0);
        _stats.ForeColor = Theme.SubText;
        bottom.Controls.Add(_stats);

        Controls.Add(_list);
        Controls.Add(bottom);
        Controls.Add(bar);

        Reload();
    }

    private void TogglePause(Button button)
    {
        _app.Scheduler.Paused = !_app.Scheduler.Paused;
        button.Text = _app.Scheduler.Paused ? "恢复提醒" : "暂停全部";
        button.ForeColor = _app.Scheduler.Paused ? Theme.Danger : Theme.Text;
        _app.Notify(_app.Scheduler.Paused ? "已暂停全部提醒" : "已恢复提醒");
    }

    private void ShiftAll(int days)
    {
        var count = 0;
        foreach (var task in _app.Repo.All().Where(t => t.State != TaskState.Done))
        {
            task.TriggerAt = task.TriggerAt.AddDays(days);
            if (task.SnoozeUntil.HasValue) task.SnoozeUntil = task.SnoozeUntil.Value.AddDays(days);
            _app.Repo.Update(task);
            count++;
        }
        _app.Notify(count == 0 ? "没有待提醒任务" : $"已把 {count} 个任务顺延 {days} 天");
        Reload();
    }

    private void DoBackup()
    {
        try
        {
            var path = BackupService.CreateBackup(out var files, out var bytes);
            MessageBox.Show(this, $"备份完成\n{Path.GetFileName(path)}\n{files} 个文件，{bytes / 1024.0 / 1024.0:F1} MB\n\n位置：{AppPaths.Backups}",
                "备份", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "备份失败：" + ex.Message, "备份", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void Reload()
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _thumbs.Images.Clear();
            _thumbs.ImageSize = new Size(48, 48);

            var tasks = _app.Repo.All()
                .Where(t => _showDone.Checked || t.State != TaskState.Done)
                .OrderBy(t => t.EffectiveTime)
                .ToList();

            foreach (var task in tasks)
            {
                var stateText = task.State switch
                {
                    TaskState.Done => "已完成",
                    TaskState.Snoozed => $"已顺延 {task.SnoozeUntil:HH:mm}",
                    _ => "待提醒"
                };

                var item = new ListViewItem(string.Empty) { Tag = task.Id };
                item.SubItems.Add(string.IsNullOrWhiteSpace(task.Note) ? "（无备注）" : task.Note);
                item.SubItems.Add(task.EffectiveTime.ToString("yyyy-MM-dd HH:mm"));
                item.SubItems.Add(RepeatEngine.Describe(task.Repeat));
                item.SubItems.Add(stateText);

                var thumb = LoadThumbnail(task.Image);
                if (thumb != null)
                {
                    _thumbs.Images.Add(thumb);
                    item.ImageIndex = _thumbs.Images.Count - 1;
                }

                if (task.State == TaskState.Done) item.ForeColor = Theme.SubText;
                else if (task.EffectiveTime <= DateTime.Now) item.ForeColor = Theme.Danger;

                _list.Items.Add(item);
            }

            var pending = tasks.Count(t => t.State != TaskState.Done);
            var history = _app.Repo.ReadHistory(30);
            var fired = history.Count(h => h.Action is "fire" or "ack" or "snooze" or "renew");
            var acked = history.Count(h => h.Action == "ack");
            var rate = history.Count(h => h.Action is "ack" or "snooze" or "renew") == 0
                ? "—"
                : $"{100.0 * acked / history.Count(h => h.Action is "ack" or "snooze" or "renew"):F0}%";

            _stats.Text = $"待提醒 {pending} 个　|　近 30 天提醒 {fired} 次，已知晓 {acked} 次，按期完成率 {rate}　|　数据目录：{AppPaths.Root}";
        }
        finally
        {
            _list.EndUpdate();
            FitColumns();
        }
    }

    /// <summary>把「备注」「重复规则」两列按窗口宽度分配，避免右侧留一条空白。</summary>
    private void FitColumns()
    {
        if (_list.Columns.Count < 5) return;
        var available = _list.ClientSize.Width - 8 - 64 - 150 - 90;
        if (available < 240) available = 240;
        _list.Columns[1].Width = (int)(available * 0.40);
        _list.Columns[3].Width = (int)(available * 0.60);
    }

    private Bitmap? LoadThumbnail(string relativePath)
    {
        try
        {
            var path = AppPaths.Resolve(relativePath);
            if (!File.Exists(path)) return null;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var src = Image.FromStream(fs, true, true);
            var bmp = new Bitmap(48, 48);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.Clear(Color.FromArgb(235, 238, 243));

            var scale = Math.Min(48f / src.Width, 48f / src.Height);
            var w = (int)(src.Width * scale);
            var h = (int)(src.Height * scale);
            g.DrawImage(src, (48 - w) / 2, (48 - h) / 2, w, h);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void ShowContextMenu(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var hit = _list.GetItemAt(e.X, e.Y);
        if (hit == null) return;
        hit.Selected = true;

        var id = hit.Tag as string;
        if (string.IsNullOrEmpty(id)) return;
        var task = _app.Repo.Get(id);
        if (task == null) return;

        var menu = new ContextMenuStrip { Font = Theme.Base };

        menu.Items.Add("打开图片", null, (_, _) => OpenSelectedImage());
        menu.Items.Add("打开所在文件夹", null, (_, _) => OpenFolder(task));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("立即提醒", null, (_, _) =>
        {
            task.TriggerAt = DateTime.Now;
            task.SnoozeUntil = null;
            task.State = TaskState.Pending;
            _app.Repo.Update(task);
            _app.Scheduler.MarkHandled(task.Id);
            _app.Scheduler.Poke();
            Reload();
        });
        menu.Items.Add("编辑提醒时间…", null, (_, _) => EditTask(task));
        menu.Items.Add("稍后提醒 15 分钟", null, (_, _) =>
        {
            _app.Snooze(task.Id, 15);
            Reload();
        });

        if (task.Repeat.Type != RepeatType.None)
        {
            menu.Items.Add("跳过下一次", null, (_, _) =>
            {
                var next = RepeatEngine.NextAfter(task.Repeat, task.TriggerAt);
                if (next == null)
                {
                    MessageBox.Show(this, "这条提醒链已经结束，没有下一次了。", "跳过", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                task.TriggerAt = next.Value;
                task.SnoozeUntil = null;
                task.PreNotifiedAt = null;
                _app.Repo.Update(task);
                _app.Scheduler.MarkHandled(task.Id);
                _app.Notify($"已跳到下一次：{next:yyyy-MM-dd HH:mm}");
                Reload();
            });
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("删除这条提醒", null, (_, _) =>
        {
            if (MessageBox.Show(this, "删除后不可恢复（图片文件不会被删除）。确定？", "删除提醒",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            _app.Repo.Remove(task.Id);
            _app.Scheduler.MarkHandled(task.Id);
            Reload();
        });

        menu.Show(_list, e.Location);
    }

    private void EditTask(RemindTask task)
    {
        using var dlg = new ReminderSetupForm(_app.Config, task.Repeat, task.TriggerAt);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        task.TriggerAt = dlg.TriggerAt;
        task.Repeat = dlg.Repeat;
        task.Note = dlg.NoteText;
        task.PreNotifyMinutes = dlg.PreNotifyMinutes;
        task.SnoozeUntil = null;
        task.PreNotifiedAt = null;
        task.State = TaskState.Pending;
        _app.Repo.Update(task);
        _app.Scheduler.MarkHandled(task.Id);
        _app.Scheduler.Poke();
        _app.Notify("已更新提醒时间");
        Reload();
    }

    private void OpenSelectedImage()
    {
        if (_list.SelectedItems.Count == 0) return;
        if (_list.SelectedItems[0].Tag is not string id) return;
        var task = _app.Repo.Get(id);
        if (task == null) return;

        var path = AppPaths.Resolve(task.Image);
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "原图文件不存在（可能被移动或删除）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var viewer = new ImagePreviewForm(path, task.Note);
        viewer.ShowDialog(this);
    }

    private void OpenFolder(RemindTask task)
    {
        var path = AppPaths.Resolve(task.Image);
        if (File.Exists(path)) NativeShell.ShowInFolder(path);
        else NativeShell.OpenFolder(AppPaths.Shots);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Reload();
    }

    /// <summary>从日历跳转过来时定位到某条任务。</summary>
    public void SelectTask(string taskId)
    {
        if (string.IsNullOrEmpty(taskId)) return;
        Reload();
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag as string != taskId) continue;
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            _list.Focus();
            return;
        }
    }
}

/// <summary>任务列表里双击看大图的轻量窗口。</summary>
public sealed class ImagePreviewForm : Form
{
    public ImagePreviewForm(string absolutePath, string note)
    {
        Theme.ApplyForm(this);
        Text = "查看原图";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 700);

        var viewer = new ImageViewer { Dock = DockStyle.Fill };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Theme.Card };
        var info = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            ForeColor = Theme.SubText,
            Text = string.IsNullOrWhiteSpace(note) ? absolutePath : $"{note}　·　{absolutePath}"
        };
        bar.Controls.Add(info);

        Controls.Add(viewer);
        Controls.Add(bar);

        try
        {
            using var fs = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var img = Image.FromStream(fs, true, true);
            viewer.SetImage(new Bitmap(img), true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "打开图片失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
