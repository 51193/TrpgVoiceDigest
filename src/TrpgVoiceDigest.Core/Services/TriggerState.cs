namespace TrpgVoiceDigest.Core.Services;

public sealed class TriggerState
{
    public int SentencesSinceLastRun { get; private set; }
    public DateTimeOffset LastRunAt { get; private set; } = DateTimeOffset.UtcNow;

    public void IncreaseSentenceCount(int count) => SentencesSinceLastRun += count;

    public bool ShouldRun(int sentenceThreshold, int secondsThreshold, DateTimeOffset now)
    {
        if (SentencesSinceLastRun >= sentenceThreshold)
        {
            return true;
        }

        return (now - LastRunAt).TotalSeconds >= secondsThreshold;
    }

    public void MarkRun(DateTimeOffset now)
    {
        SentencesSinceLastRun = 0;
        LastRunAt = now;
    }
}
