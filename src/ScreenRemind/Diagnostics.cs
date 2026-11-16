using System.Text;
using ScreenRemind.Core;
using ScreenRemind.Platform;
using ScreenRemind.UI;

namespace ScreenRemind;

/// <summary>
/// 诊断模式（命令行加 --check）：不弹界面，逐项验证运行前提，把结果写进 logs 目录并返回退出码。
/// 用途一是交付前自证「装好的 exe 真能跑」，二是用户遇到问题时一键出报告。
/// </summary>
internal static class Diagnostics
{
    /// <summary>
    /// 自检模式不能去碰用户的数据目录（首次运行还没选过盘符），统一丢到临时目录里跑。
    /// 必须在任何 AppPaths.Root 访问之前调用。
    /// </summary>
    public static void UseTempDataIfUnset()
    {
        if (AppPaths.HasEnvOverride || AppPaths.HasConfiguredRoot) return;

        var temp = Path.Combine(Path.GetTempPath(), "ScreenRemindSelfCheck",
            DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        try
        {
            Directory.CreateDirectory(temp);
            Environment.SetEnvironmentVariable("SCREENREMIND_HOME", temp);
        }
        catch
        {
            // 连临时目录都建不了就让它退回 exe 目录，不阻断自检
        }
    }

    public static int Run()
    {
        var lines = new List<string>();
        var failed = 0;

        void Add(string name, bool ok, string detail)
        {
            if (!ok) failed++;
            lines.Add($"[{(ok ? "PASS" : "FAIL")}] {name}：{detail}");
        }

        lines.Add($"截图提醒 诊断报告　{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        lines.Add($"程序路径：{Environment.ProcessPath}");
        lines.Add($"程序目录：{AppPaths.ExeDir}");
        lines.Add($"数据目录：{AppPaths.Root}（{(AppPaths.HasConfiguredRoot ? "已配置" : AppPaths.HasEnvOverride ? "临时/环境变量指定" : "尚未配置，首次启动会提示选择")}）");
        lines.Add($"操作系统：{Environment.OSVersion}　64 位进程：{Environment.Is64BitProcess}");
        lines.Add($"屏幕：主屏 {SystemInformation.PrimaryMonitorSize.Width}×{SystemInformation.PrimaryMonitorSize.Height}，显示器 {Screen.AllScreens.Length} 个");
        lines.Add($".NET 运行时：{Environment.Version}");
        lines.Add(string.Empty);

        // 1. 目录可写
        try
        {
            AppPaths.EnsureWritable();
            Add("数据目录可写", true, AppPaths.Root);
        }
        catch (Exception ex)
        {
            Add("数据目录可写", false, ex.Message);
        }

        // 2. 配置读写
        try
        {
            var cfg = JsonStore.Load(AppPaths.ConfigFile, () => new AppConfig());
            var saved = JsonStore.Save(AppPaths.ConfigFile, cfg);
            Add("配置文件读写", saved, AppPaths.ConfigFile);
        }
        catch (Exception ex)
        {
            Add("配置文件读写", false, ex.Message);
        }

        // 3. 任务数据
        try
        {
            var repo = new TaskRepository();
            var all = repo.All();
            Add("任务数据加载", true, $"共 {all.Count} 条，待提醒 {all.Count(t => t.State != TaskState.Done)} 条");
        }
        catch (Exception ex)
        {
            Add("任务数据加载", false, ex.Message);
        }

        // 4. 全局热键可用性（试注册后立即释放，不影响用户已生效的热键）
        try
        {
            var cfg = JsonStore.Load(AppPaths.ConfigFile, () => new AppConfig());
            var keys = cfg.Hotkey.EffectiveKeys();
            var free = HotkeyManager.Probe(keys, out var engine);
            var engineText = engine == HotkeyEngine.RegisterHotKey ? "系统注册" : "键盘钩子";
            Add("快捷键可注册", free, free
                ? $"{HotkeyText.Describe(keys)} 当前可用（{engineText}）"
                : $"{HotkeyText.Describe(keys)} 已被占用，需在设置里换一个");
        }
        catch (Exception ex)
        {
            Add("快捷键可注册", false, ex.Message);
        }

        // 5. 抓屏能力（真实抓一次，验证 GDI 与 DPI 链路）
        try
        {
            using var bmp = ScreenCapture.CaptureVirtualScreen(out var bounds);
            var nonBlank = IsNotBlank(bmp);
            Add("屏幕抓取", bmp.Width > 0 && bmp.Height > 0 && nonBlank,
                $"虚拟桌面 {bounds.Width}×{bounds.Height} @({bounds.X},{bounds.Y})，抓取 {bmp.Width}×{bmp.Height}，{(nonBlank ? "非空白" : "疑似全黑")}");

            var temp = Path.Combine(AppPaths.Logs, "capture_probe.png");
            ScreenCapture.SavePng(bmp, temp);
            var size = new FileInfo(temp).Length;
            using (var check = new Bitmap(temp))
            {
                Add("PNG 无损存取", check.Width == bmp.Width && check.Height == bmp.Height,
                    $"落盘 {size / 1024} KB，回读尺寸一致 {check.Width}×{check.Height}");
            }
            File.Delete(temp);
        }
        catch (Exception ex)
        {
            Add("屏幕抓取", false, ex.Message);
        }

        // 6. 开机自启状态
        try
        {
            Add("开机自启", true, AutoStart.Exists ? $"已开启（{AutoStart.ShortcutPath}）" : "未开启");
        }
        catch (Exception ex)
        {
            Add("开机自启", false, ex.Message);
        }

        lines.Add(string.Empty);
        lines.Add(failed == 0 ? "结论：全部检查通过。" : $"结论：{failed} 项未通过，详见上面标记 FAIL 的行。");

        var report = Path.Combine(AppPaths.Logs, $"selfcheck_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            File.WriteAllLines(report, lines, new UTF8Encoding(false));
        }
        catch
        {
            // 报告写不出来也不影响退出码
        }

        return failed == 0 ? 0 : 2;
    }

    /// <summary>判断截图不是全黑/全白（抓屏失败时常见的表现）。</summary>
    private static bool IsNotBlank(Bitmap bmp)    {
        var first = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
        var samples = new[]
        {
            bmp.GetPixel(bmp.Width / 4, bmp.Height / 4),
            bmp.GetPixel(bmp.Width * 3 / 4, bmp.Height / 4),
            bmp.GetPixel(bmp.Width / 4, bmp.Height * 3 / 4),
            bmp.GetPixel(bmp.Width * 3 / 4, bmp.Height * 3 / 4)
        };
        return samples.Any(c => c != first) || first.R != 0 || first.G != 0 || first.B != 0;
    }

    /// <summary>
    /// 界面渲染自检：把每个窗体真实绘制成 PNG，验证构造过程不抛异常、布局没崩。
    /// 不依赖用户点击，也不弹窗，适合交付前跑一遍。
    /// </summary>
    public static int RunRenderTest()
    {
        var dir = Path.Combine(AppPaths.Logs, "render");
        Directory.CreateDirectory(dir);
        var failed = 0;

        using var sample = MakeSampleImage();
        var cfg = JsonStore.Load(AppPaths.ConfigFile, () => new AppConfig());

        void Shot(string name, Func<Form> factory, float zoom = 1f)
        {
            Form? form = null;
            try
            {
                form = factory();
                // DrawToBitmap 只对「真的显示过」的窗体才画子控件，
                // 所以先挪到屏幕外显示一下（用户无感），再截图
                form.StartPosition = FormStartPosition.Manual;
                form.ShowInTaskbar = false;
                form.Bounds = new Rectangle(-30000, -30000, form.Width, form.Height);
                form.Show();
                Application.DoEvents();

                if (zoom > 1f)
                {
                    form.Size = new Size((int)(form.Width * zoom), (int)(form.Height * zoom));
                }

                Thread.Sleep(150);
                Application.DoEvents();

                using var canvas = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(canvas, new Rectangle(0, 0, form.Width, form.Height));
                canvas.Save(Path.Combine(dir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                form.Hide();
                Log.Info($"渲染自检通过：{name}（{form.Width}×{form.Height}）");
            }
            catch (Exception ex)
            {
                failed++;
                Log.Error($"渲染自检失败：{name}", ex);
            }
            finally
            {
                form?.Dispose();
            }
        }

        Shot("01_截图处理面板", () => new UI.ShotPanelForm((Bitmap)sample.Clone(), cfg));
        Shot("02_设置提醒", () => new UI.ReminderSetupForm(cfg));
        Shot("03_提醒弹窗", () =>
        {
            var task = new RemindTask
            {
                Id = "t_demo",
                Note = "看这个红色块，是不是配错了",
                CreatedAt = DateTime.Now.AddDays(-2),
                TriggerAt = DateTime.Now,
                ChainId = "c_demo"
            };
            return new UI.ReminderForm(task, PopupStrength.Normal, new List<string>());
        });
        Shot("04_标注", () => new UI.AnnotateForm((Bitmap)sample.Clone()));

        using var controller = new AppController();
        Shot("05_任务列表", () => new UI.TaskListForm(controller));
        Shot("06_日历", () => new UI.CalendarForm(controller));
        Shot("07_设置", () => new UI.SettingsForm(controller));

        var bounds = new Rectangle(0, 0, sample.Width, sample.Height);
        Shot("08_框选界面", () => new UI.RegionSelectorForm((Bitmap)sample.Clone(), bounds, null));

        // 缩放验证：把窗口拉大 1.5 倍，检查内容是否等比放大、有没有错位
        Shot("09_设置提醒_放大1.5倍", () => new UI.ReminderSetupForm(cfg), 1.5f);
        Shot("10_设置_放大1.5倍", () => new UI.SettingsForm(controller), 1.5f);

        // 重复规则文案验证：每隔 1 天 + 结束条件
        Shot("11_设置提醒_每隔1天", () => new UI.ReminderSetupForm(cfg, new RepeatRule
        {
            Type = RepeatType.Interval,
            IntervalValue = 1,
            IntervalUnit = IntervalUnit.Day,
            MaxCount = 5
        }));

        // 其它重复类型的版面（每周勾选一行、指定月份两行）
        Shot("12_设置提醒_每周一三五", () => new UI.ReminderSetupForm(cfg, new RepeatRule
        {
            Type = RepeatType.Weekly,
            Weekdays = new List<int> { 1, 3, 5 }
        }));

        Shot("13_设置提醒_每年三个月", () => new UI.ReminderSetupForm(cfg, new RepeatRule
        {
            Type = RepeatType.YearMonths,
            Months = new List<int> { 3, 6, 9, 12 },
            DayOfMonth = 15
        }));

        // 首次运行选数据位置
        Shot("14_首次选择数据位置", () => new UI.DataLocationForm(true, null));

        // 撤销/重做真跑一遍（不走真实键盘，直接调快捷键处理入口）
        try
        {
            using var form = new UI.AnnotateForm((Bitmap)sample.Clone());
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = new Rectangle(-30000, -30000, form.Width, form.Height);
            form.Show();
            Application.DoEvents();

            form.DebugAddRect();
            form.DebugAddRect();
            form.DebugAddRect();
            var afterAdd = form.DebugShapeCount;

            form.DebugShortcut(Keys.Control | Keys.Z);
            var afterUndo1 = form.DebugShapeCount;
            form.DebugShortcut(Keys.Control | Keys.Z);
            var afterUndo2 = form.DebugShapeCount;

            form.DebugShortcut(Keys.Control | Keys.Y);
            var afterRedo = form.DebugShapeCount;

            form.DebugShortcut(Keys.Control | Keys.Delete);
            var afterClear = form.DebugShapeCount;

            var ok = afterAdd == 3 && afterUndo1 == 2 && afterUndo2 == 1 && afterRedo == 2 && afterClear == 0;
            Log.Info($"撤销/重做自检：新增 {afterAdd} → Ctrl+Z {afterUndo1} → Ctrl+Z {afterUndo2} → Ctrl+Y {afterRedo} → 清空 {afterClear}　{(ok ? "PASS" : "FAIL")}");
            if (!ok) failed++;

            form.Hide();
        }
        catch (Exception ex)
        {
            failed++;
            Log.Error("撤销/重做自检失败", ex);
        }

        return failed == 0 ? 0 : 4;
    }

    private static Bitmap MakeSampleImage()
    {
        var bmp = new Bitmap(900, 560);
        using var g = Graphics.FromImage(bmp);
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Rectangle(0, 0, 900, 560),
            Color.FromArgb(246, 248, 252), Color.FromArgb(214, 226, 246), 35f);
        g.FillRectangle(brush, 0, 0, 900, 560);

        using var block = new SolidBrush(Color.FromArgb(240, 67, 67));
        g.FillRectangle(block, 120, 150, 260, 150);

        using var font = new Font(Theme.FontFamily, 22F, FontStyle.Bold);
        using var text = new SolidBrush(Color.FromArgb(45, 55, 72));
        g.DrawString("这是一张示例截图", font, text, 120, 60);

        using var small = new Font(Theme.FontFamily, 12F);
        g.DrawString("用于验证弹窗、标注、缩放显示是否正常", small, text, 124, 330);
        return bmp;
    }
}
