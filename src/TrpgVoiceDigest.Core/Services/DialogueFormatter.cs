using System.Text;
using System.Text.RegularExpressions;

namespace TrpgVoiceDigest.Core.Services;

public static partial class DialogueFormatter
{
    [GeneratedRegex(@"^\[(?<time>\d{2}:\d{2}:\d{2})\]\s*\[(?<speaker>[^\]]+)\]:\s*(?<text>.+)$")]
    private static partial Regex LineRegex();

    public static string Resolve(string dialogueLog,
        IReadOnlyDictionary<string, string> speakerNameMap,
        bool resolveSpeakers)
    {
        if (string.IsNullOrWhiteSpace(dialogueLog)) return dialogueLog;

        var sb = new StringBuilder();
        foreach (var line in dialogueLog.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var match = LineRegex().Match(trimmed);
            if (!match.Success)
            {
                sb.AppendLine(line);
                continue;
            }

            var time = match.Groups["time"].Value;
            var speaker = match.Groups["speaker"].Value;
            var text = match.Groups["text"].Value.Trim();

            if (resolveSpeakers && speakerNameMap.Count > 0 &&
                speakerNameMap.TryGetValue(speaker, out var resolved))
            {
                speaker = string.Equals(resolved, speaker, StringComparison.OrdinalIgnoreCase)
                    ? "暂未分辨"
                    : resolved;
            }

            sb.AppendLine($"[{time}] [{speaker}]: {text}");
        }

        return sb.ToString().TrimEnd();
    }
}
