namespace TrpgVoiceDigest.Core.Services;

public sealed record SessionPaths(
    string CampaignDirectory,
    string SessionDirectory,
    string AudioSegmentsDirectory,
    string DialogueLogPath,
    string DigestStatePath,
    string SubmitCursorPath,
    string LlmEditLogPath,
    string SessionLogPath,
    string CampaignDigestMarkdownPath,
    string CampaignConsistencyMarkdownPath,
    string CampaignTasksMarkdownPath,
    string CampaignStoryMarkdownPath,
    string CampaignConsistencyLexiconPath,
    string CharacterCardsDirectory);

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
            Path.Combine(sessionDirectory, "digest_state.json"),
            Path.Combine(sessionDirectory, "submit_cursor.json"),
            Path.Combine(sessionDirectory, "llm_edit_log.jsonl"),
            Path.Combine(sessionDirectory, "session.log"),
            Path.Combine(campaignDirectory, "campaign_digest.md"),
            Path.Combine(campaignDirectory, "campaign_consistency.md"),
            Path.Combine(campaignDirectory, "campaign_tasks.md"),
            Path.Combine(campaignDirectory, "campaign_story.md"),
            Path.Combine(campaignDirectory, "campaign_consistency_lexicon.md"),
            Path.Combine(campaignDirectory, "character_cards"));
    }
}