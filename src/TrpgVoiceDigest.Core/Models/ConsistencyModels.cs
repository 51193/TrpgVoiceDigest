using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrpgVoiceDigest.Core.Models;

public sealed class ConsistencyEntry
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class ConsistencyState : IIncrementalDataContainer
{
    public List<ConsistencyEntry> Entries { get; init; } = [];

    void IIncrementalDataContainer.ApplyOperations(IReadOnlyList<IOperation> operations)
    {
        var typed = operations.OfType<ConsistencyOperation>().ToList();
        if (typed.Count > 0)
            ApplyOperations(typed);
    }

    public void ApplySet(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;
        var existing = Entries.Find(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            existing.Value = value.Trim();
        else
            Entries.Add(new ConsistencyEntry { Key = key.Trim(), Value = value.Trim() });
    }

    public void ApplyRemove(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        Entries.RemoveAll(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public void ApplyOperations(IReadOnlyList<ConsistencyOperation> operations)
    {
        foreach (var op in operations)
        {
            if (string.IsNullOrWhiteSpace(op.Key)) continue;
            switch (op.Action)
            {
                case ConsistencyAction.Set:
                    ApplySet(op.Key, op.Value ?? "");
                    break;
                case ConsistencyAction.Remove:
                    ApplyRemove(op.Key);
                    break;
            }
        }
    }

    public string BuildJson()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            var comma = i < Entries.Count - 1 ? "," : "";
            var escapedValue = JsonEncodedText.Encode(entry.Value).ToString();
            sb.AppendLine($"  \"{entry.Key}\": \"{escapedValue}\"{comma}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    public string BuildMarkdown()
    {
        if (Entries.Count == 0) return "# 上下文一致性表\n\n暂无条目。\n";

        var sb = new StringBuilder();
        sb.AppendLine("# 上下文一致性表");
        sb.AppendLine();
        sb.AppendLine("| 条目 | 说明 |");
        sb.AppendLine("|:---|:---|");
        foreach (var e in Entries)
            sb.AppendLine($"| {e.Key} | {e.Value} |");
        sb.AppendLine();
        return sb.ToString();
    }
}

public enum ConsistencyAction
{
    Set,
    Remove
}

public sealed record ConsistencyOperation(ConsistencyAction Action, string Key, string? Value) : IOperation;

public static partial class ConsistencyProtocolParser
{
    [GeneratedRegex("""^consistency\s+set\s+"([^"]*)"\s+"([^"]*)"\s*$""", RegexOptions.IgnoreCase)]
    private static partial Regex SetRegex();

    [GeneratedRegex("""^consistency\s+remove\s+"([^"]*)"\s*$""", RegexOptions.IgnoreCase)]
    private static partial Regex RemoveRegex();

    public static IReadOnlyList<ConsistencyOperation> Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response) ||
            string.Equals(response.Trim(), "EMPTY", StringComparison.OrdinalIgnoreCase))
            return [];

        var operations = new List<ConsistencyOperation>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var set = SetRegex().Match(line);
            if (set.Success)
            {
                operations.Add(new ConsistencyOperation(ConsistencyAction.Set,
                    set.Groups[1].Value, set.Groups[2].Value));
                continue;
            }

            var remove = RemoveRegex().Match(line);
            if (remove.Success)
                operations.Add(new ConsistencyOperation(ConsistencyAction.Remove,
                    remove.Groups[1].Value, null));
        }

        return operations;
    }
}

public sealed class ConsistencyResponseParser : IResponseParser
{
    public IReadOnlyList<IOperation> Parse(string response)
    {
        return ConsistencyProtocolParser.Parse(response)
            .Select(o => (IOperation)o)
            .ToList();
    }
}