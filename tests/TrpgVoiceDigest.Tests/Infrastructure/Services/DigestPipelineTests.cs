using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Services;
using TrpgVoiceDigest.Infrastructure.Storage;

namespace TrpgVoiceDigest.Tests.Infrastructure.Services;

public class DigestPipelineTests
{
    [Fact]
    public async Task StreamingWorker_GracefullyExitsWhenCancelled()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_test_{Guid.NewGuid():N}");
        var paths = CampaignPathBuilder.Build(root, "DND_A");
        var storage = new CampaignStorage(paths);
        storage.EnsureDirectories();

        var pipeline = new DigestPipeline(paths, storage, new LlmClient(new HttpClient()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await pipeline.RunStreamingWorker(new AudioConfig(), new WhisperConfig(), new AudioSegmentationConfig(),
            new Dictionary<string, string>(), null, null, cts.Token);
        Assert.True(true);

        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task RefinementWorker_GracefullyExitsWhenCancelled()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trpg_test_{Guid.NewGuid():N}");
        var paths = CampaignPathBuilder.Build(root, "DND_A");
        var storage = new CampaignStorage(paths);
        storage.EnsureDirectories();

        var pipeline = new DigestPipeline(paths, storage, new LlmClient(new HttpClient()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await pipeline.RunRefinementWorker(new LlmConfig(), new RefinementConfig(),
            null, null, cts.Token);
        Assert.True(true);

        try { Directory.Delete(root, true); } catch { }
    }
}
