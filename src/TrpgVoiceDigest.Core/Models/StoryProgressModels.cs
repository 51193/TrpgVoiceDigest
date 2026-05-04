using System.Text;
using System.Text.Json;

namespace TrpgVoiceDigest.Core.Models;

public enum StoryAction
{
    Add,
    Edit,
    Remove,
    Empty
}

public sealed record StoryOperation(StoryAction Action, int? Key, string? Text) : IOperation;

public sealed class StoryProgressEntry
{
    public int Key { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class StoryProgressState : IIncrementalDataContainer
{
    private readonly Dictionary<int, StoryProgressEntry> _entries = new();
    private int _nextKey = 1;

    public string Title { get; set; } = "故事进展";
    public int Count => _entries.Count;

    public IReadOnlyList<StoryProgressEntry> OrderedEntries =>
        _entries.Values.OrderBy(e => e.Key).ToList().AsReadOnly();

    public StoryProgressEntry AddEntry(string text, int? afterKey = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null!;

        int key;
        if (afterKey.HasValue && afterKey.Value > 0)
        {
            var candidate = afterKey.Value + 1;
            key = !_entries.ContainsKey(candidate) ? candidate : _nextKey;
        }
        else
        {
            key = _nextKey;
        }

        while (_entries.ContainsKey(key))
            key = ++_nextKey;

        var now = DateTimeOffset.UtcNow;
        var entry = new StoryProgressEntry
        {
            Key = key,
            Text = text.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        _entries[key] = entry;
        _nextKey = Math.Max(_nextKey, key + 1);
        return entry;
    }

    public bool EditEntry(int key, string text)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return false;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        entry.Text = text.Trim();
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    public bool RemoveEntry(int key)
    {
        return _entries.Remove(key);
    }

    public StoryProgressEntry? GetEntry(int key) =>
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
            text = e.Text,
            created_at = e.CreatedAt.ToString("O"),
            updated_at = e.UpdatedAt.ToString("O")
        });
        return JsonSerializer.Serialize(new { title = Title, entries }, JsonExportOptions);
    }

    public string ExportMarkdown()
    {
        if (Count == 0)
            return $"# {Title}\n\n暂无内容。\n";

        var sb = new StringBuilder();
        sb.AppendLine($"# {Title}");
        sb.AppendLine();
        foreach (var e in OrderedEntries)
            sb.AppendLine($"{e.Key}. {e.Text}");
        sb.AppendLine();
        return sb.ToString();
    }

    public void ApplyOperations(IReadOnlyList<StoryOperation> operations)
    {
        foreach (var op in operations)
        {
            switch (op.Action)
            {
                case StoryAction.Add:
                    AddEntry(op.Text ?? "", op.Key);
                    break;
                case StoryAction.Edit:
                    if (op.Key.HasValue)
                        EditEntry(op.Key.Value, op.Text ?? "");
                    break;
                case StoryAction.Remove:
                    if (op.Key.HasValue)
                        RemoveEntry(op.Key.Value);
                    break;
                case StoryAction.Empty:
                    break;
            }
        }
    }

    void IIncrementalDataContainer.ApplyOperations(IReadOnlyList<IOperation> operations)
    {
        var typed = operations.OfType<StoryOperation>().ToList();
        if (typed.Count > 0)
            ApplyOperations(typed);
    }
}
