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
        RefinementConfig config,
        string characterCards = "",
        bool useDialogueWindow = true)
    {
        var resolvedDialogue = DialogueFormatter.Resolve(dialogueLogText, speakerNameMap, resolveSpeakers: true);

        var dialogueLines = resolvedDialogue.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] windowedDialogue;
        if (useDialogueWindow)
        {
            windowedDialogue = WindowLines(dialogueLines, config.MaxDialogueLines, config.MinContextChars);
        }
        else
        {
            windowedDialogue = dialogueLines;
        }

        var stateEntries = state.OrderedEntries.ToList();
        var windowedEntries = WindowEntries(stateEntries, config.MaxRefinementSentences, config.MaxContextChars);

        var stateJson = JsonSerializer.Serialize(new
        {
            sentences = windowedEntries.Select(s => new { number = s.Key, text = s.Content })
        }, IndentedOptions);

        var data = new Dictionary<string, string>
        {
            ["character_cards"] = characterCards,
            ["dialogue_label"] = useDialogueWindow
                ? "最近 " + windowedDialogue.Length + " 条"
                : "共 " + windowedDialogue.Length + " 条",
            ["dialogue_text"] = string.Join('\n', windowedDialogue),
            ["state_label"] = "最近 " + windowedEntries.Count + " / 共 " + state.Count + " 条",
            ["state_json"] = stateJson,
        };

        EnforceBudget(data, config, useDialogueWindow);
        return data;
    }

    private static void EnforceBudget(Dictionary<string, string> data, RefinementConfig config, bool trimDialogue)
    {
        if (config.TotalPromptBudgetChars <= 0 || !trimDialogue) return;

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
}
