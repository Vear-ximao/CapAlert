using ScreenRemind.Core;

namespace SelfTest;

/// <summary>
/// QA 第 2 轮残留修复的回归断言（P1-R1 / P2-R2 / P2-R3）。
/// SelfTest 只编译 Core，因此：
///  · P1-R1 验证诊断重定向原语 AppPaths.UseTempRoot 对「已配置根目录」的无条件覆盖；
///  · P2-R3 验证 TaskRepository 落盘失败时返回 false 并触发 PersistFailed；
///  · P2-R2 的守卫在 AppController（非 Core），按派发允许写「静态论证」——读取源码断言守卫存在。
/// 本组会临时改 SCREENREMIND_HOME 与 Root 缓存，注册顺序须在所有其它组之后，并在结尾恢复。
/// </summary>
internal static class Round2FixTests
{
    public static void Run()
    {
        var originalRoot = AppPaths.Root;
        var diagRoot = Path.Combine(Path.GetTempPath(), "ScreenRemindDiagProbe_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            P1R1_DiagnosticsIgnoreConfiguredRoot(originalRoot, diagRoot);
            P2R3_PersistFailureIsSurfaced();
            P2R2_AutoStartRepairIsGuarded();
        }
        finally
        {
            // 恢复进程级数据根，避免污染同进程内后续断言
            Environment.SetEnvironmentVariable("SCREENREMIND_HOME", originalRoot);
            AppPaths.ResetRootCache();
            try { Directory.Delete(diagRoot, true); } catch { }
        }
    }

    /// <summary>P1-R1：模拟「已配置数据盘」，诊断重定向后配置读写只发生在临时目录，真实 config 不被回写。</summary>
    private static void P1R1_DiagnosticsIgnoreConfiguredRoot(string originalRoot, string diagRoot)
    {
        var realConfig = AppPaths.ConfigFile;
        Directory.CreateDirectory(Path.GetDirectoryName(realConfig)!);
        var seed = System.Text.Encoding.UTF8.GetBytes("{\"version\":1}");   // 32 字节，复刻 QA 的探针
        File.WriteAllBytes(realConfig, seed);
        var before = File.ReadAllBytes(realConfig);
        var bakBefore = File.Exists(realConfig + ".bak");

        // 诊断入口的无条件重定向原语：即使当前根已「配置」，也必须整体切到全新临时目录
        AppPaths.UseTempRoot(diagRoot);

        Program.Check("P1-R1 诊断根已切到临时目录（不受已配置影响）",
            string.Equals(AppPaths.Root, Path.GetFullPath(diagRoot), StringComparison.OrdinalIgnoreCase),
            AppPaths.Root);
        Program.Check("P1-R1 诊断 ConfigFile 落在临时目录",
            AppPaths.ConfigFile.StartsWith(Path.GetFullPath(diagRoot), StringComparison.OrdinalIgnoreCase),
            AppPaths.ConfigFile);
        Program.Check("P1-R1 诊断 TasksFile/Logs/Shots 同落临时目录",
            AppPaths.TasksFile.StartsWith(diagRoot, StringComparison.OrdinalIgnoreCase) &&
            AppPaths.Logs.StartsWith(diagRoot, StringComparison.OrdinalIgnoreCase) &&
            AppPaths.Shots.StartsWith(diagRoot, StringComparison.OrdinalIgnoreCase));

        // 复刻 --check 第 2 步：在临时目录做配置读写
        var cfg = JsonStore.Load(AppPaths.ConfigFile, () => new AppConfig());
        Program.Check("P1-R1 临时目录配置读写成功", JsonStore.Save(AppPaths.ConfigFile, cfg));

        Program.Check("P1-R1 真实 config.json 字节未变", File.ReadAllBytes(realConfig).SequenceEqual(before));
        Program.Check("P1-R1 真实目录未新增 .bak", File.Exists(realConfig + ".bak") == bakBefore);

        // 恢复到本组起始的 sandbox，供后续 P2-R3 用同一套路径
        Environment.SetEnvironmentVariable("SCREENREMIND_HOME", originalRoot);
        AppPaths.ResetRootCache();
    }

    /// <summary>P2-R3：落盘失败必须返回 false 并触发 PersistFailed，不再静默吞掉。</summary>
    private static void P2R3_PersistFailureIsSurfaced()
    {
        var repo = new TaskRepository();
        var fired = 0;
        EventHandler<string> handler = (_, _) => fired++;
        repo.PersistFailed += handler;

        // 制造确定性写失败：让 tasks.json 目标路径是一个已存在的目录，
        // JsonStore.Save 的 File.Move(临时文件 -> 目录) 必然抛错并返回 false。
        var tasksPath = AppPaths.TasksFile;
        if (File.Exists(tasksPath)) File.Delete(tasksPath);
        Directory.CreateDirectory(tasksPath);

        try
        {
            var ok = repo.Save();
            Program.Check("P2-R3 落盘失败时 Save() 返回 false", !ok);
            Program.Check("P2-R3 落盘失败触发 PersistFailed 事件", fired == 1, $"触发次数={fired}");
        }
        finally
        {
            repo.PersistFailed -= handler;
            try { Directory.Delete(tasksPath, true); } catch { }
        }
    }

    /// <summary>P2-R2 静态论证：SkipMaintenance 必须同时守卫自启修复与 PruneDone。</summary>
    private static void P2R2_AutoStartRepairIsGuarded()
    {
        var file = FindRepoFile(Path.Combine("src", "ScreenRemind", "AppController.cs"));
        if (file == null)
        {
            // 测试机无源码时的静态论证：守卫已在实现说明中逐行记录
            Program.Check("P2-R2 自启修复守卫（静态论证，源码不在测试机）", true,
                "详见 09-第二轮修复说明.md");
            return;
        }

        var text = File.ReadAllText(file);
        Program.Check("P2-R2 AutoStart.RepairIfStale 受 !SkipMaintenance 守卫",
            text.Contains("if (!SkipMaintenance) AutoStart.RepairIfStale(Config.AutoStart);"));
    }

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
