namespace ScreenRemind.Core;

/// <summary>
/// 旧版把 data/shots 放在 exe 同目录。新版数据目录在别处时，把老数据原样搬过去（只复制，不删原件）。
/// 同一个实现也用于「设置 → 更改位置」。
/// </summary>
public static class LegacyMigration
{
    private static readonly string[] SubDirs = { "data", "shots", "backups" };

    public static int CountFiles(params string[] dirs)
    {
        var count = 0;
        foreach (var dir in dirs)
        {
            try
            {
                if (Directory.Exists(dir)) count += Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
            }
            catch
            {
                // 统计失败不影响主流程
            }
        }
        return count;
    }

    /// <summary>把 exe 目录下的 data / shots / backups 复制到新数据目录。</summary>
    public static int CopyInto(string targetRoot) => CopyFrom(AppPaths.ExeDir, targetRoot);

    /// <summary>从指定数据根目录复制到新位置，返回复制的文件数。同名文件不覆盖。</summary>
    public static int CopyFrom(string sourceRoot, string targetRoot)
    {
        if (string.Equals(
                Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(targetRoot).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var copied = 0;
        foreach (var sub in SubDirs)
        {
            var source = Path.Combine(sourceRoot, sub);
            if (!Directory.Exists(source)) continue;

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var relative = Path.GetRelativePath(sourceRoot, file);
                    var target = Path.Combine(targetRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (File.Exists(target)) continue;      // 不覆盖目标已有文件
                    File.Copy(file, target, false);
                    copied++;
                }
                catch (Exception ex)
                {
                    Log.Error("迁移文件失败：" + file, ex);
                }
            }
        }

        if (copied > 0) Log.Info($"已迁移 {copied} 个文件：{sourceRoot} → {targetRoot}");
        return copied;
    }
}
