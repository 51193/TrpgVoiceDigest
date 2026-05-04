using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TrpgVoiceDigest.Core.Models;

public enum TaskAction
{
    Add,
    Edit,
    Remove,
    Complete,
    Fail,
    Empty
}

public sealed record TaskOperation(TaskAction Action, int? Key, string? Text) : IOperation;

public enum TaskOutcome
{
    Success,
    Failure
}

public sealed class TaskEntry
{
    public int Key { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsCompleted { get; set; }
    public TaskOutcome? Outcome { get; set; }
}

public sealed class TaskState : IIncrementalDataContainer
{
    private static readonly JsonSerializerOptions JsonExportOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly Dictionary<int, TaskEntry> _active = new();
    private readonly Dictionary<int, TaskEntry> _completed = new();
    private int _nextKey = 1;

    public string Title { get; set; } = "任务";
    public int ActiveCount => _active.Count;
    public int CompletedCount => _completed.Count;

    public IReadOnlyList<TaskEntry> ActiveEntries =>
        _active.Values.OrderBy(e => e.Key).ToList().AsReadOnly();

    public IReadOnlyList<TaskEntry> CompletedEntries =>
        _completed.Values.OrderBy(e => e.Key).ToList().AsReadOnly();

    void IIncrementalDataContainer.ApplyOperations(IReadOnlyList<IOperation> operations)
    {
        var typed = operations.OfType<TaskOperation>().ToList();
        if (typed.Count > 0)
            ApplyOperations(typed);
    }

    public TaskEntry AddEntry(string text, int? afterKey = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null!;

        int key;
        if (afterKey.HasValue && afterKey.Value > 0)
        {
            var candidate = afterKey.Value + 1;
            key = !_active.ContainsKey(candidate) ? candidate : _nextKey;
        }
        else
        {
            key = _nextKey;
        }

        while (_active.ContainsKey(key))
            key = ++_nextKey;

        var now = DateTimeOffset.UtcNow;
        var entry = new TaskEntry
        {
            Key = key,
            Text = text.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            IsCompleted = false
        };
        _active[key] = entry;
        _nextKey = Math.Max(_nextKey, key + 1);
        return entry;
    }

    public bool EditEntry(int key, string text)
    {
        if (!_active.TryGetValue(key, out var entry)) return false;
        if (string.IsNullOrWhiteSpace(text)) return false;
        entry.Text = text.Trim();
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public bool RemoveEntry(int key)
    {
        return _active.Remove(key) || _completed.Remove(key);
    }

    public bool CompleteEntry(int key, TaskOutcome outcome)
    {
        if (!_active.TryGetValue(key, out var entry)) return false;
        _active.Remove(key);
        entry.IsCompleted = true;
        entry.Outcome = outcome;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        _completed[key] = entry;
        return true;
    }

    public void AddThenCompleteEntry(string text, int? afterKey, TaskOutcome outcome)
    {
        var entry = AddEntry(text, afterKey);
        if (entry is not null)
            CompleteEntry(entry.Key, outcome);
    }

    public void ApplyOperations(IReadOnlyList<TaskOperation> operations)
    {
        foreach (var op in operations)
            switch (op.Action)
            {
                case TaskAction.Add:
                    AddEntry(op.Text ?? "", op.Key);
                    break;
                case TaskAction.Edit:
                    if (op.Key.HasValue)
                        EditEntry(op.Key.Value, op.Text ?? "");
                    break;
                case TaskAction.Remove:
                    if (op.Key.HasValue)
                        RemoveEntry(op.Key.Value);
                    break;
                case TaskAction.Complete:
                    if (op.Key.HasValue)
                        CompleteEntry(op.Key.Value, TaskOutcome.Success);
                    break;
                case TaskAction.Fail:
                    if (op.Key.HasValue)
                        CompleteEntry(op.Key.Value, TaskOutcome.Failure);
                    break;
                case TaskAction.Empty:
                    break;
            }
    }

    public string ExportActiveJson()
    {
        var list = ActiveEntries.Select(e => new { key = e.Key, text = e.Text });
        return JsonSerializer.Serialize(list, JsonExportOptions);
    }

    public string ExportMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {Title}");

        sb.AppendLine();
        sb.AppendLine("## 进行中");
        sb.AppendLine();
        if (ActiveCount == 0)
            sb.AppendLine("暂无。");
        else
            foreach (var e in ActiveEntries)
                sb.AppendLine($"{e.Key}. {e.Text}");

        sb.AppendLine();
        sb.AppendLine("## 已完成");
        sb.AppendLine();
        if (CompletedCount == 0)
            sb.AppendLine("暂无。");
        else
            foreach (var e in CompletedEntries)
            {
                var outcome = e.Outcome == TaskOutcome.Success ? "成功" : "失败";
                sb.AppendLine($"{e.Key}. {e.Text} *({outcome})*");
            }

        sb.AppendLine();
        return sb.ToString();
    }

    public string ExportStateJson()
    {
        var payload = new
        {
            active = ActiveEntries.Select(e => new
            {
                key = e.Key,
                text = e.Text,
                created_at = e.CreatedAt.ToString("O"),
                updated_at = e.UpdatedAt.ToString("O")
            }),
            completed = CompletedEntries.Select(e => new
            {
                key = e.Key,
                text = e.Text,
                outcome = e.Outcome?.ToString(),
                created_at = e.CreatedAt.ToString("O"),
                updated_at = e.UpdatedAt.ToString("O")
            })
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}