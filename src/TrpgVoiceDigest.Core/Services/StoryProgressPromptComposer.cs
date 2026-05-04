using System.Text.Json;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public static class StoryProgressPromptComposer
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    public static IReadOnlyDictionary<string, string> BuildStoryProgressData(
        string refinementText,
        StoryProgressState storyState,
        TaskState taskState,
        StoryProgressConfig config,
        string characterCards = "")
    {
        var refinementLines = refinementText.Split('\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var stateEntries = storyState.OrderedEntries.ToList();
        var windowedEntries = WindowEntries(stateEntries, config.MaxStoryEntries, config.MaxContextChars);

        var stateJson = JsonSerializer.Serialize(new
        {
            entries = windowedEntries.Select(s => new { key = s.Key, text = s.Text })
        }, IndentedOptions);

        var taskJson = taskState.ExportActiveJson();

        return new Dictionary<string, string>
        {
            ["character_cards"] = characterCards,
            ["refinement_label"] = "共 " + refinementLines.Length + " 行",
            ["refinement_text"] = string.Join('\n', refinementLines),
            ["state_label"] = "最近 " + windowedEntries.Count + " / 共 " + storyState.Count + " 条",
            ["state_json"] = stateJson,
            ["task_state_label"] = "共 " + taskState.ActiveCount + " 条活跃任务",
            ["task_state_json"] = taskJson
        };
    }

    private static List<StoryProgressEntry> WindowEntries(
        List<StoryProgressEntry> entries, int maxEntries, int maxChars)
    {
        if (entries.Count <= maxEntries)
            return entries;

        var keepFirst = Math.Min(3, entries.Count / 4);
        var keepLast = maxEntries - keepFirst;

        var result = new List<StoryProgressEntry>();
        for (var i = 0; i < keepFirst && i < entries.Count; i++)
            result.Add(entries[i]);

        var start = Math.Max(keepFirst, entries.Count - keepLast);
        for (var i = start; i < entries.Count; i++)
            result.Add(entries[i]);

        var totalTextLen = result.Sum(s => s.Text.Length);
        while (totalTextLen > maxChars && result.Count > keepFirst + 3)
        {
            var lastIdx = result.Count - 1;
            if (lastIdx <= keepFirst) break;
            totalTextLen -= result[lastIdx].Text.Length;
            result.RemoveAt(lastIdx);
        }

        result.Sort((a, b) => a.Key.CompareTo(b.Key));
        return result;
    }
}