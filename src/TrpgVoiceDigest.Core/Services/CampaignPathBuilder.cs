namespace TrpgVoiceDigest.Core.Services;

public sealed record CampaignPaths(
    string CampaignDirectory,
    string SystemDirectory,
    string CharacterCardsDirectory,
    string AudioSegmentsDirectory,
    string DialogueLogPath,
    string RuntimeLogPath,
    string MergedDialoguePath,
    string RefinementStatePath,
    string RefinementCursorPath,
    string RefinementEditLogPath,
    string RefinementMarkdownPath,
    string SpeakerEmbeddingsDirectory,
    string CampaignSpeakersPath,
    string ConsistencyJsonPath,
    string ConsistencyMarkdownPath,
    string StoryProgressStatePath,
    string StoryProgressEditLogPath,
    string StoryProgressMarkdownPath,
    string TaskStatePath,
    string TaskEditLogPath,
    string TaskMarkdownPath,
    string ProcessedSequencePath,
    string TokenUsagePath);

public static class CampaignPathBuilder
{
    private const string SystemSubDir = "_system";

    public static CampaignPaths Build(string campaignRoot, string campaignName)
    {
        var campaignDirectory = Path.Combine(campaignRoot, campaignName);
        var systemDirectory = Path.Combine(campaignDirectory, SystemSubDir);
        return new CampaignPaths(
            campaignDirectory,
            systemDirectory,
            Path.Combine(campaignDirectory, "character_cards"),
            Path.Combine(systemDirectory, "audio_segments"),
            Path.Combine(campaignDirectory, "dialogue.log"),
            Path.Combine(systemDirectory, "runtime.log"),
            Path.Combine(campaignDirectory, "merged_dialogue.md"),
            Path.Combine(systemDirectory, "refinement_state.json"),
            Path.Combine(systemDirectory, "refinement_cursor.json"),
            Path.Combine(systemDirectory, "refinement_edit_log.jsonl"),
            Path.Combine(campaignDirectory, "refinement.md"),
            Path.Combine(systemDirectory, "speaker_embeddings"),
            Path.Combine(systemDirectory, "campaign_speakers.json"),
            Path.Combine(systemDirectory, "consistency.json"),
            Path.Combine(campaignDirectory, "consistency.md"),
            Path.Combine(systemDirectory, "story_progress_state.json"),
            Path.Combine(systemDirectory, "story_progress_edit_log.jsonl"),
            Path.Combine(campaignDirectory, "story_progress.md"),
            Path.Combine(systemDirectory, "task_state.json"),
            Path.Combine(systemDirectory, "task_edit_log.jsonl"),
            Path.Combine(campaignDirectory, "tasks.md"),
            Path.Combine(systemDirectory, "processed_sequence.txt"),
            Path.Combine(systemDirectory, "token_usage.json"));
    }
}