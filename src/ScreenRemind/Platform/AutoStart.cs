using System.Runtime.InteropServices;
using System.Text;
using ScreenRemind.Core;

namespace ScreenRemind.Platform;

/// <summary>
/// 开机自启：在「用户启动目录」放一个快捷方式（写注册表会破坏便携性，所以不用 Run 键）。
/// 便携包被搬走或改名后，快捷方式会指向旧路径 —— 每次启动自检并自动修复。
/// </summary>
public static class AutoStart
{
    private const string LinkName = "截图提醒.lnk";

    public static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LinkName);

    public static string? CurrentExePath =>
        Environment.ProcessPath is { } p && !Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? p
            : null;

    /// <summary>快捷方式是否存在且指向当前 exe。</summary>
    public static bool IsHealthy()
    {
        try
        {
            if (!File.Exists(ShortcutPath)) return false;
            var target = ReadTarget(ShortcutPath);
            var exe = CurrentExePath;
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(exe)) return false;
            return string.Equals(Path.GetFullPath(target), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool Exists => File.Exists(ShortcutPath);

    public static bool Enable(out string? error)
    {
        error = null;
        try
        {
            var exe = CurrentExePath;
            if (string.IsNullOrEmpty(exe))
            {
                error = "当前以开发模式运行，无法创建自启快捷方式（发布后可用）";
                return false;
            }

            var dir = Path.GetDirectoryName(exe)!;
            object? com = null;
            try
            {
                com = new ShellLinkCoClass();
                var link = (IShellLinkW)com;
                link.SetPath(exe);
                link.SetWorkingDirectory(dir);
                link.SetDescription("截图提醒（便携版）— 开机自动启动，避免漏提醒");
                link.SetIconLocation(exe, 0);
                ((IPersistFile)link).Save(ShortcutPath, true);
            }
            finally
            {
                if (com != null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
            }

            Log.Info("已开启开机自启：" + ShortcutPath);
            return true;
        }
        catch (Exception ex)
        {
            error = "创建快捷方式失败：" + ex.Message;
            Log.Error("开启自启失败", ex);
            return false;
        }
    }

    public static bool Disable(out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            Log.Info("已关闭开机自启");
            return true;
        }
        catch (Exception ex)
        {
            error = "删除快捷方式失败：" + ex.Message;
            Log.Error("关闭自启失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 启动自检：以「配置是否要求自启」为准，而不是被遗留快捷方式反向改写（C-19）。
    /// 目标 exe 文件仍存在时不因“与当前 exe 不同”就重写，避免多副本互相抢占指向。
    /// </summary>
    public static void RepairIfStale(bool desiredEnabled)
    {
        try
        {
            var exists = File.Exists(ShortcutPath);
            if (!desiredEnabled)
            {
                if (exists)
                {
                    Log.Info("配置未开启自启，移除遗留快捷方式");
                    Disable(out _);
                }
                return;
            }

            if (!exists)
            {
                Log.Info("配置要求开机自启但快捷方式缺失，正在创建");
                Enable(out _);
                return;
            }

            // 快捷方式存在：仅当它指向的文件确实已不存在时才重写指向
            var target = ReadTarget(ShortcutPath);
            var targetAlive = !string.IsNullOrWhiteSpace(target) && File.Exists(target);
            if (targetAlive) return;

            Log.Warn("自启快捷方式指向的文件已不存在，正在重建");
            Enable(out _);
        }
        catch (Exception ex)
        {
            Log.Error("自启自检失败", ex);
        }
    }

    public static string? ReadTarget(string lnkPath)
    {
        object? com = null;
        try
        {
            com = new ShellLinkCoClass();
            var link = (IShellLinkW)com;
            ((IPersistFile)link).Load(lnkPath, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            var path = sb.ToString();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (com != null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
        }
    }

    // ---------------- COM 互操作（vtable 顺序不可调整） ----------------

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
