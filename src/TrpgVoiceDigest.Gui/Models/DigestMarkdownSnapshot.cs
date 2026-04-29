namespace TrpgVoiceDigest.Gui.Models;

public sealed record DigestMarkdownSnapshot(
    string Digest,
    string Consistency,
    string ActiveTasks,
    string CompletedTasks,
    string Story);
