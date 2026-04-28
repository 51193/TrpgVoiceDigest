namespace TrpgVoiceDigest.Core.Services;

public sealed record SessionPaths(
    string CampaignDirectory,
    string SessionDirectory,
    string AudioSegmentDirectory,
    string TranscriptDirectory,
    string DigestStatePath,
    string SubmitCursorPath,
    string CampaignDigestMarkdownPath);

public static class SessionPathBuilder
{
    public static SessionPaths Build(string campaignRoot, string campaignName, string sessionName)
    {
        var campaignDirectory = Path.Combine(campaignRoot, campaignName);
        var sessionDirectory = Path.Combine(campaignDirectory, sessionName);
        var audioSegmentDirectory = Path.Combine(sessionDirectory, "audio_segments");
        var transcriptDirectory = Path.Combine(sessionDirectory, "transcripts");
        return new SessionPaths(
            campaignDirectory,
            sessionDirectory,
            audioSegmentDirectory,
            transcriptDirectory,
            Path.Combine(sessionDirectory, "digest_state.json"),
            Path.Combine(sessionDirectory, "submit_cursor.json"),
            Path.Combine(campaignDirectory, "campaign_digest.md"));
    }
}
