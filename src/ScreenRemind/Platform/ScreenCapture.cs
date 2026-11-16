using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScreenRemind.Core;

namespace ScreenRemind.Platform;

/// <summary>
/// 抓屏。进程已声明 Per-Monitor V2 感知，所以这里的坐标全部是物理像素，
/// 不存在「逻辑单位放大导致截图发虚/偏移」的问题。
/// </summary>
public static class ScreenCapture
{
    /// <summary>整个虚拟桌面的物理像素矩形（多显示器时是并集）。</summary>
    public static Rectangle VirtualScreenBounds()
    {
        var x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        if (w <= 0 || h <= 0)
        {
            // 极端兜底：拿不到系统度量时退回 WinForms 的值
            return SystemInformation.VirtualScreen;
        }
        return new Rectangle(x, y, w, h);
    }

    /// <summary>抓取整个虚拟桌面，返回 1:1 像素位图。</summary>
    public static Bitmap CaptureVirtualScreen(out Rectangle bounds)
    {
        bounds = VirtualScreenBounds();
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>取当前前台窗口的物理像素矩形（取不到或属于本程序时返回 null）。</summary>
    public static Rectangle? ForegroundWindowBounds()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        if (IsOwnWindow(hwnd)) return null;
        if (!NativeMethods.IsWindowVisible(hwnd)) return null;

        NativeMethods.RECT rect;
        // 优先用 DWM 扩展边框（不含窗口阴影），失败再退回 GetWindowRect
        var hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<NativeMethods.RECT>());
        if (hr != 0)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out rect)) return null;
        }

        var target = Rectangle.Intersect(
            Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom),
            VirtualScreenBounds());

        return target.Width < 2 || target.Height < 2 ? null : target;
    }

    /// <summary>抓活动窗口（O-15）。取不到或窗口属于本程序时返回 null。</summary>
    public static Bitmap? CaptureForegroundWindow(out Rectangle bounds)
    {
        var target = ForegroundWindowBounds();
        if (target == null)
        {
            bounds = Rectangle.Empty;
            return null;
        }

        var rect = target.Value;
        var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
        bounds = rect;
        return bmp;
    }

    private static bool IsOwnWindow(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            return pid == Environment.ProcessId;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把位图压成 PNG（无损，不重编码为有损格式）。</summary>
    public static void SavePng(Bitmap bmp, string absolutePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        bmp.Save(absolutePath, ImageFormat.Png);
    }

    /// <summary>复制到剪贴板（O-02）。失败不抛出，仅记日志。</summary>
    public static void CopyToClipboard(Bitmap bmp)
    {
        try
        {
            Clipboard.SetImage(bmp);
        }
        catch (Exception ex)
        {
            Log.Warn("复制到剪贴板失败：" + ex.Message);
        }
    }
}
