using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrpgVoiceDigest.Core.Models;

public enum EditAction
{
    Add,
    Remove,
    Edit,
    Empty
}

public sealed record EditOperation(EditAction Action, string? Key, DigestEntry? Value);

public static partial class EditProtocolParser
{
    [GeneratedRegex("^(add|edit)\\s+\"(?<key>.+?)\"\\s*:\\s*(?<json>\\{.+\\})\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AddEditRegex();

    [GeneratedRegex("^remove\\s+\"(?<key>.+?)\"\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RemoveRegex();

    public static IReadOnlyList<EditOperation> Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response) ||
            string.Equals(response.Trim(), "EMPTY", StringComparison.OrdinalIgnoreCase))
        {
            return [new EditOperation(EditAction.Empty, null, null)];
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
                var value = JsonSerializer.Deserialize<DigestEntryPayload>(json, JsonOptions());
                if (value is null)
                {
                    continue;
                }

                var action = line.StartsWith("add", StringComparison.OrdinalIgnoreCase)
                    ? EditAction.Add
                    : EditAction.Edit;
                operations.Add(new EditOperation(action, key, new DigestEntry(value.Content, value.Tags ?? [])));
                continue;
            }

            var remove = RemoveRegex().Match(line);
            if (remove.Success)
            {
                operations.Add(new EditOperation(EditAction.Remove, remove.Groups["key"].Value, null));
            }
        }

        return operations.Count == 0
            ? [new EditOperation(EditAction.Empty, null, null)]
            : operations;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class DigestEntryPayload
    {
        public string Content { get; set; } = string.Empty;
        public List<string>? Tags { get; set; } = [];
    }
}
