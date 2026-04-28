using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Tests;

public class SessionPathBuilderTests
{
    [Fact]
    public void Build_ShouldContainAudioSegmentDirectory()
    {
        var paths = SessionPathBuilder.Build("Campaigns", "DND_A", "Session_02");
        Assert.EndsWith(Path.Combine("DND_A", "Session_02", "audio_segments"), paths.AudioSegmentDirectory);
        Assert.EndsWith(Path.Combine("DND_A", "campaign_consistency.md"), paths.CampaignConsistencyMarkdownPath);
        Assert.EndsWith(Path.Combine("DND_A", "Session_02", "llm_edit_log.jsonl"), paths.LlmEditLogPath);
    }
}
