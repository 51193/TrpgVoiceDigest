using System.Threading.Channels;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Audio;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Services;
using TrpgVoiceDigest.Infrastructure.Storage;
using TrpgVoiceDigest.Infrastructure.Whisper;

namespace TrpgVoiceDigest.Tests;

public class DigestPipelineTests
{
    [Fact]
    public void Constructor_AcceptsValidDependencies()
    {
        var paths = SessionPathBuilder.Build("test_campaigns", "test_campaign", "test_session");
        var pipeline = new DigestPipeline(
            paths,
            new SessionStorage(),
            new AudioCaptureService(),
            new WhisperBridge(),
            new LlmClient(new HttpClient()));

        Assert.NotNull(pipeline);
    }

    [Fact]
    public async Task RunCaptureWorker_WritesJobsToChannel()
    {
        // This test only verifies that the worker gracefully exits when cancelled
        // because CaptureSegmentAsync depends on ffmpeg being available.
        var paths = SessionPathBuilder.Build("test_campaigns", "test_campaign", "test_session");
        var pipeline = new DigestPipeline(
            paths,
            new SessionStorage(),
            new AudioCaptureService(),
            new WhisperBridge(),
            new LlmClient(new HttpClient()));

        var channel = Channel.CreateBounded<SegmentJob>(1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await pipeline.RunCaptureWorker(
            new AudioConfig(),
            channel.Writer,
            null,
            cts.Token);

        Assert.True(channel.Reader.Completion.IsCompleted);
    }
}
