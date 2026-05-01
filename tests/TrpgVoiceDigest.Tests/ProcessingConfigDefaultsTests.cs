using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Tests;

public class ProcessingConfigDefaultsTests
{
    [Fact]
    public void Defaults_ShouldMatchExpectedValues()
    {
        var cfg = new AppConfig();
        Assert.Equal(16000, cfg.Audio.SampleRate);
        Assert.Equal(1000, cfg.Processing.TranscribePollingMs);
        Assert.True(cfg.Processing.DeleteAudioAfterTranscribe);
        Assert.Equal(60, cfg.Refinement.PollingSeconds);
    }
}