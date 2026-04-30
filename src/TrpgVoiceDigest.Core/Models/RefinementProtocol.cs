using System.Text.RegularExpressions;

namespace TrpgVoiceDigest.Core.Models;

public static partial class RefinementProtocolParser
{
    [GeneratedRegex("^refine\\s+add\\s+(\\d+)\\s+\"(.+)\"\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AddWithPositionRegex();

    [GeneratedRegex("^refine\\s+add\\s+\"(.+)\"\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AddWithoutPositionRegex();

    [GeneratedRegex("^refine\\s+edit\\s+(\\d+)\\s+\"(.+)\"\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex EditRegex();

    [GeneratedRegex("^refine\\s+remove\\s+(\\d+)\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RemoveRegex();

    public static IReadOnlyList<RefineOperation> Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response) ||
            string.Equals(response.Trim(), "EMPTY", StringComparison.OrdinalIgnoreCase))
            return [new RefineOperation(RefineAction.Empty, null, null)];

        var operations = new List<RefineOperation>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var addWithPos = AddWithPositionRegex().Match(line);
            if (addWithPos.Success)
            {
                var number = int.Parse(addWithPos.Groups[1].Value);
                var text = addWithPos.Groups[2].Value;
                operations.Add(new RefineOperation(RefineAction.Add, number, text));
                continue;
            }

            var addWithoutPos = AddWithoutPositionRegex().Match(line);
            if (addWithoutPos.Success)
            {
                var text = addWithoutPos.Groups[1].Value;
                operations.Add(new RefineOperation(RefineAction.Add, null, text));
                continue;
            }

            var edit = EditRegex().Match(line);
            if (edit.Success)
            {
                var number = int.Parse(edit.Groups[1].Value);
                var text = edit.Groups[2].Value;
                operations.Add(new RefineOperation(RefineAction.Edit, number, text));
                continue;
            }

            var remove = RemoveRegex().Match(line);
            if (remove.Success)
            {
                var number = int.Parse(remove.Groups[1].Value);
                operations.Add(new RefineOperation(RefineAction.Remove, number, null));
            }
        }

        return operations.Count == 0
            ? [new RefineOperation(RefineAction.Empty, null, null)]
            : operations;
    }
}
