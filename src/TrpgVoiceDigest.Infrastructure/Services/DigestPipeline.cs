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
    private readonly ILlmClient _llmClient;
    private readonly ILogService? _logService;
    private readonly CampaignPaths _paths;
    private readonly CampaignStorage _storage;

    public DigestPipeline(
        CampaignPaths paths,
        CampaignStorage storage,
        ILlmClient llmClient,
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
            cancellationToken.ThrowIfCancellationRequested();
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
        Action<string>? onStatus,
        Action<IncrementalDigestContainer>? onRefinementChanged,
        CancellationToken cancellationToken)
    {
        _logService?.Info($"精炼 Worker 已启动: 轮询间隔={refinementConfig.PollingSeconds}s");

        var cumulativeTokens = 0;
        var cumulativeCacheHit = 0;
        var cumulativeCacheMiss = 0;
        var callCount = 0;

        var dialogueAccumulator = new AccumulatingDataProvider(
            refinementConfig.AccumulationMaxChars,
            refinementConfig.ColdStartDialogueLines);

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(refinementConfig.PollingSeconds), cancellationToken).ConfigureAwait(false);

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

                var currentSpeakerMap = _storage.LoadSpeakerNameMap();
                var characterCards = _storage.LoadCharacterCards();
                var state = _storage.LoadRefinementState(_logService);

                _logService?.Info("检测到对话日志变化，触发精炼周期");

                var (speakerCalls, speakerUsage) = await IdentifyUnknownSpeakers(llmConfig, currentSpeakerMap, dialogueLogText, refinementConfig.PollingSeconds, characterCards, cancellationToken).ConfigureAwait(false);
                callCount += speakerCalls;
                if (speakerUsage is not null)
                    LogTokenStats("说话人识别", speakerUsage, ref cumulativeTokens, ref cumulativeCacheHit, ref cumulativeCacheMiss, callCount);

                var refinementUsage = await ProcessRefinementInvocation(llmConfig, refinementConfig, state,
                    dialogueLogText, currentHash, currentSpeakerMap,
                    dialogueAccumulator,
                    characterCards,
                    onRefinementChanged, onStatus, cancellationToken).ConfigureAwait(false);

                callCount++;
                if (refinementUsage is not null)
                    LogTokenStats("精炼", refinementUsage, ref cumulativeTokens, ref cumulativeCacheHit, ref cumulativeCacheMiss, callCount);

                var refinementMd = state.ExportMarkdown();
                var (consistencyCalls, consistencyUsage) = await UpdateConsistencyTable(llmConfig, refinementMd, refinementConfig.PollingSeconds, characterCards, cancellationToken).ConfigureAwait(false);
                callCount += consistencyCalls;
                if (consistencyUsage is not null)
                    LogTokenStats("一致性", consistencyUsage, ref cumulativeTokens, ref cumulativeCacheHit, ref cumulativeCacheMiss, callCount);
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

        var stopCacheInfo = cumulativeCacheHit + cumulativeCacheMiss > 0
            ? $", 缓存命中率={cumulativeCacheHit}/{cumulativeCacheMiss + cumulativeCacheHit}="
                + $"{(double)cumulativeCacheHit / (cumulativeCacheMiss + cumulativeCacheHit) * 100:F1}%"
            : "";
        _logService?.Info($"精炼 Worker 已停止: 共 {callCount} 次 LLM 调用, 累计 {cumulativeTokens} tokens{stopCacheInfo}");
    }

    public async Task RunStoryProgressWorker(
        LlmConfig llmConfig,
        StoryProgressConfig config,
        Action<string>? onStatus,
        CancellationToken cancellationToken)
    {
        _logService?.Info($"故事进展 Worker 已启动: 轮询间隔={config.PollingSeconds}s");

        var cumulativeTokens = 0;
        var cumulativeCacheHit = 0;
        var cumulativeCacheMiss = 0;
        var callCount = 0;

        var refinementAccumulator = new AccumulatingDataProvider(
            config.AccumulationMaxChars,
            config.ColdStartLines);

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(config.PollingSeconds), cancellationToken).ConfigureAwait(false);

                if (!File.Exists(_paths.RefinementMarkdownPath))
                {
                    _logService?.Debug("精炼结果文件尚未生成，跳过故事进展");
                    continue;
                }

                var refinementText = File.ReadAllText(_paths.RefinementMarkdownPath);
                var currentHash = _storage.ComputeDialogueLogHash(refinementText);
                var previousHash = _storage.LoadStoryProgressCursor();

                if (string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logService?.Debug("精炼结果无变化，跳过故事进展");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(refinementText))
                {
                    _logService?.Debug("精炼结果为空，跳过故事进展");
                    _storage.SaveStoryProgressCursor(currentHash);
                    continue;
                }

                var characterCards = _storage.LoadCharacterCards();
                var state = _storage.LoadStoryProgressState();

                _logService?.Info("检测到精炼结果变化，触发故事进展提取");

                var data = StoryProgressPromptComposer.BuildStoryProgressData(
                    refinementText, state, config, characterCards);

                var containers = new Dictionary<string, IncrementalDigestContainer>();

                var scheduler = SchedulerManager.Instance.Get(SchedulerManager.StoryProgress);
                var result = await scheduler.ExecuteAsync(data, containers,
                    new IIncrementalDataContainer[] { state }, llmConfig, cancellationToken,
                    accumulatingProvider: refinementAccumulator,
                    accumulatingKey: "refinement_text");

                callCount++;

                if (result.Usage is not null)
                {
                    cumulativeTokens += result.Usage.TotalTokens;
                    var cacheInfo = "";
                    if (result.Usage.CacheHitTokens + result.Usage.CacheMissTokens > 0)
                    {
                        cumulativeCacheHit += result.Usage.CacheHitTokens;
                        cumulativeCacheMiss += result.Usage.CacheMissTokens;
                        var ratio = cumulativeCacheHit + cumulativeCacheMiss > 0
                            ? (double)cumulativeCacheHit / (cumulativeCacheHit + cumulativeCacheMiss) * 100
                            : 0;
                        cacheInfo = $", 缓存命中: {result.Usage.CacheHitTokens} / 未命中: {result.Usage.CacheMissTokens} "
                            + $"(累计 {cumulativeCacheHit}/{cumulativeCacheMiss}={ratio:F1}%)";
                    }
                    _logService?.Info(
                        $"[故事进展] Token: 本次 {result.Usage.PromptTokens} in + {result.Usage.CompletionTokens} out = {result.Usage.TotalTokens}, "
                        + $"累计 {cumulativeTokens} (共{callCount}次){cacheInfo}");
                }

                var operations = StoryProgressProtocolParser.Parse(result.Response);
                if (operations.Count > 0 && operations[0].Action != StoryAction.Empty)
                {
                    var typedOps = operations.Where(o => o.Action != StoryAction.Empty).ToList();
                    _storage.AppendStoryProgressEditLog(DateTimeOffset.UtcNow, result.Response, typedOps);
                    state.ApplyOperations(typedOps);
                    _storage.SaveStoryProgressState(state);
                    _storage.ExportStoryProgressMarkdown(state);
                    var status = $"故事进展已更新: 操作数={typedOps.Count}";
                    onStatus?.Invoke(status);
                    _logService?.Info(status);
                }

                _storage.SaveStoryProgressCursor(currentHash);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var msg = $"故事进展提取失败: {ex.Message}";
                onStatus?.Invoke(msg);
                _logService?.Warning(msg);
            }

        var stopCacheInfo = cumulativeCacheHit + cumulativeCacheMiss > 0
            ? $", 缓存命中率={cumulativeCacheHit}/{cumulativeCacheMiss + cumulativeCacheHit}="
                + $"{(double)cumulativeCacheHit / (cumulativeCacheMiss + cumulativeCacheHit) * 100:F1}%"
            : "";
        _logService?.Info($"故事进展 Worker 已停止: 共 {callCount} 次 LLM 调用, 累计 {cumulativeTokens} tokens{stopCacheInfo}");
    }

    private void LogTokenStats(string label, LlmUsage usage,
        ref int cumulativeTokens, ref int cumulativeCacheHit, ref int cumulativeCacheMiss, int callCount)
    {
        cumulativeTokens += usage.TotalTokens;
        var cacheInfo = "";
        if (usage.CacheHitTokens + usage.CacheMissTokens > 0)
        {
            cumulativeCacheHit += usage.CacheHitTokens;
            cumulativeCacheMiss += usage.CacheMissTokens;
            var ratio = cumulativeCacheHit + cumulativeCacheMiss > 0
                ? (double)cumulativeCacheHit / (cumulativeCacheHit + cumulativeCacheMiss) * 100
                : 0;
            cacheInfo = $", 缓存命中: {usage.CacheHitTokens} / 未命中: {usage.CacheMissTokens} "
                + $"(累计 {cumulativeCacheHit}/{cumulativeCacheMiss}={ratio:F1}%)";
        }

        _logService?.Info(
            $"[{label}] Token: 本次 {usage.PromptTokens} in + {usage.CompletionTokens} out = {usage.TotalTokens}, "
            + $"累计 {cumulativeTokens} (共{callCount}次){cacheInfo}");
    }

    private async Task<(int Calls, LlmUsage? Usage)> IdentifyUnknownSpeakers(
        LlmConfig llmConfig,
        Dictionary<string, string> speakerMap,
        string dialogueLogText,
        int pollingSeconds,
        string characterCards,
        CancellationToken cancellationToken)
    {
        var unknowns = speakerMap
            .Where(kv => string.Equals(kv.Value, kv.Key, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (unknowns.Count == 0) return (0, null);

        _logService?.Info($"发现 {unknowns.Count} 个未识别说话人，尝试 LLM 推断");
        var prompt = SpeakerIdentificationPromptComposer.BuildPrompt(unknowns, dialogueLogText, speakerMap, characterCards);

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Min(pollingSeconds - 5, 30)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var (response, usage) = await _llmClient.CompleteAsync(llmConfig, string.Empty, prompt, linked.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response) || response.Trim() == "EMPTY") return (1, usage);

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

            return (1, usage);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logService?.Warning($"说话人识别失败: {ex.Message}");
        }

        return (1, null);
    }

    private async Task<(int Calls, LlmUsage? Usage)> UpdateConsistencyTable(
        LlmConfig llmConfig,
        string refinementMarkdown,
        int pollingSeconds,
        string characterCards,
        CancellationToken cancellationToken)
    {
        var state = _storage.LoadConsistencyState();

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Min(pollingSeconds - 5, 40)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var prompt = ConsistencyPromptComposer.BuildPrompt(state, string.Empty, refinementMarkdown);
            var data = new Dictionary<string, string>
            {
                ["character_cards"] = characterCards,
                ["consistency_prompt"] = prompt
            };
            var containers = new Dictionary<string, IncrementalDigestContainer>();

            var scheduler = SchedulerManager.Instance.Get(SchedulerManager.Consistency);
            var result = await scheduler.ExecuteAsync(data, containers,
                new IIncrementalDataContainer[] { state }, llmConfig, linked.Token);

            if (string.IsNullOrWhiteSpace(result.Response) || result.Response.Trim() == "EMPTY") return (0, result.Usage);

            var operations = ConsistencyProtocolParser.Parse(result.Response);
            if (operations.Count == 0) return (1, result.Usage);

            _storage.AppendConsistencyEditLog(DateTimeOffset.UtcNow, result.Response, operations);
            _storage.SaveConsistencyState(state);
            _logService?.Info($"一致性表已更新: {operations.Count} 个操作, 共 {state.Entries.Count} 个条目");

            return (1, result.Usage);
        }
        catch (OperationCanceledException) { return (0, null); }
        catch (Exception ex)
        {
            _logService?.Warning($"一致性表更新失败: {ex.Message}");
            return (0, null);
        }
    }

    private async Task<LlmUsage?> ProcessRefinementInvocation(
        LlmConfig llmConfig,
        RefinementConfig refinementConfig,
        IncrementalDigestContainer state,
        string dialogueLogText,
        string currentHash,
        Dictionary<string, string> speakerNameMap,
        IAccumulatingDataProvider dialogueAccumulator,
        string characterCards,
        Action<IncrementalDigestContainer>? onRefinementChanged,
        Action<string>? onStatus,
        CancellationToken cancellationToken)
    {
        var data = RefinementPromptComposer.BuildRefinementData(
            dialogueLogText, state,
            speakerNameMap, refinementConfig,
            characterCards,
            useDialogueWindow: false);

        _logService?.Info(
            $"向 LLM 发送精炼请求 (累积模式: 对话上限≤{refinementConfig.AccumulationMaxChars}字符, "
            + $"状态≤{refinementConfig.MaxRefinementSentences}条)");

        var containers = new Dictionary<string, IncrementalDigestContainer>
        {
            ["state"] = state
        };

        var scheduler = SchedulerManager.Instance.Get(SchedulerManager.Refinement);
        var result = await scheduler.ExecuteAsync(data, containers,
            new IIncrementalDataContainer[] { state }, llmConfig, cancellationToken,
            accumulatingProvider: dialogueAccumulator,
            accumulatingKey: "dialogue_text");
        _logService?.Debug($"LLM 精炼响应长度: {result.Response.Length} 字符");

        var operations = RefinementProtocolParser.Parse(result.Response);
        _logService?.Debug($"解析精炼操作数: {operations.Count}");

        _storage.AppendRefinementEditLog(DateTimeOffset.UtcNow, currentHash, result.Response, operations);
        _storage.SaveRefinementState(state);
        _storage.SaveRefinementCursor(currentHash);
        _storage.ExportRefinementMarkdown(state);

        onRefinementChanged?.Invoke(state);

        var status = $"精炼已更新，操作数: {operations.Count}";
        onStatus?.Invoke(status);
        _logService?.Info(status);

        return result.Usage;
    }
}
