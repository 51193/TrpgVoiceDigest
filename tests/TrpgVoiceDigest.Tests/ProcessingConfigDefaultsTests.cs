using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Tests;

public class ProcessingConfigDefaultsTests
{
    [Fact]
    public void Defaults_ShouldMatchExpectedValues()
    {
        var cfg = new AppConfig();
        Assert.Equal(20, cfg.Audio.SegmentSeconds);
        Assert.Equal(60, cfg.Trigger.LlmPollingSeconds);
        Assert.Equal(1000, cfg.Processing.TranscribePollingMs);
        Assert.True(cfg.Processing.DeleteAudioAfterTranscribe);
    }
}