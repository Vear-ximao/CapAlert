using System.Text;
using System.Text.Json;

namespace ScreenRemind.Core;

/// <summary>
/// 任务与历史的唯一读写入口。tasks.json 整体读写（量小、简单可靠），
/// history.jsonl 逐行追加（永不整体重写，避免越用越慢）。
/// </summary>
public sealed class TaskRepository
{
    private readonly object _gate = new();
    private TasksFile _file;

    public TaskRepository()
    {
        _file = JsonStore.Load(AppPaths.TasksFile, () => new TasksFile());
        _file.Tasks ??= new List<RemindTask>();
    }

    /// <summary>从磁盘重新加载（还原备份后用）。</summary>
    public void Reload()
    {
        lock (_gate)
        {
            _file = JsonStore.Load(AppPaths.TasksFile, () => new TasksFile());
            _file.Tasks ??= new List<RemindTask>();
        }
    }

    /// <summary>返回快照，调用方随手改不会污染内存数据（改完必须 Update 才会落盘）。</summary>
    public List<RemindTask> All()
    {
        lock (_gate)
        {
            return _file.Tasks.Select(t => t.Clone()).ToList();
        }
    }

    public RemindTask? Get(string id)
    {
        lock (_gate)
        {
            return _file.Tasks.FirstOrDefault(t => t.Id == id)?.Clone();
        }
    }

    public string NewTaskId()
    {
        lock (_gate)
        {
            for (var i = 1; i <= 999; i++)
            {
                var id = $"t_{DateTime.Now:yyyyMMdd}_{i:000}";
                if (_file.Tasks.All(t => t.Id != id)) return id;
            }
            return $"t_{DateTime.Now:yyyyMMddHHmmss}";
        }
    }

    public void Add(RemindTask task)
    {
        lock (_gate)
        {
            _file.Tasks.Add(task);
            PersistLocked();
        }
    }

    /// <summary>按 Id 覆盖更新。</summary>
    public void Update(RemindTask task)
    {
        lock (_gate)
        {
            var index = _file.Tasks.FindIndex(t => t.Id == task.Id);
            if (index >= 0) _file.Tasks[index] = task;
            else _file.Tasks.Add(task);
            PersistLocked();
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _file.Tasks.RemoveAll(t => t.Id == id);
            PersistLocked();
        }
    }

    public void Save()
    {
        lock (_gate) PersistLocked();
    }

    private void PersistLocked()
    {
        // 按触发时间排序，文件本身可读性更好
        _file.Tasks = _file.Tasks.OrderBy(t => t.EffectiveTime).ToList();
        JsonStore.Save(AppPaths.TasksFile, _file);
    }

    /// <summary>清理过期已完成任务，避免 tasks.json 无限膨胀（历史仍在 history.jsonl 里）。</summary>
    public int PruneDone(int keepDays = 30)
    {
        lock (_gate)
        {
            var limit = DateTime.Now.AddDays(-keepDays);
            var removed = _file.Tasks.RemoveAll(t => t.State == TaskState.Done && (t.AckAt ?? t.TriggerAt) < limit);
            if (removed > 0)
            {
                PersistLocked();
                Log.Info($"清理已完成任务 {removed} 条");
            }
            return removed;
        }
    }

    public void AppendHistory(HistoryEntry entry)
    {
        try
        {
            AppPaths.EnsureAll();
            var line = JsonSerializer.Serialize(entry, JsonStore.LineOptions) + Environment.NewLine;
            lock (_gate)
            {
                File.AppendAllText(AppPaths.HistoryFile, line, new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            Log.Error("写历史失败", ex);
        }
    }

    public List<HistoryEntry> ReadHistory(int days = 30)
    {
        var list = new List<HistoryEntry>();
        try
        {
            if (!File.Exists(AppPaths.HistoryFile)) return list;
            var limit = DateTime.Now.AddDays(-days);
            foreach (var line in File.ReadLines(AppPaths.HistoryFile, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<HistoryEntry>(line, JsonStore.LineOptions);
                    if (entry == null) continue;
                    if (entry.At >= limit) list.Add(entry);
                }
                catch
                {
                    // 单行坏了跳过，不影响其余历史
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("读历史失败", ex);
        }
        return list;
    }

    /// <summary>同一条链上的所有图片（用于看图时上/下一张）。</summary>
    public List<string> ChainImages(string chainId)
    {
        lock (_gate)
        {
            return _file.Tasks
                .Where(t => t.ChainId == chainId && !string.IsNullOrEmpty(t.Image))
                .Select(t => t.Image)
                .Distinct()
                .ToList();
        }
    }
}
