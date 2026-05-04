using System.Text.RegularExpressions;

namespace TrpgVoiceDigest.Core.Models;

public static partial class StoryProgressProtocolParser
{
    [GeneratedRegex(@"^story\s+add\s+(?<key>\d+)\s+""(?<text>(?:[^""\\]|\\.)*)""\s*$")]
    private static partial Regex AddWithKeyRegex();

    [GeneratedRegex(@"^story\s+add\s+""(?<text>(?:[^""\\]|\\.)*)""\s*$")]
    private static partial Regex AddRegex();

    [GeneratedRegex(@"^story\s+edit\s+(?<key>\d+)\s+""(?<text>(?:[^""\\]|\\.)*)""\s*$")]
    private static partial Regex EditRegex();

    [GeneratedRegex(@"^story\s+remove\s+(?<key>\d+)\s*$")]
    private static partial Regex RemoveRegex();

    public static IReadOnlyList<StoryOperation> Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response) || response.Trim() == "EMPTY")
            return new[] { new StoryOperation(StoryAction.Empty, null, null) };

        var results = new List<StoryOperation>();
        foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var match = AddWithKeyRegex().Match(trimmed);
            if (match.Success)
            {
                var key = int.Parse(match.Groups["key"].Value);
                var text = Unescape(match.Groups["text"].Value);
                results.Add(new StoryOperation(StoryAction.Add, key, text));
                continue;
            }

            match = AddRegex().Match(trimmed);
            if (match.Success)
            {
                results.Add(new StoryOperation(StoryAction.Add, null, Unescape(match.Groups["text"].Value)));
                continue;
            }

            match = EditRegex().Match(trimmed);
            if (match.Success)
            {
                var key = int.Parse(match.Groups["key"].Value);
                results.Add(new StoryOperation(StoryAction.Edit, key, Unescape(match.Groups["text"].Value)));
                continue;
            }

            match = RemoveRegex().Match(trimmed);
            if (match.Success)
                results.Add(new StoryOperation(StoryAction.Remove, int.Parse(match.Groups["key"].Value), null));
        }

        return results.Count > 0
            ? results
            : new[] { new StoryOperation(StoryAction.Empty, null, null) };
    }

    private static string Unescape(string text)
    {
        return text.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}

public sealed class StoryProgressResponseParser : IResponseParser
{
    public IReadOnlyList<IOperation> Parse(string response)
    {
        var ops = StoryProgressProtocolParser.Parse(response);
        return ops.Cast<IOperation>().ToList().AsReadOnly();
    }
}