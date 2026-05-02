using System.Text.Json;
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

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        _logService?.Info("流式转录 Worker 已停止");
    }

    public async Task RunRefinementWorker(
        LlmConfig llmConfig,
        RefinementConfig refinementConfig,
        string systemRefinementPrompt,
        string protocolRefinementPrompt,
        string refinementRequirementsPrompt,
        string systemConsistencyPrompt,
        Action<string>? onStatus,
        Action<RefinementState>? onRefinementChanged,
        CancellationToken cancellationToken)
    {
        _logService?.Info($"精炼 Worker 已启动: 轮询间隔={refinementConfig.PollingSeconds}s");

        var cumulativeTokens = 0;
        var callCount = 0;

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(refinementConfig.PollingSeconds), cancellationToken).ConfigureAwait(false);

                // 1. 从文件系统读取所有状态
                var dialogueLogText = _storage.ReadDialogueLog();
                var currentHash = _storage.ComputeDialogueLogHash(dialogueLogText);
                var previousHash = _storage.LoadRefinementCursor();

                if (string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logService?.Debug("对话日志无变化，跳过精炼调用");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(dialogueLogText))
                {
                    _logService?.Debug("对话日志为空，跳过精炼调用");
                    _storage.SaveRefinementCursor(currentHash);
                    continue;
                }

                // 每次都从文件系统重新读取（保证热更新）
                var currentSpeakerMap = _storage.LoadSpeakerNameMap();
                var state = _storage.LoadRefinementState();

                _logService?.Info("检测到对话日志变化，触发精炼周期");

                // 2. 说话人识别：推断未命名的 speaker
                callCount += await IdentifyUnknownSpeakers(llmConfig, currentSpeakerMap, dialogueLogText, refinementConfig.PollingSeconds, cancellationToken).ConfigureAwait(false);

                // 3. 精炼
                var usage = await ProcessRefinementInvocation(llmConfig, refinementConfig, state, systemRefinementPrompt,
                    protocolRefinementPrompt, refinementRequirementsPrompt, dialogueLogText, currentHash, currentSpeakerMap,
                    onRefinementChanged, onStatus, cancellationToken).ConfigureAwait(false);

                callCount++;
                if (usage is not null)
                {
                    cumulativeTokens += usage.TotalTokens;
                    _logService?.Info(
                        $"Token 用量: 本次={usage.PromptTokens} in + {usage.CompletionTokens} out = {usage.TotalTokens}, "
                        + $"累计={cumulativeTokens} (共{callCount}次调用)");
                }

                // 4. 一致性表更新
                var refinementMd = state.BuildMarkdown();
                callCount += await UpdateConsistencyTable(llmConfig, systemConsistencyPrompt, refinementMd, refinementConfig.PollingSeconds, cancellationToken).ConfigureAwait(false);
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

        _logService?.Info($"精炼 Worker 已停止: 共 {callCount} 次 LLM 调用, 累计 {cumulativeTokens} tokens");
    }

    private async Task<int> IdentifyUnknownSpeakers(
        LlmConfig llmConfig,
        Dictionary<string, string> speakerMap,
        string dialogueLogText,
        int pollingSeconds,
        CancellationToken cancellationToken)
    {
        var unknowns = speakerMap
            .Where(kv => string.Equals(kv.Value, kv.Key, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (unknowns.Count == 0) return 0;

        _logService?.Info($"发现 {unknowns.Count} 个未识别说话人，尝试 LLM 推断");
        var prompt = SpeakerIdentificationPromptComposer.BuildPrompt(unknowns, dialogueLogText, speakerMap);

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Min(pollingSeconds - 5, 30)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var (response, _) = await _llmClient.CompleteAsync(llmConfig, string.Empty, prompt, linked.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response) || response.Trim() == "EMPTY") return 1;

            // 解析 JSON
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response[jsonStart..(jsonEnd + 1)];
                var result = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (result is not null)
                {
                    var identified = 0;
                    foreach (var (speakerId, name) in result)
                    {
                        if (string.IsNullOrWhiteSpace(name) || name == "null") continue;
                        if (!speakerMap.ContainsKey(speakerId)) continue;
                        speakerMap[speakerId] = name.Trim();
                        identified++;
                    }
                    if (identified > 0)
                    {
                        _storage.SaveSpeakerNameMap(speakerMap);
                        _logService?.Info($"说话人识别完成: {identified} 个新映射已写入文件");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logService?.Warning($"说话人识别失败: {ex.Message}");
        }

        return 1;
    }

    private async Task<int> UpdateConsistencyTable(
        LlmConfig llmConfig,
        string systemConsistencyPrompt,
        string refinementMarkdown,
        int pollingSeconds,
        CancellationToken cancellationToken)
    {
        var state = _storage.LoadConsistencyState();

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Min(pollingSeconds - 5, 40)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var prompt = ConsistencyPromptComposer.BuildPrompt(state, "", refinementMarkdown);
            var (response, _) = await _llmClient.CompleteAsync(llmConfig, systemConsistencyPrompt, prompt, linked.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response) || response.Trim() == "EMPTY") return 0;

            var operations = ConsistencyProtocolParser.Parse(response);
            if (operations.Count == 0) return 1;

            state.ApplyOperations(operations);
            _storage.SaveConsistencyState(state);
            _logService?.Info($"一致性表已更新: {operations.Count} 个操作, 共 {state.Entries.Count} 个条目");

            return 1;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            _logService?.Warning($"一致性表更新失败: {ex.Message}");
            return 0;
        }
    }

    private async Task<LlmUsage?> ProcessRefinementInvocation(
        LlmConfig llmConfig,
        RefinementConfig refinementConfig,
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

        var prompt = RefinementPromptComposer.BuildWindowedPrompt(
            mergedDialogue, state, refinementRequirementsPrompt, protocolPrompt, speakerNameMap, refinementConfig);
        _logService?.Info($"向 LLM 发送精炼请求: 提示词 {prompt.Length} 字符 (窗口: 对话≤{refinementConfig.MaxDialogueLines}条, 状态≤{refinementConfig.MaxRefinementSentences}条)");

        var (response, usage) = await _llmClient.CompleteAsync(llmConfig, systemPrompt, prompt, cancellationToken).ConfigureAwait(false);
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

        return usage;
    }
}