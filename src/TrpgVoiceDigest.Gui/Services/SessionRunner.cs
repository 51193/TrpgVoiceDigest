using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Gui.Models;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Services;
using TrpgVoiceDigest.Infrastructure.Storage;

namespace TrpgVoiceDigest.Gui.Services;

public sealed class SessionRunner
{
    private readonly ILogService _logService;

    public SessionRunner(ILogService logService)
    {
        _logService = logService;
    }

    public async Task RunAsync(
        AppConfig config,
        string campaignName,
        string sessionName,
        SessionPaths paths,
        ILogService logService,
        Action<bool> onVoiceActiveChanged,
        Action<MeterDiagnostics> onMeterDiagnostics,
        Action<TranscriptSegment> onTranscript,
        Action<string> onRefinementMarkdownChanged,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        _logService.Info($"会话启动: Campaign={campaignName}, Session={sessionName}");

        var storage = new SessionStorage(paths);
        storage.EnsureDirectories();

        var refinementState = storage.LoadRefinementState();
        _logService.Info($"已加载精炼状态: {refinementState.Sentences.Count} 条句子");
        PushRefinementView(refinementState, onRefinementMarkdownChanged);

        var speakerNameMap = storage.LoadSpeakerNameMap();
        _logService.Info($"已加载说话人名称映射: {speakerNameMap.Count} 项");

        var pipeline = new DigestPipeline(
            paths,
            storage,
            new LlmClient(new HttpClient(), logService: logService),
            logService);

        var refinementSystemPromptPath = ApplicationPathResolver.ResolvePromptPath(config.Prompts.RefinementSystemPromptPath, "精炼系统提示词");
        var refinementProtocolPath = ApplicationPathResolver.ResolvePromptPath(config.Prompts.RefinementProtocolPath, "精炼输出协议");
        var refinementRequirementsPath = ApplicationPathResolver.ResolvePromptPath(config.Prompts.RefinementRequirementsPath, "精炼处理要求");

        var refinementSystemPrompt = File.ReadAllText(refinementSystemPromptPath);
        var refinementProtocol = File.ReadAllText(refinementProtocolPath);
        var refinementRequirements = File.ReadAllText(refinementRequirementsPath);

        _logService.Info($"已加载精炼提示词: {refinementSystemPrompt.Length} 字符");

        var workers = new List<Task>
        {
            pipeline.RunStreamingWorker(config.Audio, config.Whisper, config.AudioSegmentation, speakerNameMap, onStatus,
                onTranscript,
                cancellationToken),
            pipeline.RunRefinementWorker(config.Llm, config.Refinement, refinementState,
                refinementSystemPrompt, refinementProtocol, refinementRequirements,
                speakerNameMap, onStatus, s =>
                {
                    PushRefinementView(s, onRefinementMarkdownChanged);
                }, cancellationToken),
        };

        _logService.Info("所有 Worker 已启动 (流式转录/精炼)");
        await Task.WhenAll(workers);
        _logService.Info("会话结束");
    }

    private static void PushRefinementView(RefinementState state, Action<string> onRefinement)
    {
        onRefinement(state.BuildMarkdown());
    }
}
