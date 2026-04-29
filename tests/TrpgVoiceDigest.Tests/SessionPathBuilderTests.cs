using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Tests;

public class SessionPathBuilderTests
{
    [Fact]
    public void Build_ShouldContainAudioSegmentDirectory()
    {
        var paths = SessionPathBuilder.Build("Campaigns", "DND_A", "Session_02");
        Assert.EndsWith(Path.Combine("DND_A", "character_cards"), paths.CharacterCardsDirectory);
        Assert.EndsWith(Path.Combine("DND_A", "campaign_consistency_lexicon.md"), paths.CampaignConsistencyLexiconPath);
        Assert.EndsWith(Path.Combine("DND_A", "campaign_consistency.md"), paths.CampaignConsistencyMarkdownPath);
        Assert.EndsWith(Path.Combine("DND_A", "Session_02", "llm_edit_log.jsonl"), paths.LlmEditLogPath);
        Assert.EndsWith(Path.Combine("DND_A", "Session_02", "asr_metrics.jsonl"), paths.AsrMetricsLogPath);
    }
}
