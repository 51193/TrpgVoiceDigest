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

    public static IReadOnlyDictionary<string, string> BuildRefinementData(
        string dialogueLogText,
        IncrementalDigestContainer state,
        IReadOnlyDictionary<string, string> speakerNameMap,
        RefinementConfig config)
    {
        var resolvedDialogue = ResolveSpeakerNames(dialogueLogText, speakerNameMap);

        var dialogueLines = resolvedDialogue.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var windowedDialogue = WindowLines(dialogueLines, config.MaxDialogueLines, config.MinContextChars);

        var stateEntries = state.OrderedEntries.ToList();
        var windowedEntries = WindowEntries(stateEntries, config.MaxRefinementSentences, config.MaxContextChars);

        var stateJson = JsonSerializer.Serialize(new
        {
            sentences = windowedEntries.Select(s => new { number = s.Key, text = s.Content })
        }, IndentedOptions);

        var data = new Dictionary<string, string>
        {
            ["speaker_mapping_section"] = BuildSpeakerMappingTable(speakerNameMap, dialogueLogText),
            ["dialogue_label"] = "最近 " + windowedDialogue.Length + " 条",
            ["dialogue_text"] = string.Join('\n', windowedDialogue),
            ["state_label"] = "最近 " + windowedEntries.Count + " / 共 " + state.Count + " 条",
            ["state_json"] = stateJson,
        };

        EnforceBudget(data, config);
        return data;
    }

    private static void EnforceBudget(Dictionary<string, string> data, RefinementConfig config)
    {
        if (config.TotalPromptBudgetChars <= 0) return;

        var total = data.Values.Sum(v => v.Length);
        if (total <= config.TotalPromptBudgetChars) return;

        var excess = total - config.TotalPromptBudgetChars;
        var dialogueTarget = Math.Max(200, data["dialogue_text"].Length - excess);
        var trimmed = PromptWindowHelper.TrimLinesFromStart(data["dialogue_text"], dialogueTarget);
        if (trimmed.Length < data["dialogue_text"].Length)
        {
            data["dialogue_text"] = trimmed;
            data["dialogue_label"] = "最近 " + trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length + " 条";
        }
    }

    public static string BuildWindowedPrompt(
        string dialogueLogText,
        IncrementalDigestContainer state,
        string refinementRequirementsPrompt,
        string protocolPrompt,
        IReadOnlyDictionary<string, string> speakerNameMap,
        RefinementConfig config)
    {
        var data = BuildRefinementData(dialogueLogText, state, speakerNameMap, config);

        var builder = new StringBuilder();
        builder.AppendLine(data["speaker_mapping_section"]);
        builder.AppendLine("## 当前轮次对话（" + data["dialogue_label"] + "）");
        builder.AppendLine(data["dialogue_text"]);
        builder.AppendLine();
        builder.AppendLine("## 当前精炼状态（" + data["state_label"] + "）");
        builder.AppendLine(data["state_json"]);
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

    private static List<IncrementalDigestContainer.Entry> WindowEntries(
        List<IncrementalDigestContainer.Entry> entries, int maxEntries, int maxChars)
    {
        if (entries.Count <= maxEntries)
            return entries;

        var keepFirst = Math.Min(5, entries.Count / 4);
        var keepLast = maxEntries - keepFirst;

        var result = new List<IncrementalDigestContainer.Entry>();
        for (var i = 0; i < keepFirst && i < entries.Count; i++)
            result.Add(entries[i]);

        var start = Math.Max(keepFirst, entries.Count - keepLast);
        for (var i = start; i < entries.Count; i++)
            result.Add(entries[i]);

        var totalTextLen = result.Sum(s => s.Content.Length);
        while (totalTextLen > maxChars && result.Count > keepFirst + 5)
        {
            var lastIdx = result.Count - 1;
            if (lastIdx <= keepFirst) break;
            totalTextLen -= result[lastIdx].Content.Length;
            result.RemoveAt(lastIdx);
        }

        result.Sort((a, b) => a.Key.CompareTo(b.Key));
        return result;
    }

    public static string BuildUserPrompt(
        string mergedDialogueText,
        IncrementalDigestContainer state,
        string refinementRequirementsPrompt,
        string protocolPrompt)
    {
        var stateJson = JsonSerializer.Serialize(new
        {
            sentences = state.OrderedEntries.Select(s => new { number = s.Key, text = s.Content })
        }, IndentedOptions);

        var builder = new StringBuilder();
        builder.AppendLine("## 当前轮次对话（带句子编号）");
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
        string dialogueLogText,
        IncrementalDigestContainer state,
        string refinementRequirementsPrompt,
        string protocolPrompt,
        IReadOnlyDictionary<string, string> speakerNameMap)
    {
        var resolvedDialogue = ResolveSpeakerNames(dialogueLogText, speakerNameMap);
        var stateJson = JsonSerializer.Serialize(new
        {
            sentences = state.OrderedEntries.Select(s => new { number = s.Key, text = s.Content })
        }, IndentedOptions);

        var builder = new StringBuilder();
        builder.AppendLine(BuildSpeakerMappingTable(speakerNameMap, dialogueLogText));
        builder.AppendLine("## 当前轮次对话（带句子编号）");
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
        string dialogueLogText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 说话人映射表");
        sb.AppendLine();
        sb.AppendLine("| 声纹ID | 角色名 | 状态 |");
        sb.AppendLine("|:---|:---|:---|");

        var allSpeakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var speakerRegex = new System.Text.RegularExpressions.Regex(@"\[(?<speaker>[^\]]+)\]");
        foreach (var match in speakerRegex.Matches(dialogueLogText).Cast<System.Text.RegularExpressions.Match>())
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

    private static string ResolveSpeakerNames(string dialogueLog,
        IReadOnlyDictionary<string, string> speakerNameMap)
    {
        if (speakerNameMap.Count == 0 || string.IsNullOrWhiteSpace(dialogueLog)) return dialogueLog;

        var sb = new StringBuilder();
        foreach (var line in dialogueLog.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var match = System.Text.RegularExpressions.Regex.Match(trimmed,
                @"^\[(?<time>\d{2}:\d{2}:\d{2})\]\s*\[(?<speaker>[^\]]+)\]:\s*(?<text>.+)$");
            if (!match.Success)
            {
                sb.AppendLine(line);
                continue;
            }

            var time = match.Groups["time"].Value;
            var speaker = match.Groups["speaker"].Value;
            var text = match.Groups["text"].Value.Trim();

            if (speakerNameMap.TryGetValue(speaker, out var resolved))
            {
                if (string.Equals(resolved, speaker, StringComparison.OrdinalIgnoreCase))
                    speaker = "暂未分辨";
                else
                    speaker = resolved;
            }

            sb.AppendLine($"[{time}] [{speaker}]: {text}");
        }

        return sb.ToString().TrimEnd();
    }
}
