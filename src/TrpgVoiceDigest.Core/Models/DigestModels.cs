namespace TrpgVoiceDigest.Core.Models;

public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);

public sealed record DigestEntry(string Content, List<string> Tags);

public sealed class DigestState
{
    public Dictionary<string, DigestEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

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
