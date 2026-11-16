using ScreenRemind.Core;

namespace ScreenRemind.UI;

/// <summary>
/// 首次启动 / 更改位置时的「数据放哪」选择框。
/// 只选盘符就自动用「&lt;盘&gt;:\截图提醒」，也可以手动指定任意文件夹。
/// </summary>
public sealed class DataLocationForm : Form
{
    private readonly ComboBox _drives = new();
    private readonly TextBox _path = new();
    private readonly Label _status = new();
    private readonly CheckBox _migrate = new();
    private bool _applyingDrive;

    public string SelectedPath => _path.Text.Trim();

    public bool MigrateLegacy => _migrate.Checked;

    /// <param name="firstRun">true = 首次启动引导；false = 设置里更改位置</param>
    public DataLocationForm(bool firstRun, string? currentPath)
    {
        Theme.ApplyForm(this);
        Text = firstRun ? "第一次使用 · 选一个数据存放位置" : "更改数据存放位置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(580, 372);

        var title = new Label
        {
            Text = firstRun ? "截图、任务、日志、备份都会存在这里" : "把现有数据搬到新位置",
            Font = new Font(Theme.FontFamily, 13F, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(24, 20)
        };
        Controls.Add(title);

        var desc = new Label
        {
            Text = "程序本体只有一个 exe，不会在桌面铺一堆文件夹。\n" +
                   "选好盘符后会自动建一个「截图提醒」文件夹，以后全部内容都在里面，换电脑只要带上 exe 再选一次。",
            ForeColor = Theme.SubText,
            AutoSize = false,
            Bounds = new Rectangle(24, 52, 532, 44)
        };
        Controls.Add(desc);

        Controls.Add(new Label
        {
            Text = "存放盘符",
            AutoSize = true,
            ForeColor = Theme.Text,
            Location = new Point(24, 112)
        });

        _drives.DropDownStyle = ComboBoxStyle.DropDownList;
        _drives.SetBounds(110, 108, 300, 28);
        _drives.SelectedIndexChanged += (_, _) =>
        {
            if (_applyingDrive) return;
            if (_drives.SelectedItem is DriveItem item)
            {
                _path.Text = AppPaths.DefaultDataDirFor(item.Root);
                SetStatus(null);
            }
        };
        Controls.Add(_drives);

        Controls.Add(new Label
        {
            Text = "文件夹",
            AutoSize = true,
            ForeColor = Theme.Text,
            Location = new Point(24, 156)
        });

        _path.SetBounds(110, 152, 340, 28);
        _path.Font = new Font(Theme.FontFamily, 9.5F);
        Controls.Add(_path);

        var browse = Theme.GhostButton("浏览…");
        browse.SetBounds(458, 152, 80, 28);
        browse.Click += (_, _) => Browse();
        Controls.Add(browse);

        _status.SetBounds(24, 192, 532, 44);
        _status.ForeColor = Theme.SubText;
        _status.AutoSize = false;
        Controls.Add(_status);

        LoadDrives(currentPath);

        var legacyCount = LegacyMigration.CountFiles(
            Path.Combine(AppPaths.ExeDir, "data"),
            Path.Combine(AppPaths.ExeDir, "shots"));

        _migrate.Text = $"同时把旧数据一起搬过来（检测到 {legacyCount} 个文件）";
        _migrate.AutoSize = true;
        _migrate.Checked = legacyCount > 0;
        _migrate.Location = new Point(24, 244);
        _migrate.Visible = legacyCount > 0;
        Controls.Add(_migrate);

        var ok = Theme.PrimaryButton("就用这个位置");
        ok.SetBounds(348, 308, 120, 38);
        ok.Click += (_, _) => Confirm();
        Controls.Add(ok);

        var cancel = Theme.GhostButton(firstRun ? "退出" : "取消");
        cancel.SetBounds(476, 308, 80, 38);
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void LoadDrives(string? currentPath)
    {
        _applyingDrive = true;
        try
        {
            var items = new List<DriveItem>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network)) continue;

                    var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                    items.Add(new DriveItem
                    {
                        Root = drive.RootDirectory.FullName,
                        Display = $"{drive.Name.TrimEnd('\\')}    可用 {freeGb:F0} GB"
                    });
                }
                catch
                {
                    // 单个盘读不到信息就跳过（比如刚拔掉的U盘）
                }
            }

            _drives.Items.Clear();
            foreach (var item in items) _drives.Items.Add(item);

            // 已有配置就定位到它所在的盘
            var driveRoot = currentPath != null ? Path.GetPathRoot(currentPath) : null;
            var index = items.FindIndex(i => string.Equals(i.Root, driveRoot, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = items.FindIndex(i => i.Root.StartsWith("D:", StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = 0;

            if (_drives.Items.Count > 0) _drives.SelectedIndex = index;

            _path.Text = currentPath ?? (items.Count > 0 ? AppPaths.DefaultDataDirFor(items[Math.Max(index, 0)].Root) : string.Empty);
        }
        finally
        {
            _applyingDrive = false;
        }
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "选一个文件夹存放截图与任务数据",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_path.Text) ? _path.Text : AppPaths.Root
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _path.Text = dlg.SelectedPath;
        SetStatus(null);
    }

    private void SetStatus(string? error, bool ok = false)
    {
        _status.ForeColor = error != null ? Theme.Danger : ok ? Theme.Success : Theme.SubText;
        _status.Text = error ?? (ok ? "位置可用" : string.Empty);
    }

    private void Confirm()
    {
        var path = SelectedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("请先选择盘符或文件夹");
            return;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            SetStatus("路径不合法：" + ex.Message);
            return;
        }

        if (Path.GetPathRoot(full)?.Equals(full, StringComparison.OrdinalIgnoreCase) == true)
        {
            SetStatus("不要直接选整个盘根目录，请选一个文件夹（如 D:\\截图提醒）");
            return;
        }

        // 真实写一次，确认可写，别等用起来才发现没权限
        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, ".write_probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            SetStatus("这个位置不可写：" + ex.Message);
            return;
        }

        var existing = LegacyMigration.CountFiles(full);
        if (existing > 0 && MessageBox.Show(this,
                $"这个文件夹里已经有 {existing} 个文件。\n继续会在里面建立 data / shots 等子目录，同名文件不会被覆盖。\n\n确定用这个位置？",
                "文件夹非空", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed class DriveItem
    {
        public string Root { get; init; } = string.Empty;
        public string Display { get; init; } = string.Empty;
        public override string ToString() => Display;
    }
}
