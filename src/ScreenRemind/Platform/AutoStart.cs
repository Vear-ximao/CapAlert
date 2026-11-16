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

    /// <summary>启动自检：快捷方式存在但目标失效 → 指向当前 exe 重写一遍。</summary>
    public static void RepairIfStale()
    {
        try
        {
            if (!File.Exists(ShortcutPath)) return;
            if (IsHealthy()) return;

            Log.Warn("检测到自启快捷方式指向旧路径，正在修复");
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
