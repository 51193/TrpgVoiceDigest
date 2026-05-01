using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Audio;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Storage;
using TrpgVoiceDigest.Infrastructure.Whisper;

namespace TrpgVoiceDigest.Infrastructure.Services;

public sealed class DigestPipeline
{
    private readonly LlmClient _llmClient;
    private readonly ILogService? _logService;
    private readonly SessionPaths _paths;
    private readonly SessionStorage _storage;

    public DigestPipeline(
        SessionPaths paths,
        SessionStorage storage,
        LlmClient llmClient,
        ILogService? logService = null)
    {
        _paths = paths;
        _storage = storage;
        _llmClient = llmClient;
        _logService = logService;
    }

    public async Task RunStreamingWorker(
        AudioConfig audioConfig,
        WhisperConfig whisperConfig,
        AudioSegmentationConfig segConfig,
        Dictionary<string, string> speakerNameMap,
        Action<string>? onStatus,
        Action<TranscriptSegment>? onTranscript,
        CancellationToken cancellationToken)
    {
        _logService?.Info("流式转录 Worker 已启动");

        var nextSequence = _storage.LoadProcessedSequence();
        if (nextSequence < 0) nextSequence = 0;
        _logService?.Info($"序列计数器已初始化: next={nextSequence}");

        var inputDevice = PlatformAudioInputDiscovery.CreateDefault()
            .Resolve(audioConfig)
            .EffectiveInputDevice;
        _logService?.Info($"音频设备: {inputDevice}");

        await using var streamingRunner = new StreamingWhisperRunner(_logService);
        streamingRunner.OnTranscript += segment =>
        {
            var capturedAt = DateTimeOffset.Now;
            var seq = Interlocked.Increment(ref nextSequence);
            var currentSeq = seq - 1;

            var speaker = segment.Speaker ?? "";
            if (speaker.Length > 0)
                _storage.EnsureSpeakerInMap(speakerNameMap, speaker);

            _storage.AppendToDialogueLog(capturedAt, segment.Text, speaker);
            _storage.SaveProcessedSequence(currentSeq);
            onTranscript?.Invoke(segment);
        };
        streamingRunner.OnStatus += status => onStatus?.Invoke(status);
        streamingRunner.OnError += ex =>
        {
            var msg = $"流式转录异常: {ex.Message}";
            onStatus?.Invoke(msg);
            _logService?.Warning(msg);
        };

        try
        {
            streamingRunner.Start(whisperConfig, audioConfig, segConfig, inputDevice, _paths.SpeakerEmbeddingsDirectory);

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        _logService?.Info("流式转录 Worker 已停止");
    }

    public async Task RunRefinementWorker(
        LlmConfig llmConfig,
        RefinementConfig refinementConfig,
        RefinementState state,
        string systemPrompt,
        string protocolPrompt,
        string refinementRequirementsPrompt,
        Dictionary<string, string> speakerNameMap,
        Action<string>? onStatus,
        Action<RefinementState>? onRefinementChanged,
        CancellationToken cancellationToken)
    {
        _logService?.Info($"精炼 Worker 已启动: 轮询间隔={refinementConfig.PollingSeconds}s");

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(refinementConfig.PollingSeconds), cancellationToken);

                var dialogueLogText = _storage.ReadDialogueLog();
                var currentHash = _storage.ComputeDialogueLogHash(dialogueLogText);
                var previousHash = _storage.LoadRefinementCursor();

                if (string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logService?.Debug("对话日志无变化，跳过精炼调用");
                    continue;
                }

                _logService?.Info("检测到对话日志变化，触发 LLM 精炼调用");
                await ProcessRefinementInvocation(llmConfig, state, systemPrompt, protocolPrompt,
                    refinementRequirementsPrompt, dialogueLogText, currentHash, speakerNameMap, onRefinementChanged,
                    onStatus, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var msg = $"精炼处理失败: {ex.Message}";
                onStatus?.Invoke(msg);
                _logService?.Warning(msg);
            }

        _logService?.Info("精炼 Worker 已停止");
    }

    private async Task ProcessRefinementInvocation(
        LlmConfig llmConfig,
        RefinementState state,
        string systemPrompt,
        string protocolPrompt,
        string refinementRequirementsPrompt,
        string dialogueLogText,
        string currentHash,
        Dictionary<string, string> speakerNameMap,
        Action<RefinementState>? onRefinementChanged,
        Action<string>? onStatus,
        CancellationToken cancellationToken)
    {
        var mergedDialogue = SessionStorage.MergeConsecutiveSpeakerLines(dialogueLogText);
        _storage.SaveMergedDialogue(mergedDialogue);
        _logService?.Info($"合并对话: 原始 {dialogueLogText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} 行 -> 合并 {mergedDialogue.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} 行");

        var prompt = RefinementPromptComposer.BuildUserPromptResolved(
            mergedDialogue, state, refinementRequirementsPrompt, protocolPrompt, speakerNameMap);
        _logService?.Info($"向 LLM 发送精炼请求: 提示词 {prompt.Length} 字符");

        var response = await _llmClient.CompleteAsync(llmConfig, systemPrompt, prompt, cancellationToken);
        _logService?.Debug($"LLM 精炼响应长度: {response.Length} 字符");

        var operations = RefinementProtocolParser.Parse(response);
        _logService?.Debug($"解析精炼操作数: {operations.Count}");

        _storage.AppendRefinementEditLog(DateTimeOffset.UtcNow, currentHash, response, operations);
        state.ApplyOperations(operations);
        _storage.SaveRefinementState(state);
        _storage.SaveRefinementCursor(currentHash);
        _storage.ExportRefinementMarkdown(state);

        onRefinementChanged?.Invoke(state);

        var status = $"精炼已更新，操作数: {operations.Count}";
        onStatus?.Invoke(status);
        _logService?.Info(status);
    }
}