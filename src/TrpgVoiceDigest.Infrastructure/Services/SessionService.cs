using System.Net.Http;
using TrpgVoiceDigest.Core.Models;
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

        // ─── 加载外置提示词（D3） ───
        var sysRefinement = await File.ReadAllTextAsync(
            ApplicationPathResolver.ResolvePromptPath(
                options.Config.Prompts.RefinementSystemPromptPath, "精炼系统提示词"),
            cancellationToken).ConfigureAwait(false);
        var protoRefinement = await File.ReadAllTextAsync(
            ApplicationPathResolver.ResolvePromptPath(
                options.Config.Prompts.RefinementProtocolPath, "精炼输出协议"),
            cancellationToken).ConfigureAwait(false);
        var reqRefinement = await File.ReadAllTextAsync(
            ApplicationPathResolver.ResolvePromptPath(
                options.Config.Prompts.RefinementRequirementsPath, "精炼处理要求"),
            cancellationToken).ConfigureAwait(false);
        var sysConsistency = await File.ReadAllTextAsync(
            ApplicationPathResolver.ResolvePromptPath(
                options.Config.Prompts.ConsistencySystemPromptPath, "一致性系统提示词"),
            cancellationToken).ConfigureAwait(false);
        options.LogService.Info($"已加载提示词: 精炼={sysRefinement.Length}字符 + 一致性={sysConsistency.Length}字符");

        // ─── 构建结构化 LLM 容器 ───
        // 精炼容器：requirements 和 protocol 内联在模板中（它们来自提示词文件，会话期间不变）
        var refinementUserPromptTemplate = "{{speaker_mapping_section}}\n"
            + "\n"
            + "## 当前轮次合并对话（{{dialogue_label}}）\n"
            + "{{dialogue_text}}\n"
            + "\n"
            + "## 当前精炼状态（{{state_label}}）\n"
            + "{{state_json}}\n"
            + "\n"
            + reqRefinement + "\n"
            + "\n"
            + "## 输出协议\n"
            + protoRefinement;

        var refinementContainer = new StructuredLlmContainer(
            llmClient,
            new PromptSection[]
            {
                new("system", sysRefinement),
                new("user", refinementUserPromptTemplate)
            },
            new IResponseParser[] { new RefinementResponseParser() },
            options.LogService);

        var consistencyContainer = new StructuredLlmContainer(
            llmClient,
            new PromptSection[]
            {
                new("system", sysConsistency),
                new("user", "{{consistency_prompt}}")
            },
            new IResponseParser[] { new ConsistencyResponseParser() },
            options.LogService);

        var pipeline = new DigestPipeline(
            paths, storage, llmClient,
            refinementContainer, consistencyContainer,
            options.LogService);

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
