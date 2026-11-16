using System.IO.Compression;

namespace ScreenRemind.Core;

/// <summary>一键备份 / 还原（O-10）。备份 data + shots 为 zip，还原时解压回程序目录。</summary>
public static class BackupService
{
    public static string CreateBackup(out int fileCount, out long bytes)
    {
        AppPaths.EnsureAll();
        Directory.CreateDirectory(AppPaths.Backups);

        var name = $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        var path = Path.Combine(AppPaths.Backups, name);
        fileCount = 0;

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            fileCount += AddDirectory(zip, AppPaths.Data, "data");
            fileCount += AddDirectory(zip, AppPaths.Shots, "shots");
        }

        bytes = new FileInfo(path).Length;
        Log.Info($"备份完成：{name}，{fileCount} 个文件，{bytes / 1024 / 1024} MB");
        return path;
    }

    private static int AddDirectory(ZipArchive zip, string sourceDir, string entryPrefix)
    {
        if (!Directory.Exists(sourceDir)) return 0;
        var count = 0;
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            // 跳过临时文件与备份自身的中间产物
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".tmp") continue;

            var relative = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            var entryName = $"{entryPrefix}/{relative}";
            try
            {
                zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                count++;
            }
            catch (Exception ex)
            {
                Log.Error($"备份跳过文件：{file}", ex);
            }
        }
        return count;
    }

    /// <summary>还原：把 zip 里的 data/ 与 shots/ 解压回程序目录，覆盖同名文件。</summary>
    public static int Restore(string zipPath, out string? error)
    {
        error = null;
        var restored = 0;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;   // 目录项
                var target = Path.GetFullPath(Path.Combine(AppPaths.Root, entry.FullName));
                // 防目录穿越
                if (!target.StartsWith(Path.GetFullPath(AppPaths.Root), StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn("备份包内存在越界路径，已跳过：" + entry.FullName);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
                restored++;
            }
            Log.Info($"还原完成：{restored} 个文件 ← {Path.GetFileName(zipPath)}");
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error("还原失败", ex);
        }
        return restored;
    }
}
