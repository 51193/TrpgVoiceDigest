using System.Text;

namespace TrpgVoiceDigest.Core.Services;

public static class SpeakerIdentificationPromptComposer
{
    public static string BuildPrompt(
        IReadOnlyDictionary<string, string> unknownSpeakers,
        string dialogueLogText,
        IReadOnlyDictionary<string, string> speakerNameMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是一个语音识别系统的说话人身份推断助手。");
        sb.AppendLine();
        sb.AppendLine("以下说话人尚未被识别。请根据他们最近的发言内容以及上下文，推断他们的身份。");
        sb.AppendLine("**只在你非常确信时输出角色名称**。无法确定时，将其值设为 null。");
        sb.AppendLine();

        var lines = dialogueLogText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsedLines = new List<(string Time, string Speaker, string Text)>();
        foreach (var line in lines)
        {
            var match = System.Text.RegularExpressions.Regex.Match(line,
                @"^\[(?<time>\d{2}:\d{2}:\d{2})\]\s*\[(?<speaker>[^\]]+)\]:\s*(?<text>.+)$");
            if (!match.Success) continue;
            parsedLines.Add((match.Groups["time"].Value, match.Groups["speaker"].Value, match.Groups["text"].Value.Trim()));
        }

        foreach (var speakerId in unknownSpeakers.Keys.OrderBy(k => k))
        {
            sb.AppendLine($"### {speakerId}");
            sb.AppendLine();
            sb.AppendLine("最近发言及上下文（[-1] [+1] 为该说话人发言的前后一行）：");
            sb.AppendLine();

            // Collect all lines for this speaker
            var indices = new List<int>();
            for (var i = 0; i < parsedLines.Count; i++)
                if (parsedLines[i].Speaker.Equals(speakerId, StringComparison.OrdinalIgnoreCase))
                    indices.Add(i);

            // Take last 5 utterances
            var sampleIndices = indices.Skip(Math.Max(0, indices.Count - 5)).ToList();
            var contextLines = new HashSet<int>();
            foreach (var idx in sampleIndices)
            {
                for (var offset = -1; offset <= 1; offset++)
                {
                    var ci = idx + offset;
                    if (ci >= 0 && ci < parsedLines.Count)
                        contextLines.Add(ci);
                }
            }

            var sortedContext = contextLines.OrderBy(i => i).ToList();
            var lastWrittenIdx = -2;
            foreach (var ci in sortedContext)
            {
                var (time, spk, text) = parsedLines[ci];
                var resolvedSpeaker = speakerNameMap.TryGetValue(spk, out var resolved)
                    ? resolved
                    : spk;
                var marker = spk.Equals(speakerId, StringComparison.OrdinalIgnoreCase) ? ">>> " : "    ";
                var prefix = marker + $" [{resolvedSpeaker}] [{time}]:";

                // Insert separator if non-continuous
                if (ci > lastWrittenIdx + 1)
                    sb.AppendLine("  ...");

                sb.AppendLine($"{prefix} {text}");
                lastWrittenIdx = ci;
            }
            sb.AppendLine();
        }

        sb.AppendLine("请以 JSON 格式返回（只输出 JSON 字典，不要任何其他内容）：");
        sb.Append("{");
        var first = true;
        foreach (var speakerId in unknownSpeakers.Keys.OrderBy(k => k))
        {
            if (!first) sb.Append(", ");
            sb.Append($"\"{speakerId}\": null");
            first = false;
        }
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("将 null 替换为角色名称（如 \"DM\", \"梅林\", \"酒馆老板\" 等）。");
        sb.AppendLine("如果无法确定，保持 null。");

        return sb.ToString();
    }
}
