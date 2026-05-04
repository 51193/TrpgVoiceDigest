using System.Text.RegularExpressions;

namespace TrpgVoiceDigest.Core.Models;

public static partial class TaskProtocolParser
{
    [GeneratedRegex(@"^task\s+add\s+(?<key>\d+)\s+""(?<text>(?:[^""\\]|\\.)*)""\s*$")]
    private static partial Regex AddWithKeyRegex();

    [GeneratedRegex(@"^task\s+add\s+""(?<text>(?:[^""\\]|\\.)*)""\s*$")]
    private static partial Regex AddRegex();

    [GeneratedRegex(@"^task\s+edit\s+(?<key>\d+)\s+""(?<text>(?:[^""\\]|\\.)*)""\s*$")]
    private static partial Regex EditRegex();

    [GeneratedRegex(@"^task\s+remove\s+(?<key>\d+)\s*$")]
    private static partial Regex RemoveRegex();

    [GeneratedRegex(@"^task\s+complete\s+(?<key>\d+)\s*$")]
    private static partial Regex CompleteRegex();

    [GeneratedRegex(@"^task\s+fail\s+(?<key>\d+)\s*$")]
    private static partial Regex FailRegex();

    public static IReadOnlyList<TaskOperation> Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new[] { new TaskOperation(TaskAction.Empty, null, null) };

        var results = new List<TaskOperation>();
        foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var match = AddWithKeyRegex().Match(trimmed);
            if (match.Success)
            {
                var key = int.Parse(match.Groups["key"].Value);
                results.Add(new TaskOperation(TaskAction.Add, key, Unescape(match.Groups["text"].Value)));
                continue;
            }

            match = AddRegex().Match(trimmed);
            if (match.Success)
            {
                results.Add(new TaskOperation(TaskAction.Add, null, Unescape(match.Groups["text"].Value)));
                continue;
            }

            match = EditRegex().Match(trimmed);
            if (match.Success)
            {
                results.Add(new TaskOperation(TaskAction.Edit, int.Parse(match.Groups["key"].Value),
                    Unescape(match.Groups["text"].Value)));
                continue;
            }

            match = RemoveRegex().Match(trimmed);
            if (match.Success)
            {
                results.Add(new TaskOperation(TaskAction.Remove, int.Parse(match.Groups["key"].Value), null));
                continue;
            }

            match = CompleteRegex().Match(trimmed);
            if (match.Success)
            {
                results.Add(new TaskOperation(TaskAction.Complete, int.Parse(match.Groups["key"].Value), null));
                continue;
            }

            match = FailRegex().Match(trimmed);
            if (match.Success)
                results.Add(new TaskOperation(TaskAction.Fail, int.Parse(match.Groups["key"].Value), null));
        }

        return results.Count > 0
            ? results
            : new[] { new TaskOperation(TaskAction.Empty, null, null) };
    }

    private static string Unescape(string text)
    {
        return text.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}

public sealed class TaskResponseParser : IResponseParser
{
    public IReadOnlyList<IOperation> Parse(string response)
    {
        var ops = TaskProtocolParser.Parse(response);
        return ops.Cast<IOperation>().ToList().AsReadOnly();
    }
}