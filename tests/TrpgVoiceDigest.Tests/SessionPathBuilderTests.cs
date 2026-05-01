using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Tests;

public class SessionPathBuilderTests
{
    [Fact]
    public void Build_ShouldContainExpectedDirectories()
    {
        var paths = SessionPathBuilder.Build("Campaigns", "DND_A", "Session_02");
        Assert.EndsWith(Path.Combine("DND_A", "Session_02", "audio_segments"), paths.AudioSegmentsDirectory);
        Assert.EndsWith(Path.Combine("DND_A", "Session_02", "dialogue.log"), paths.DialogueLogPath);
        Assert.EndsWith(Path.Combine("DND_A", "Session_02", "merged_dialogue.md"), paths.MergedDialoguePath);
        Assert.EndsWith(Path.Combine("DND_A", "Session_02", "refinement_state.json"), paths.RefinementStatePath);
        Assert.EndsWith(Path.Combine("DND_A", "Session_02", "refinement_cursor.json"), paths.RefinementCursorPath);
        Assert.EndsWith(Path.Combine("DND_A", "Session_02", "refinement_edit_log.jsonl"), paths.RefinementEditLogPath);
        Assert.EndsWith(Path.Combine("DND_A", "campaign_speakers.json"), paths.CampaignSpeakersPath);
        Assert.EndsWith(Path.Combine("DND_A", "speaker_embeddings"), paths.SpeakerEmbeddingsDirectory);
    }
}