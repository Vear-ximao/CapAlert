using System.Text;

namespace ScreenRemind.Core;

/// <summary>
/// 路径解析。程序文件（exe/docs）与用户数据分开：
///  · 程序目录：exe 放哪就在哪，只多一个几十字节的「数据位置.txt」记录选择；
///  · 数据目录：首次启动时让用户选盘符，默认建「&lt;盘&gt;:\截图提醒」，截图/任务/日志/备份全在里面，
///    不会在桌面堆一堆文件夹。
/// 环境变量 SCREENREMIND_HOME 可覆盖数据目录，仅用于自检与测试。
/// </summary>
public static class AppPaths
{
    private const string MarkerName = "截图提醒-数据位置.txt";

    private static string? _root;

    /// <summary>exe 所在目录（只读区域：程序本体与文档）。</summary>
    public static string ExeDir { get; } = ResolveExeDir();

    /// <summary>记录「数据放哪」的小文件，与 exe 同目录。</summary>
    public static string MarkerPath => Path.Combine(ExeDir, MarkerName);

    /// <summary>数据根目录。</summary>
    public static string Root
    {
        get
        {
            if (_root != null) return _root;

            var overrideHome = Environment.GetEnvironmentVariable("SCREENREMIND_HOME");
            if (!string.IsNullOrWhiteSpace(overrideHome))
            {
                _root = Path.GetFullPath(overrideHome);
                return _root;
            }

            var marked = ReadMarker();
            if (marked != null)
            {
                _root = marked;
                return _root;
            }

            // 没设过（或设的盘不在了）：退回程序目录，保证任何调用都不会抛异常。
            // 正常启动流程会在创建窗口之前先引导用户选一次，所以这里只是兜底。
            _root = ExeDir;
            return _root;
        }
    }

    /// <summary>是否已经明确配置过数据目录（且目录仍然存在）。</summary>
    public static bool HasConfiguredRoot => ReadMarker() != null;

    public static bool HasEnvOverride => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SCREENREMIND_HOME"));

    public static string DefaultDataDirFor(string driveRoot)
    {
        var drive = driveRoot.TrimEnd('\\', '/');
        return Path.Combine(drive + Path.DirectorySeparatorChar, "截图提醒");
    }

    /// <summary>设定数据目录并写入标记文件（目录会被创建）。</summary>
    public static void SetRoot(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(full);
        File.WriteAllText(MarkerPath, full, new UTF8Encoding(false));
        _root = full;
        Log.Info("数据目录已设定：" + full);
    }

    private static string? ReadMarker()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return null;
            var text = File.ReadAllText(MarkerPath, Encoding.UTF8).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(text)) return null;
            return Directory.Exists(text) ? Path.GetFullPath(text) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveExeDir()
    {
        var exe = Environment.ProcessPath;
        var dir = exe != null ? Path.GetDirectoryName(exe) : null;
        if (string.IsNullOrEmpty(dir) ||
            (exe != null && Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)))
        {
            dir = AppContext.BaseDirectory;
        }
        return Path.GetFullPath(dir!);
    }

    // ---------------- 数据目录下的固定结构 ----------------

    public static string Data => Path.Combine(Root, "data");
    public static string Shots => Path.Combine(Root, "shots");
    public static string Logs => Path.Combine(Root, "logs");
    public static string Backups => Path.Combine(Root, "backups");
    public static string ConfigFile => Path.Combine(Data, "config.json");
    public static string TasksFile => Path.Combine(Data, "tasks.json");
    public static string HistoryFile => Path.Combine(Data, "history.jsonl");

    public static void EnsureAll()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Shots);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
    }

    /// <summary>启动自检：目录必须可写，否则宁可明确报错，也不静默丢数据。</summary>
    public static void EnsureWritable()
    {
        EnsureAll();
        var probe = Path.Combine(Data, ".write_probe");
        try
        {
            File.WriteAllText(probe, DateTime.Now.ToString("O"), Encoding.UTF8);
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new IOException($"数据目录不可写：{Data}\n请换一个位置（设置 → 数据 → 更改位置），或检查磁盘权限。\n原始错误：{ex.Message}", ex);
        }
    }

    /// <summary>把绝对路径转成相对数据根目录的路径，统一用正斜杠存盘。</summary>
    public static string ToRelative(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        var root = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return full[root.Length..].Replace('\\', '/');
        }
        return full.Replace('\\', '/');
    }

    public static string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized) ? normalized : Path.Combine(Root, normalized);
    }

    /// <summary>生成截图路径：shots/YYYY-MM/YYYYMMDD_HHMMSS_序号.png，同秒多张自动递增序号。</summary>
    public static string NewShotAbsolutePath(DateTime now, out string relativePath)
    {
        var monthDir = Path.Combine(Shots, now.ToString("yyyy-MM"));
        Directory.CreateDirectory(monthDir);

        for (var seq = 1; seq <= 999; seq++)
        {
            var name = $"{now:yyyyMMdd_HHmmss}_{seq:000}.png";
            var abs = Path.Combine(monthDir, name);
            if (!File.Exists(abs))
            {
                relativePath = ToRelative(abs);
                return abs;
            }
        }

        var fallback = Path.Combine(monthDir, $"{now:yyyyMMdd_HHmmss_fff}.png");
        relativePath = ToRelative(fallback);
        return fallback;
    }

    /// <summary>旧版把数据放在 exe 同目录，检查是否还有需要搬走的老数据。</summary>
    public static bool HasLegacyDataInExeDir()
    {
        try
        {
            return Directory.Exists(Path.Combine(ExeDir, "data")) || Directory.Exists(Path.Combine(ExeDir, "shots"));
        }
        catch
        {
            return false;
        }
    }
}
