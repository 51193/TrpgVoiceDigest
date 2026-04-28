namespace TrpgVoiceDigest.Core.Models;

public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);

public sealed record DigestEntry(string Content, List<string> Tags);

public sealed record DigestTagGroup(string Tag, IReadOnlyList<(string Key, string Content)> Items);

public sealed class DigestState
{
    public Dictionary<string, DigestEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DigestTagGroup> GetTagGroups()
    {
        return Entries
            .SelectMany(x => x.Value.Tags.Select(tag => new { Tag = tag, Key = x.Key, Content = x.Value.Content }))
            .GroupBy(x => x.Tag)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DigestTagGroup(g.Key, g.Select(x => (x.Key, x.Content)).ToList()))
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

                    Entries[op.Key] = op.Value;
                    break;
                case EditAction.Remove:
                    if (op.Key is null)
                    {
                        continue;
                    }

                    Entries.Remove(op.Key);
                    break;
                case EditAction.Empty:
                    break;
            }
        }
    }
}
