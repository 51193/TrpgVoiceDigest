using System.Text;

namespace TrpgVoiceDigest.Core.Services;

public static class SpeakerIdentificationPromptComposer
{
    public static string BuildPrompt(
        IReadOnlyDictionary<string, string> unknownSpeakers,
        string dialogueLogText,
        IReadOnlyDictionary<string, string> speakerNameMap,
        string characterCards = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是一个语音识别系统的说话人身份推断助手。");
        sb.AppendLine();
        sb.AppendLine("以下说话人尚未被识别。请根据他们最近的发言内容以及上下文，推断他们的身份。");
        sb.AppendLine("**只在你非常确信时输出角色名称**。无法确定时，将其值设为 null。");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(characterCards))
        {
            sb.AppendLine(characterCards);
            sb.AppendLine();
        }

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

            // Collect all indices for this speaker
            var indices = new List<int>();
            for (var i = 0; i < parsedLines.Count; i++)
                if (parsedLines[i].Speaker.Equals(speakerId, StringComparison.OrdinalIgnoreCase))
                    indices.Add(i);

            if (indices.Count == 0)
            {
                sb.AppendLine("  (无对话)");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine($"共 {indices.Count} 次发言，以下为全部发言及上下文（[-2][-1]上下文 [+1][+2]上下文）：");
            sb.AppendLine();

            // Sample: if >20 utterances, take first 3 + last 12 for breadth
            var sampleIndices = indices;
            if (indices.Count > 20)
            {
                sampleIndices = indices.Take(3)
                    .Concat(indices.Skip(indices.Count - 12))
                    .Distinct()
                    .OrderBy(i => i)
                    .ToList();
            }

            var contextLines = new HashSet<int>();
            foreach (var idx in sampleIndices)
            {
                for (var offset = -2; offset <= 2; offset++)
                {
                    var ci = idx + offset;
                    if (ci >= 0 && ci < parsedLines.Count)
                        contextLines.Add(ci);
                }
            }

            var sortedContext = contextLines.OrderBy(i => i).ToList();
            var lastWrittenIdx = -3;
            foreach (var ci in sortedContext)
            {
                var (time, spk, text) = parsedLines[ci];
                var resolvedSpeaker = speakerNameMap.TryGetValue(spk, out var resolved)
                    ? resolved
                    : spk;
                var isTarget = spk.Equals(speakerId, StringComparison.OrdinalIgnoreCase);
                var marker = isTarget ? ">>> " : "    ";
                var prefix = marker + $" [{resolvedSpeaker}] [{time}]:";

                // Insert separator if gap > 1
                if (ci > lastWrittenIdx + 1)
                {
                    if (lastWrittenIdx >= 0)
                        sb.AppendLine();
                    sb.AppendLine($"  --- 时间跳跃 ({ci - lastWrittenIdx - 1} 条省略) ---");
                    sb.AppendLine();
                }

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
