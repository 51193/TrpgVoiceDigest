using System.Text;

namespace TrpgVoiceDigest.Core.Models;

public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text, string? Speaker = null);

public sealed record DialogueLine(DateTimeOffset Timestamp, string Speaker, string Text, int SequenceNumber)
{
    public string FormatRaw()
    {
        var speakerPart = Speaker.Length > 0 ? $"[{Speaker}]: " : "";
        return $"[{Timestamp:HH:mm:ss}] {speakerPart}{Text}";
    }

    public string FormatResolved(IReadOnlyDictionary<string, string> speakerNameMap)
    {
        var resolvedSpeaker = Speaker.Length > 0 && speakerNameMap.TryGetValue(Speaker, out var name)
            ? name
            : Speaker;
        var speakerPart = resolvedSpeaker.Length > 0 ? $"[{resolvedSpeaker}]: " : "";
        return $"[{Timestamp:HH:mm:ss}] {speakerPart}{Text}";
    }
}

public sealed record DigestEntry(string Content, List<string> Tags);

public sealed record DigestTagGroup(string Tag, IReadOnlyList<(string Key, string Content)> Items);

public enum EntryArea
{
    Digest,
    Task,
    Story
}

public sealed class DigestState
{
    public const string ConsistencyTag = "LLM_Consistency";

    public Dictionary<string, DigestEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ActiveTasks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CompletedTasks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> StoryEntries { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<DigestTagGroup> GetTagGroups()
    {
        return Entries
            .SelectMany(x => x.Value.Tags.Select(tag => new { Tag = tag, x.Key, x.Value.Content }))
            .GroupBy(x => x.Tag)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DigestTagGroup(g.Key, g.Select(x => (x.Key, x.Content)).ToList()))
            .ToList();
    }

    public IReadOnlyList<DigestTagGroup> GetTagGroupsByTag(string tag)
    {
        return FilterTagGroups(tag, true);
    }

    public IReadOnlyList<DigestTagGroup> GetTagGroupsExcludingTag(string tag)
    {
        return FilterTagGroups(tag, false);
    }

    private IReadOnlyList<DigestTagGroup> FilterTagGroups(string? tag, bool include)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return include ? [] : GetTagGroups();

        return GetTagGroups()
            .Where(x => string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase) == include)
            .ToList();
    }

    public void Apply(IReadOnlyList<EditOperation> operations)
    {
        foreach (var op in operations)
            switch (op.Action)
            {
                case EditAction.Add:
                case EditAction.Edit:
                    if (op.Key is null || op.Value is null) continue;

                    ApplyEntryUpsert(op.Area, op.Key, op.Value);
                    break;
                case EditAction.Remove:
                    if (op.Key is null) continue;

                    ApplyEntryRemove(op.Area, op.Key);
                    break;
                case EditAction.Complete:
                    if (op.Area != EntryArea.Task || op.Key is null) continue;

                    CompleteTask(op.Key);
                    break;
                case EditAction.Empty:
                    break;
            }
    }

    public string BuildDigestMarkdown()
    {
        return BuildGroupedSection("# 当前摘录", GetTagGroupsExcludingTag(ConsistencyTag), "暂无摘录条目。");
    }

    public string BuildConsistencyMarkdown()
    {
        return BuildGroupedSection("# 一致性参考", GetTagGroupsByTag(ConsistencyTag), "暂无一致性条目。");
    }

    public string BuildActiveTasksMarkdown()
    {
        return BuildKvpSection("# 活跃任务", ActiveTasks, "暂无活跃任务。");
    }

    public string BuildCompletedTasksMarkdown()
    {
        return BuildKvpSection("# 已完成任务", CompletedTasks, "暂无已完成任务。");
    }

    public string BuildStoryMarkdown()
    {
        return BuildKvpSection("# 故事进展", StoryEntries, "暂无故事进展记录。");
    }

    public static string BuildGroupedSection(string title, IReadOnlyList<DigestTagGroup> groups,
        string emptyText = "- (none)")
    {
        if (groups.Count == 0)
            return $"{title}\n\n{emptyText}\n";

        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine();
        foreach (var group in groups)
        {
            sb.AppendLine($"## {group.Tag}");
            foreach (var (key, content) in group.Items)
                sb.AppendLine($"- **{key}**: {content}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string BuildKvpSection(string title, IReadOnlyDictionary<string, string> entries,
        string emptyText = "- (none)")
    {
        if (entries.Count == 0)
            return $"{title}\n\n{emptyText}\n";

        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine();
        foreach (var pair in entries.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"- **{pair.Key}**: {pair.Value}");

        return sb.ToString();
    }

    private void ApplyEntryUpsert(EntryArea area, string key, EditValue value)
    {
        switch (area)
        {
            case EntryArea.Digest:
                Entries[key] = value.Digest ?? new DigestEntry(string.Empty, []);
                break;
            case EntryArea.Task:
                if (value.Text is null) return;

                ActiveTasks[key] = value.Text;
                break;
            case EntryArea.Story:
                if (value.Text is null) return;

                StoryEntries[key] = value.Text;
                break;
        }
    }

    private void ApplyEntryRemove(EntryArea area, string key)
    {
        switch (area)
        {
            case EntryArea.Digest:
                Entries.Remove(key);
                break;
            case EntryArea.Task:
                ActiveTasks.Remove(key);
                break;
            case EntryArea.Story:
                StoryEntries.Remove(key);
                break;
        }
    }

    private void CompleteTask(string key)
    {
        if (!ActiveTasks.TryGetValue(key, out var content)) return;

        ActiveTasks.Remove(key);
        CompletedTasks[key] = content;
    }
}