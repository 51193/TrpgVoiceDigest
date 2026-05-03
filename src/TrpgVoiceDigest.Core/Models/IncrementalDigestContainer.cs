using System.Text;
using System.Text.Json;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Core.Models;

public sealed class IncrementalDigestContainer : IIncrementalDataContainer
{
    public sealed class Entry
    {
        public int Key { get; set; }
        public string Content { get; set; } = "";
        public string[] Tags { get; set; } = [];
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private readonly Dictionary<int, Entry> _entries = new();
    private int _nextKey = 1;
    private readonly ILogService? _log;
    private readonly string _name;

    public string Title { get; set; }
    public int Count => _entries.Count;

    public IReadOnlyList<Entry> OrderedEntries =>
        _entries.Values.OrderBy(e => e.Key).ToList().AsReadOnly();

    public IncrementalDigestContainer(string name, string title, ILogService? log = null)
    {
        _name = name;
        Title = title;
        _log = log;
        _log?.Info($"[{_name}] 容器已创建 title=\"{title}\"");
    }

    public Entry AddEntry(string content, string[]? tags = null, int? afterKey = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            _log?.Debug($"[{_name}] AddEntry 跳过（空内容）");
            return null!;
        }

        int key;
        if (afterKey.HasValue && afterKey.Value > 0)
        {
            var candidate = afterKey.Value + 1;
            if (!_entries.ContainsKey(candidate))
                key = candidate;
            else
            {
                key = _nextKey;
                _log?.Debug($"[{_name}] AddEntry afterKey={afterKey} key+1={candidate} 已被占用，使用递增key={key}");
            }
        }
        else
        {
            key = _nextKey;
        }

        while (_entries.ContainsKey(key))
            key = ++_nextKey;

        var now = DateTimeOffset.UtcNow;
        var entry = new Entry
        {
            Key = key,
            Content = content.Trim(),
            Tags = tags ?? [],
            CreatedAt = now,
            UpdatedAt = now
        };
        _entries[key] = entry;

        _nextKey = Math.Max(_nextKey, key + 1);

        _log?.Info($"[{_name}] AddEntry key={key} content=\"{Truncate(content, 80)}\" "
            + $"afterKey={afterKey} tags=[{string.Join(",", entry.Tags)}] total={Count}");

        return entry;
    }

    public bool EditEntry(int key, string content, string[]? tags = null)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            _log?.Warning($"[{_name}] EditEntry 跳过（key={key} 不存在）total={Count} keys=[{KeysSummary()}]");
            return false;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            _log?.Debug($"[{_name}] EditEntry 跳过（空内容）key={key}");
            return false;
        }

        _log?.Info($"[{_name}] EditEntry key={key} "
            + $"old=\"{Truncate(entry.Content, 60)}\" → new=\"{Truncate(content, 60)}\"");

        entry.Content = content.Trim();
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        if (tags is not null)
            entry.Tags = tags;

        return true;
    }

    public bool RemoveEntry(int key)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            _log?.Warning($"[{_name}] RemoveEntry 跳过（key={key} 不存在）total={Count} keys=[{KeysSummary()}]");
            return false;
        }

        _log?.Info($"[{_name}] RemoveEntry key={key} content=\"{Truncate(entry.Content, 80)}\" "
            + $"remaining={Count - 1}");

        _entries.Remove(key);
        return true;
    }

    public Entry? GetEntry(int key) =>
        _entries.TryGetValue(key, out var entry) ? entry : null;

    private static readonly JsonSerializerOptions JsonExportOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ExportJson()
    {
        var entries = OrderedEntries.Select(e => new
        {
            key = e.Key,
            content = e.Content,
            tags = e.Tags.Length > 0 ? e.Tags : null,
            created_at = e.CreatedAt.ToString("O"),
            updated_at = e.UpdatedAt.ToString("O")
        });
        return JsonSerializer.Serialize(
            new { title = Title, entries }, JsonExportOptions);
    }

    public string ExportMarkdown()
    {
        if (Count == 0)
            return $"# {Title}\n\n暂无内容。\n";

        var sb = new StringBuilder();
        sb.AppendLine($"# {Title}");
        sb.AppendLine();
        foreach (var e in OrderedEntries)
            sb.AppendLine(e.Content);
        sb.AppendLine();
        return sb.ToString();
    }

    void IIncrementalDataContainer.ApplyOperations(IReadOnlyList<IOperation> operations)
    {
        _log?.Info($"[{_name}] ApplyOperations 收到 {operations.Count} 个原始操作 "
            + $"应用前 total={Count} keys=[{KeysSummary()}]");

        var typed = operations.OfType<RefineOperation>().ToList();
        if (typed.Count == 0)
        {
            _log?.Debug($"[{_name}] ApplyOperations 无 RefineOperation，跳过");
            return;
        }

        _log?.Info($"[{_name}] ApplyOperations 有效操作 {typed.Count} 个: "
            + string.Join("; ", typed.Select(o =>
                o.Action switch
                {
                    RefineAction.Add => $"Add(key={o.Number?.ToString() ?? "append"} text=\"{Truncate(o.Text ?? "", 40)}\")",
                    RefineAction.Edit => $"Edit(key={o.Number} text=\"{Truncate(o.Text ?? "", 40)}\")",
                    RefineAction.Remove => $"Remove(key={o.Number})",
                    RefineAction.Empty => "Empty",
                    _ => "?"
                })));

        foreach (var op in typed)
        {
            switch (op.Action)
            {
                case RefineAction.Add:
                    AddEntry(op.Text ?? "", null, op.Number);
                    break;
                case RefineAction.Edit:
                    if (op.Number.HasValue)
                        EditEntry(op.Number.Value, op.Text ?? "");
                    else
                        _log?.Warning($"[{_name}] ApplyOperations Edit 缺少编号, text=\"{Truncate(op.Text ?? "", 40)}\"");
                    break;
                case RefineAction.Remove:
                    if (op.Number.HasValue)
                        RemoveEntry(op.Number.Value);
                    else
                        _log?.Warning($"[{_name}] ApplyOperations Remove 缺少编号");
                    break;
                case RefineAction.Empty:
                    break;
            }
        }

        _log?.Info($"[{_name}] ApplyOperations 完成 "
            + $"应用后 total={Count} keys=[{KeysSummary()}]");
    }

    private string KeysSummary()
    {
        if (Count == 0) return "";
        var keys = _entries.Keys.OrderBy(k => k).ToList();
        if (keys.Count <= 10)
            return string.Join(",", keys);
        return $"{keys[0]}..{keys[0] + keys.Count - 1}({keys.Count} items, gaps exist)";
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "…";
}
