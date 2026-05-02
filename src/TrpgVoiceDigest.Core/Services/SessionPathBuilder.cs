namespace TrpgVoiceDigest.Core.Services;

public sealed record SessionPaths(
    string CampaignDirectory,
    string SessionDirectory,
    string AudioSegmentsDirectory,
    string DialogueLogPath,
    string SessionLogPath,
    string MergedDialoguePath,
    string RefinementStatePath,
    string RefinementCursorPath,
    string RefinementEditLogPath,
    string RefinementMarkdownPath,
    string SpeakerEmbeddingsDirectory,
    string CampaignSpeakersPath,
    string ConsistencyJsonPath,
    string ConsistencyMarkdownPath,
    string ProcessedSequencePath);

public static class SessionPathBuilder
{
    public static SessionPaths Build(string campaignRoot, string campaignName, string sessionName)
    {
        var campaignDirectory = Path.Combine(campaignRoot, campaignName);
        var sessionDirectory = Path.Combine(campaignDirectory, sessionName);
        return new SessionPaths(
            campaignDirectory,
            sessionDirectory,
            Path.Combine(sessionDirectory, "audio_segments"),
            Path.Combine(sessionDirectory, "dialogue.log"),
            Path.Combine(sessionDirectory, "session.log"),
            Path.Combine(sessionDirectory, "merged_dialogue.md"),
            Path.Combine(sessionDirectory, "refinement_state.json"),
            Path.Combine(sessionDirectory, "refinement_cursor.json"),
            Path.Combine(sessionDirectory, "refinement_edit_log.jsonl"),
            Path.Combine(sessionDirectory, "refinement.md"),
            Path.Combine(campaignDirectory, "speaker_embeddings"),
            Path.Combine(campaignDirectory, "campaign_speakers.json"),
            Path.Combine(sessionDirectory, "consistency.json"),
            Path.Combine(sessionDirectory, "consistency.md"),
            Path.Combine(sessionDirectory, "processed_sequence.txt"));
    }
}