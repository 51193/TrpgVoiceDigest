using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Tests.Core.Services;

public class CampaignPathBuilderTests
{
    [Fact]
    public void Build_ShouldContainExpectedDirectories()
    {
        var paths = CampaignPathBuilder.Build("Campaigns", "DND_A");
        Assert.EndsWith(Path.Combine("DND_A", "_system", "audio_segments"), paths.AudioSegmentsDirectory);
        Assert.EndsWith(Path.Combine("DND_A", "dialogue.log"), paths.DialogueLogPath);
        Assert.EndsWith(Path.Combine("DND_A", "merged_dialogue.md"), paths.MergedDialoguePath);
        Assert.EndsWith(Path.Combine("DND_A", "_system", "refinement_state.json"), paths.RefinementStatePath);
        Assert.EndsWith(Path.Combine("DND_A", "_system", "refinement_cursor.json"), paths.RefinementCursorPath);
        Assert.EndsWith(Path.Combine("DND_A", "_system", "refinement_edit_log.jsonl"), paths.RefinementEditLogPath);
        Assert.EndsWith(Path.Combine("DND_A", "_system", "campaign_speakers.json"), paths.CampaignSpeakersPath);
        Assert.EndsWith(Path.Combine("DND_A", "_system", "speaker_embeddings"), paths.SpeakerEmbeddingsDirectory);
    }
}
