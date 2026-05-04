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