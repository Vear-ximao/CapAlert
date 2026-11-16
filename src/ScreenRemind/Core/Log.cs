using System.Text;

namespace ScreenRemind.Core;

/// <summary>按天切分的文件日志，保留 14 天。日志写失败绝不影响主流程。</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            AppPaths.EnsureAll();
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);
            if (ex != null)
            {
                line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                {
                    line.Append(" @ ").Append(ex.StackTrace!.Split('\n')[0].Trim());
                }
            }
            line.AppendLine();

            lock (Gate)
            {
                File.AppendAllText(Path.Combine(AppPaths.Logs, $"{DateTime.Now:yyyy-MM-dd}.log"), line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 日志本身失败时保持静默，不能因为日志导致主流程崩溃
        }
    }

    public static void Cleanup(int keepDays = 14)
    {
        try
        {
            if (!Directory.Exists(AppPaths.Logs)) return;
            var limit = DateTime.Now.Date.AddDays(-keepDays);
            foreach (var file in Directory.GetFiles(AppPaths.Logs, "*.log"))
            {
                if (File.GetLastWriteTime(file) < limit)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // 清理失败忽略
        }
    }
}
