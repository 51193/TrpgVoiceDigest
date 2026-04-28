using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Tests;

public class ProcessingConfigDefaultsTests
{
    [Fact]
    public void Defaults_ShouldMatchAsyncPipelineExpectations()
    {
        var cfg = new AppConfig();
        Assert.Equal(20, cfg.Audio.SegmentSeconds);
        Assert.True(cfg.Processing.DeleteAudioAfterTranscribe);
        Assert.Equal(1, cfg.Processing.TranscribeWorkerCount);
    }
}
