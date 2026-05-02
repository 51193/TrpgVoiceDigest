using System.Text;
using System.Text.Json;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public static class RefinementPromptComposer
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };

    public static string BuildWindowedPrompt(
        string mergedDialogueText,
        RefinementState state,
        string refinementRequirementsPrompt,
        string protocolPrompt,
        IReadOnlyDictionary<string, string> speakerNameMap,
        RefinementConfig config)
    {
        var resolvedDialogue = ResolveSpeakerNamesInMerged(mergedDialogueText, speakerNameMap);

        var dialogueLines = resolvedDialogue.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var windowedDialogue = WindowLines(dialogueLines, config.MaxDialogueLines, config.MinContextChars);

        var stateSentences = state.Sentences.ToList(); // copy
        var windowedSentences = WindowSentences(stateSentences, config.MaxRefinementSentences, config.MaxContextChars);

        var stateJson = JsonSerializer.Serialize(new
        {
            sentences = windowedSentences.Select(s => new { s.Number, s.Text })
        }, IndentedOptions);

        var builder = new StringBuilder();
        builder.AppendLine(BuildSpeakerMappingTable(speakerNameMap, mergedDialogueText));
        builder.AppendLine("## 当前轮次合并对话（最近 " + windowedDialogue.Length + " 条）");
        builder.AppendLine(string.Join('\n', windowedDialogue));
        builder.AppendLine();
        builder.AppendLine("## 当前精炼状态（最近 " + windowedSentences.Count + " / 共 " + state.Sentences.Count + " 条）");
        builder.AppendLine(stateJson);
        builder.AppendLine();
        builder.AppendLine(refinementRequirementsPrompt);
        builder.AppendLine();
        builder.AppendLine("## 输出协议");
        builder.AppendLine(protocolPrompt);
        return builder.ToString();
    }

    private static string[] WindowLines(string[] lines, int maxLines, int minChars)
    {
        if (lines.Length <= maxLines)
            return lines;

        var take = maxLines;
        while (take < lines.Length)
        {
            var subset = lines.Skip(lines.Length - take).Take(take);
            var chars = subset.Sum(l => l.Length);
            if (chars >= minChars || take >= lines.Length)
                break;
            take = Math.Min(take + 5, lines.Length);
        }

        return lines.Skip(lines.Length - take).ToArray();
    }

    private static List<RefinedSentence> WindowSentences(List<RefinedSentence> sentences, int maxSentences, int maxChars)
    {
        if (sentences.Count <= maxSentences)
            return sentences;

        var keepFirst = Math.Min(5, sentences.Count / 4);
        var keepLast = maxSentences - keepFirst;

        var result = new List<RefinedSentence>();
        // 前N条（角色介绍等世界状态）
        for (var i = 0; i < keepFirst && i < sentences.Count; i++)
            result.Add(sentences[i]);

        // 后M条（近期上下文，供edit操作）
        var start = Math.Max(keepFirst, sentences.Count - keepLast);
        for (var i = start; i < sentences.Count; i++)
            result.Add(sentences[i]);

        // 检查总字符数，超限则缩减近期的句子数
        var totalTextLen = result.Sum(s => s.Text.Length);
        while (totalTextLen > maxChars && result.Count > keepFirst + 5)
        {
            // 从近期末尾开始移除
            var lastIdx = result.Count - 1;
            if (lastIdx <= keepFirst) break;
            totalTextLen -= result[lastIdx].Text.Length;
            result.RemoveAt(lastIdx);
        }

        // 按原编号排序
        result.Sort((a, b) => a.Number.CompareTo(b.Number));
        return result;
    }

    public static string BuildUserPrompt(
        string mergedDialogueText,
        RefinementState state,
        string refinementRequirementsPrompt,
        string protocolPrompt)
    {
        var stateJson = JsonSerializer.Serialize(new
        {
            sentences = state.Sentences.Select(s => new { s.Number, s.Text })
        }, IndentedOptions);

        var builder = new StringBuilder();
        builder.AppendLine("## 当前轮次合并对话（带句子编号）");
        builder.AppendLine(mergedDialogueText);
        builder.AppendLine();
        builder.AppendLine("## 当前精炼状态");
        builder.AppendLine(stateJson);
        builder.AppendLine();
        builder.AppendLine(refinementRequirementsPrompt);
        builder.AppendLine();
        builder.AppendLine("## 输出协议");
        builder.AppendLine(protocolPrompt);
        return builder.ToString();
    }

    public static string BuildUserPromptResolved(
        string mergedDialogueText,
        RefinementState state,
        string refinementRequirementsPrompt,
        string protocolPrompt,
        IReadOnlyDictionary<string, string> speakerNameMap)
    {
        var resolvedDialogue = ResolveSpeakerNamesInMerged(mergedDialogueText, speakerNameMap);
        var stateJson = JsonSerializer.Serialize(new
        {
            sentences = state.Sentences.Select(s => new { s.Number, s.Text })
        }, IndentedOptions);

        var builder = new StringBuilder();
        builder.AppendLine(BuildSpeakerMappingTable(speakerNameMap, mergedDialogueText));
        builder.AppendLine("## 当前轮次合并对话（带句子编号）");
        builder.AppendLine(resolvedDialogue);
        builder.AppendLine();
        builder.AppendLine("## 当前精炼状态");
        builder.AppendLine(stateJson);
        builder.AppendLine();
        builder.AppendLine(refinementRequirementsPrompt);
        builder.AppendLine();
        builder.AppendLine("## 输出协议");
        builder.AppendLine(protocolPrompt);
        return builder.ToString();
    }

    private static string BuildSpeakerMappingTable(
        IReadOnlyDictionary<string, string> speakerNameMap,
        string mergedDialogueText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 说话人映射表");
        sb.AppendLine();
        sb.AppendLine("| 声纹ID | 角色名 | 状态 |");
        sb.AppendLine("|:---|:---|:---|");

        var allSpeakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var speakerRegex = new System.Text.RegularExpressions.Regex(@"\[(?<speaker>[^\]]+)\]");
        foreach (var match in speakerRegex.Matches(mergedDialogueText).Cast<System.Text.RegularExpressions.Match>())
        {
            var speaker = match.Groups["speaker"].Value;
            if (speaker.StartsWith("speaker_", StringComparison.OrdinalIgnoreCase))
                allSpeakers.Add(speaker);
        }

        foreach (var speakerId in allSpeakers.OrderBy(s => s))
        {
            string name;
            string status;
            if (speakerNameMap.TryGetValue(speakerId, out var mapped) &&
                !string.Equals(mapped, speakerId, StringComparison.OrdinalIgnoreCase))
            {
                name = mapped;
                status = "已识别";
            }
            else
            {
                name = speakerId;
                status = "未识别";
            }
            sb.AppendLine($"| {speakerId} | {name} | {status} |");
        }

        if (allSpeakers.Count == 0)
            sb.AppendLine("| (无) | | |");

        sb.AppendLine();
        sb.AppendLine("> **重要**: 「未识别」的 speaker_X 需要你根据对话内容判断其真实身份。规则见精炼要求。");
        return sb.ToString();
    }

    private static string ResolveSpeakerNamesInMerged(string mergedDialogue,
        IReadOnlyDictionary<string, string> speakerNameMap)
    {
        if (speakerNameMap.Count == 0 || string.IsNullOrWhiteSpace(mergedDialogue)) return mergedDialogue;

        var sb = new StringBuilder();
        foreach (var line in mergedDialogue.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            var match = System.Text.RegularExpressions.Regex.Match(trimmed,
                @"^\[(?<num>\d+)\]\s*\[(?<speaker>[^\]]+)\]\s*\[(?<time>[^\]]+)\]:\s*(?<text>.+)$");
            if (!match.Success)
            {
                match = System.Text.RegularExpressions.Regex.Match(trimmed,
                    @"^\[(?<num>\d+)\]\s*\[(?<time>[^\]]+)\]:\s*(?<text>.+)$");
                if (match.Success)
                {
                    sb.AppendLine(line);
                    continue;
                }

                sb.AppendLine(line);
                continue;
            }

            var num = match.Groups["num"].Value;
            var speaker = match.Groups["speaker"].Value;
            var time = match.Groups["time"].Value;
            var text = match.Groups["text"].Value.Trim();

            if (speakerNameMap.TryGetValue(speaker, out var resolved))
                speaker = resolved;

            sb.AppendLine($"[{num}] [{speaker}] [{time}]: {text}");
        }

        return sb.ToString().TrimEnd();
    }
}
