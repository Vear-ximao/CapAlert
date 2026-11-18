using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenRemind.Core;

/// <summary>
/// JSON 落盘：临时文件 + 原子替换，写前留 .bak；解析失败自动用 .bak 兜底，
/// 彻底坏了就改名留证据并返回空数据，绝不因一个坏文件让程序起不来。
/// </summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>单行 JSON（用于 history.jsonl，一行一条，不能有缩进换行）</summary>
    public static readonly JsonSerializerOptions LineOptions = new(Options) { WriteIndented = false };

    public static T Load<T>(string path, Func<T> fallbackFactory) where T : class
    {
        var bak = path + ".bak";

        if (!File.Exists(path))
        {
            if (File.Exists(bak))
            {
                Log.Warn($"主数据文件缺失，尝试用备份恢复：{Path.GetFileName(path)}");
                if (TryRead(bak, out T? fromBak) && fromBak != null) return fromBak;
            }
            return fallbackFactory();
        }

        if (TryRead(path, out T? data) && data != null) return data;

        Log.Error($"数据文件解析失败：{path}");
        TryQuarantine(path);

        if (File.Exists(bak) && TryRead(bak, out T? recovered) && recovered != null)
        {
            Log.Warn($"已用备份恢复：{Path.GetFileName(bak)}");
            return recovered;
        }

        return fallbackFactory();
    }

    private static bool TryRead<T>(string path, out T? value) where T : class
    {
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text))
            {
                value = null;
                return false;
            }
            value = JsonSerializer.Deserialize<T>(text, Options);
            return value != null;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    public static bool Save<T>(string path, T value)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";  // 多实例并发写不互相踩临时文件
        var bak = path + ".bak";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(value, Options);
            File.WriteAllText(tmp, json, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                // File.Replace 会先写 bak 再原子换入，断电也不会出现半个文件
                File.Replace(tmp, path, bak, true);
            }
            else
            {
                File.Move(tmp, path);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"写入失败：{path}", ex);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }

    private static void TryQuarantine(string path)
    {
        try
        {
            var target = path + $".corrupt_{DateTime.Now:yyyyMMdd_HHmmss}";
            File.Move(path, target, true);
            Log.Warn($"损坏文件已隔离为：{Path.GetFileName(target)}");
        }
        catch (Exception ex)
        {
            Log.Error("隔离损坏文件失败", ex);
        }
    }
}
