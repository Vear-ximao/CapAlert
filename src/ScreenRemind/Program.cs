using ScreenRemind.Core;
using ScreenRemind.Platform;
using ScreenRemind.UI;

namespace ScreenRemind;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 诊断模式：不弹界面，验证运行前提并写报告，用退出码表达结果
        if (args.Any(a => a.Equals("--check", StringComparison.OrdinalIgnoreCase)))
        {
            Diagnostics.UseTempDataIfUnset();
            Environment.ExitCode = Diagnostics.Run();
            return;
        }

        // 界面渲染自检：把每个窗体画成 PNG，验证布局不崩
        if (args.Any(a => a.Equals("--render-test", StringComparison.OrdinalIgnoreCase)))
        {
            Diagnostics.UseTempDataIfUnset();
            Environment.ExitCode = Diagnostics.RunRenderTest();
            return;
        }

        // 装配自检：真正把托盘、热键、调度器跑起来 2 秒再退出，验证启动链路无异常
        if (args.Any(a => a.Equals("--check-ui", StringComparison.OrdinalIgnoreCase)))
        {
            var uiFailed = false;
            try
            {
                Diagnostics.UseTempDataIfUnset();
                // 只读：跳过 PruneDone 等数据维护副作用（C-20）
                using var probe = new AppController { SkipMaintenance = true };
                var timer = new System.Windows.Forms.Timer { Interval = 2000 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    try { probe.Shutdown(); }
                    catch (Exception ex) { Log.Error("装配自检关闭失败", ex); uiFailed = true; }
                };
                probe.Start();
                timer.Start();
                Application.Run(probe);
                Environment.ExitCode = uiFailed ? 3 : 0;
            }
            catch (Exception ex)
            {
                Log.Error("装配自检失败", ex);
                Environment.ExitCode = 3;
            }
            return;
        }

        // 单实例：第二次双击/启动只唤起已有实例，不产生第二个进程
        if (!SingleInstance.TryAcquire(out var mutex))
        {
            SingleInstance.SignalExisting();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Microsoft YaHei UI", 9F));

        // 任何未处理异常都落日志并给出可读提示，绝不让程序「静默消失」
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleFatal(e.Exception, "界面线程异常");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => HandleFatal(e.ExceptionObject as Exception, "未处理异常");

        // 首次运行：先问数据放哪块盘，再开始建文件。
        // 这样 exe 放桌面也只会多一个几十字节的位置标记，不会在桌面铺一堆文件夹。
        try
        {
            if (!AppPaths.HasEnvOverride && !AppPaths.HasConfiguredRoot)
            {
                using var dlg = new DataLocationForm(true, null);
                if (dlg.ShowDialog() != DialogResult.OK)
                {
                    return;   // 用户选择退出：不建任何目录
                }

                AppPaths.SetRoot(dlg.SelectedPath);
                if (dlg.MigrateLegacy)
                {
                    LegacyMigration.CopyInto(dlg.SelectedPath);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("初始化数据目录失败：" + ex.Message, "截图提醒 · 无法启动",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            // 目录不可写就别装作能跑：明确报错，避免数据静默丢失
            AppPaths.EnsureWritable();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "截图提醒 · 无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            using var controller = new AppController();
            controller.Start();
            Application.Run(controller);
        }
        catch (Exception ex)
        {
            HandleFatal(ex, "启动失败");
        }
        finally
        {
            GC.KeepAlive(mutex);
        }
    }

    private static void HandleFatal(Exception? ex, string title)
    {
        if (ex == null) return;
        Log.Error(title, ex);

        try
        {
            MessageBox.Show(
                $"{title}：{ex.Message}\n\n错误详情已写入 logs 目录，可把该日志发给开发者排查。",
                "截图提醒",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // 连弹窗都失败时放弃提示
        }
    }
}
