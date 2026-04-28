using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrpgVoiceDigest.Core.Models;

public enum EditAction
{
    Add,
    Remove,
    Edit,
    Complete,
    Empty
}

public sealed record EditValue(DigestEntry? Digest, string? Text);

public sealed record EditOperation(EditAction Action, EntryArea Area, string? Key, EditValue? Value);

public static partial class EditProtocolParser
{
    [GeneratedRegex("^(?<area>digest|task|story)\\s+(?<action>add|edit)\\s+\"(?<key>.+?)\"\\s*:\\s*(?<json>\\{.+\\})\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AddEditRegex();

    [GeneratedRegex("^(?<area>digest|task|story)\\s+remove\\s+\"(?<key>.+?)\"\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RemoveRegex();
    
    [GeneratedRegex("^task\\s+complete\\s+\"(?<key>.+?)\"\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CompleteRegex();

    public static IReadOnlyList<EditOperation> Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response) ||
            string.Equals(response.Trim(), "EMPTY", StringComparison.OrdinalIgnoreCase))
        {
            return [new EditOperation(EditAction.Empty, EntryArea.Digest, null, null)];
        }

        var operations = new List<EditOperation>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var addEdit = AddEditRegex().Match(line);
            if (addEdit.Success)
            {
                var key = addEdit.Groups["key"].Value;
                var json = addEdit.Groups["json"].Value;
                var area = ParseArea(addEdit.Groups["area"].Value);
                if (area is null)
                {
                    continue;
                }

                var action = string.Equals(addEdit.Groups["action"].Value, "add", StringComparison.OrdinalIgnoreCase)
                    ? EditAction.Add
                    : EditAction.Edit;
                var value = ParseAddEditValue(area.Value, json);
                if (value is null)
                {
                    continue;
                }

                operations.Add(new EditOperation(action, area.Value, key, value));
                continue;
            }

            var remove = RemoveRegex().Match(line);
            if (remove.Success)
            {
                var area = ParseArea(remove.Groups["area"].Value);
                if (area is null)
                {
                    continue;
                }

                operations.Add(new EditOperation(EditAction.Remove, area.Value, remove.Groups["key"].Value, null));
                continue;
            }

            var complete = CompleteRegex().Match(line);
            if (complete.Success)
            {
                operations.Add(new EditOperation(EditAction.Complete, EntryArea.Task, complete.Groups["key"].Value, null));
            }
        }

        return operations.Count == 0
            ? [new EditOperation(EditAction.Empty, EntryArea.Digest, null, null)]
            : operations;
    }

    private static readonly JsonSerializerOptions JsonOptionsValue = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class DigestEntryPayload
    {
        public string Content { get; set; } = string.Empty;
        public List<string>? Tags { get; set; } = [];
    }

    private sealed class PlainTextPayload
    {
        public string Content { get; set; } = string.Empty;
    }

    private static EntryArea? ParseArea(string area)
    {
        if (string.Equals(area, "digest", StringComparison.OrdinalIgnoreCase))
        {
            return EntryArea.Digest;
        }

        if (string.Equals(area, "task", StringComparison.OrdinalIgnoreCase))
        {
            return EntryArea.Task;
        }

        if (string.Equals(area, "story", StringComparison.OrdinalIgnoreCase))
        {
            return EntryArea.Story;
        }

        return null;
    }

    private static EditValue? ParseAddEditValue(EntryArea area, string json)
    {
        if (area == EntryArea.Digest)
        {
            var value = JsonSerializer.Deserialize<DigestEntryPayload>(json, JsonOptionsValue);
            return value is null
                ? null
                : new EditValue(new DigestEntry(value.Content, value.Tags ?? []), null);
        }

        var plain = JsonSerializer.Deserialize<PlainTextPayload>(json, JsonOptionsValue);
        return plain is null ? null : new EditValue(null, plain.Content);
    }
}
