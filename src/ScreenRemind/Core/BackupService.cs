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

    /// <summary>
    /// 还原：先解压到隔离目录校验全部条目，再逐个换入；任一失败按换入前快照回滚（C-14/C-16）。
    /// </summary>
    public static int Restore(string zipPath, out string? error)
    {
        error = null;
        var restored = 0;
        var root = Path.GetFullPath(AppPaths.Root);
        var staging = Path.Combine(root, "backups", ".restore_tmp_" + Guid.NewGuid().ToString("N"));
        var staged = new List<(string Temp, string Target)>();
        var snapshots = new List<(string Target, byte[]? Bytes)>();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(staging)!);

            // 阶段一：解压到隔离目录并做路径穿越双重校验（不触碰任何主数据）
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;   // 目录项
                    if (!IsSafeEntry(entry.FullName))
                    {
                        Log.Warn("备份包内存在越界路径，已拒绝：" + entry.FullName);
                        continue;
                    }

                    var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!IsUnderRoot(root, target))
                    {
                        Log.Warn("备份包内路径越界，已拒绝：" + entry.FullName);
                        continue;
                    }

                    var temp = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!IsUnderRoot(Path.GetFullPath(staging), temp))
                    {
                        Log.Warn("备份包内路径越界（暂存区），已拒绝：" + entry.FullName);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                    entry.ExtractToFile(temp, true);
                    staged.Add((temp, target));
                }
            }

            // 阶段二：逐个换入；换入前记录快照，失败时逆序回滚
            foreach (var (temp, target) in staged)
            {
                snapshots.Add((target, File.Exists(target) ? File.ReadAllBytes(target) : null));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target)) File.Replace(temp, target, null, true);
                else File.Move(temp, target);
                restored++;
            }
            Log.Info($"还原完成：{restored} 个文件 ← {Path.GetFileName(zipPath)}");
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error("还原失败，正在回滚", ex);
            for (var i = snapshots.Count - 1; i >= 0; i--)
            {
                var (target, bytes) = snapshots[i];
                try
                {
                    if (bytes == null)
                    {
                        if (File.Exists(target)) File.Delete(target);
                    }
                    else
                    {
                        File.WriteAllBytes(target, bytes);
                    }
                }
                catch (Exception rollbackEx)
                {
                    Log.Error($"回滚失败：{target}", rollbackEx);
                }
            }
            restored = 0;   // 已全部回滚，不对外报告“部分成功”
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
        return restored;
    }

    /// <summary>拒绝绝对路径、盘符/冒号与任何 .. 段（C-14）。</summary>
    private static bool IsSafeEntry(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return false;
        var normalized = fullName.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':')) return false;
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(s => s != "..");
    }

    /// <summary>根路径补尾分隔符再比较，避免 C:\root2 被误判为位于 C:\root 下（C-14）。</summary>
    private static bool IsUnderRoot(string root, string candidate)
    {
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
