using System.Net.Http;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Storage;

namespace TrpgVoiceDigest.Infrastructure.Services;

public sealed class SessionService : ISessionService
{
    public async Task RunAsync(SessionOptions options, CancellationToken cancellationToken = default)
    {
        options.LogService.Info($"会话启动: Campaign={options.CampaignName}, Session={options.SessionName}");

        var paths = ApplicationPathResolver.BuildSessionPaths(
            options.Config.Storage.CampaignRoot,
            options.CampaignName,
            options.SessionName);

        var storage = new SessionStorage(paths);
        storage.EnsureDirectories();

        var speakerNameMap = storage.LoadSpeakerNameMap();
        options.LogService.Info($"已加载说话人映射: {speakerNameMap.Count} 项");

        var llmClient = new LlmClient(new HttpClient(), logService: options.LogService);

        // ─── 初始化调度器管理器（单例，全部调度器在构造函数中注入构建）───
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

        options.LogService.Info("所有 Worker 已启动 (流式转录/精炼/一致性)");
        await Task.WhenAll(transcribeTask, refineTask).ConfigureAwait(false);
        options.LogService.Info("会话结束");
    }
}
