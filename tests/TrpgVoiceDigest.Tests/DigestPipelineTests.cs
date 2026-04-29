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
            new SessionStorage(paths),
            new AudioCaptureService(),
            new WhisperProcessRunner(),
            new LlmClient(new HttpClient()));

        Assert.NotNull(pipeline);
    }

    [Fact]
    public async Task RunCaptureWorker_GracefullyExitsWhenCancelled()
    {
        var paths = SessionPathBuilder.Build("test_campaigns", "test_campaign", "test_session");
        var pipeline = new DigestPipeline(
            paths,
            new SessionStorage(paths),
            new AudioCaptureService(),
            new WhisperProcessRunner(),
            new LlmClient(new HttpClient()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await pipeline.RunCaptureWorker(new AudioConfig(), null, cts.Token);
        Assert.True(true);
    }

    [Fact]
    public async Task RunTranscribeWorker_GracefullyExitsWhenCancelled()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_test_{Guid.NewGuid():N}");
        var paths = SessionPathBuilder.Build(root, "DND_A", "Session_01");
        var storage = new SessionStorage(paths);
        storage.EnsureDirectories();

        var pipeline = new DigestPipeline(
            paths,
            storage,
            new AudioCaptureService(),
            new WhisperProcessRunner(),
            new LlmClient(new HttpClient()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await pipeline.RunTranscribeWorker(new WhisperConfig(), new ProcessingConfig(), null, null, cts.Token);
        Assert.True(true);

        Directory.Delete(root, true);
    }

    [Fact]
    public async Task RunLlmWorker_GracefullyExitsWhenCancelled()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_test_{Guid.NewGuid():N}");
        var paths = SessionPathBuilder.Build(root, "DND_A", "Session_01");
        var storage = new SessionStorage(paths);
        storage.EnsureDirectories();

        var pipeline = new DigestPipeline(
            paths,
            storage,
            new AudioCaptureService(),
            new WhisperProcessRunner(),
            new LlmClient(new HttpClient()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await pipeline.RunLlmWorker(new LlmConfig(), new TriggerConfig(), new DigestState(), "", "", "", null, null,
            cts.Token);
        Assert.True(true);

        Directory.Delete(root, true);
    }
}