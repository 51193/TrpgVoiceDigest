namespace TrpgVoiceDigest.Core.Models;

public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);

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

    public IReadOnlyList<DigestTagGroup> GetTagGroups()
    {
        return Entries
            .SelectMany(x => x.Value.Tags.Select(tag => new { Tag = tag, Key = x.Key, Content = x.Value.Content }))
            .GroupBy(x => x.Tag)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DigestTagGroup(g.Key, g.Select(x => (x.Key, x.Content)).ToList()))
            .ToList();
    }

    public IReadOnlyList<DigestTagGroup> GetTagGroupsByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return [];
        }

        return GetTagGroups()
            .Where(x => string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IReadOnlyList<DigestTagGroup> GetTagGroupsExcludingTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return GetTagGroups();
        }

        return GetTagGroups()
            .Where(x => !string.Equals(x.Tag, tag, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void Apply(IReadOnlyList<EditOperation> operations)
    {
        foreach (var op in operations)
        {
            switch (op.Action)
            {
                case EditAction.Add:
                case EditAction.Edit:
                    if (op.Key is null || op.Value is null)
                    {
                        continue;
                    }
                    
                    ApplyEntryUpsert(op.Area, op.Key, op.Value);
                    break;
                case EditAction.Remove:
                    if (op.Key is null)
                    {
                        continue;
                    }
                    
                    ApplyEntryRemove(op.Area, op.Key);
                    break;
                case EditAction.Complete:
                    if (op.Area != EntryArea.Task || op.Key is null)
                    {
                        continue;
                    }

                    CompleteTask(op.Key);
                    break;
                case EditAction.Empty:
                    break;
            }
        }
    }

    private void ApplyEntryUpsert(EntryArea area, string key, EditValue value)
    {
        switch (area)
        {
            case EntryArea.Digest:
                Entries[key] = value.Digest ?? new DigestEntry(string.Empty, []);
                break;
            case EntryArea.Task:
                if (value.Text is null)
                {
                    return;
                }

                ActiveTasks[key] = value.Text;
                break;
            case EntryArea.Story:
                if (value.Text is null)
                {
                    return;
                }

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
        if (!ActiveTasks.TryGetValue(key, out var content))
        {
            return;
        }

        ActiveTasks.Remove(key);
        CompletedTasks[key] = content;
    }
}
