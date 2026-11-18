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

    /// <summary>
    /// 任务数据落盘失败时触发（参数为失败的文件路径）。Core 不引用 UI，
    /// 由 AppController 订阅后转成托盘红字告警。QA-P2-R3：不再静默吞掉 Save 的 false。
    /// </summary>
    public event EventHandler<string>? PersistFailed;

    public TaskRepository()
    {
        _file = JsonStore.Load(AppPaths.TasksFile, () => new TasksFile());
        SanitizeLoaded(_file);
    }

    /// <summary>从磁盘重新加载（还原备份后用）。</summary>
    public void Reload()
    {
        lock (_gate)
        {
            _file = JsonStore.Load(AppPaths.TasksFile, () => new TasksFile());
            SanitizeLoaded(_file);
        }
    }

    /// <summary>加载后统一净化半合法数据：集合字段非 null、规则集合在合法域内。幂等。</summary>
    private static void SanitizeLoaded(TasksFile file)
    {
        file.Tasks ??= new List<RemindTask>();
        foreach (var task in file.Tasks)
        {
            task.Repeat = RepeatEngine.Sanitize(task.Repeat);
            task.Id ??= string.Empty;
            task.Image ??= string.Empty;
            task.Note ??= string.Empty;
            task.ChainId ??= string.Empty;
            // DeferKind / LastFiredAt 为新增字段，缺值时 CLR 默认已正确，无需赋值
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

    /// <summary>返回是否落盘成功；失败时同时触发 PersistFailed 事件。</summary>
    public bool Save()
    {
        lock (_gate) return PersistLocked();
    }

    /// <summary>调用方必须持有 _gate。落盘失败会告警，避免内存/磁盘静默分叉（QA-P2-R3）。</summary>
    private bool PersistLocked()
    {
        // 按触发时间排序，文件本身可读性更好
        _file.Tasks = _file.Tasks.OrderBy(t => t.EffectiveTime).ToList();
        var path = AppPaths.TasksFile;
        var ok = JsonStore.Save(path, _file);
        if (!ok)
        {
            // JsonStore.Save 内部已 Log.Error 记录了异常；这里补一条明确的任务数据告警，
            // 并通过事件上抛给 UI 层显示托盘红字（Core 不直接依赖 UI）。
            Log.Error($"任务数据写入失败，内存更改未落盘：{path}");
            try { PersistFailed?.Invoke(this, path); }
            catch (Exception ex) { Log.Warn("落盘失败告警回调异常：" + ex.Message); }
        }
        return ok;
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
