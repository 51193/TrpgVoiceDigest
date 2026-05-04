using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Storage;

namespace TrpgVoiceDigest.Infrastructure.Services;

public sealed class CampaignService : ICampaignService
{
    public async Task RunAsync(CampaignOptions options, CancellationToken cancellationToken = default)
    {
        options.LogService.Info($"Campaign 启动: {options.CampaignName}");

        var paths = ApplicationPathResolver.BuildCampaignPaths(
            options.Config.Storage.CampaignRoot,
            options.CampaignName);

        var storage = new CampaignStorage(paths);
        storage.EnsureDirectories();

        var speakerNameMap = storage.LoadSpeakerNameMap();
        options.LogService.Info($"已加载说话人映射: {speakerNameMap.Count} 项");

        var llmClient = new LlmClient(new HttpClient(), logService: options.LogService);

        if (!SchedulerManager.IsInitialized)
        {
            var resolver = new DefaultPromptTemplateResolver(
                ApplicationPathResolver.AppRoot, options.LogService);
            SchedulerManager.Initialize(llmClient, resolver, options.LogService);
        }

        var pipeline = new DigestPipeline(
            paths, storage, llmClient, options.LogService);

        var transcribeTask = pipeline.RunStreamingWorker(
            options.Config.Audio, options.Config.Whisper, options.Config.AudioSegmentation,
            speakerNameMap,
            options.OnStatus, options.OnTranscript,
            cancellationToken);

        var refineTask = pipeline.RunRefinementWorker(
            options.Config.Llm, options.Config.Refinement,
            options.OnStatus, options.OnRefinementChanged,
            cancellationToken);

        var storyProgressTask = pipeline.RunStoryProgressWorker(
            options.Config.Llm, options.Config.StoryProgress,
            options.OnStatus,
            cancellationToken);

        options.LogService.Info("所有 Worker 已启动 (流式转录/精炼/故事进展/一致性)");
        await Task.WhenAll(transcribeTask, refineTask, storyProgressTask).ConfigureAwait(false);
        options.LogService.Info("Campaign 结束");
    }
}