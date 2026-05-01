using System.Collections.Concurrent;
using System.Globalization;
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
    private readonly AudioCaptureService _audioCapture;
    private readonly LlmClient _llmClient;
    private readonly ILogService? _logService;
    private readonly SessionPaths _paths;
    private readonly SessionStorage _storage;
    private readonly WhisperProcessRunner _whisperBridge;
    private readonly ConcurrentDictionary<int, DialogueLine> _pendingLines = new();
    private int _nextSequence;

    public DigestPipeline(
        SessionPaths paths,
        SessionStorage storage,
        AudioCaptureService audioCapture,
        WhisperProcessRunner whisperBridge,
        LlmClient llmClient,
        ILogService? logService = null)
    {
        _paths = paths;
        _storage = storage;
        _audioCapture = audioCapture;
        _whisperBridge = whisperBridge;
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

        _nextSequence = _storage.LoadProcessedSequence();
        if (_nextSequence < 0) _nextSequence = 0;
        _logService?.Info($"序列计数器已初始化: next={_nextSequence}");

        var inputDevice = PlatformAudioInputDiscovery.CreateDefault()
            .Resolve(audioConfig)
            .EffectiveInputDevice;
        _logService?.Info($"音频设备: {inputDevice}");

        await using var streamingRunner = new StreamingWhisperRunner(_logService);
        streamingRunner.OnTranscript += segment =>
        {
            var capturedAt = DateTimeOffset.Now;
            var seq = Interlocked.Increment(ref _nextSequence);
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

    public async Task RunCaptureWorker(
        AudioConfig audioConfig,
        Action<string>? onStatus,
        CancellationToken cancellationToken)
    {
        _logService?.Info("录音 Worker 已启动");
        try
        {
            Directory.CreateDirectory(_paths.AudioSegmentsDirectory);

            while (!cancellationToken.IsCancellationRequested)
                try
                {
                    var now = DateTimeOffset.Now;
                    var segmentPath = Path.Combine(_paths.AudioSegmentsDirectory, $"{now:yyyyMMdd_HHmmss_fff}.wav");
                    await _audioCapture.CaptureSegmentAsync(audioConfig, segmentPath, cancellationToken);
                    onStatus?.Invoke($"录音段已存储: {Path.GetFileName(segmentPath)}");
                    _logService?.Info($"录音段已存储: {Path.GetFileName(segmentPath)}");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    var msg = $"录音段失败 (下一段将重试): {ex.Message}";
                    onStatus?.Invoke(msg);
                    _logService?.Warning(msg);
                    await Task.Delay(500, cancellationToken);
                }
        }
        finally
        {
            _logService?.Info("录音 Worker 已停止");
        }
    }

    public async Task RunTranscribeWorker(
        WhisperConfig whisperConfig,
        ProcessingConfig processingConfig,
        Action<string>? onStatus,
        Action<TranscriptSegment>? onTranscript,
        CancellationToken cancellationToken)
    {
        _logService?.Info("转录 Worker 已启动");

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                var segmentPath = _storage.GetOldestAudioSegmentPath();
                if (segmentPath is null)
                {
                    await Task.Delay(processingConfig.TranscribePollingMs, cancellationToken);
                    continue;
                }

                var capturedAt = ParseTimestampFromFileName(Path.GetFileNameWithoutExtension(segmentPath));
                _logService?.Info($"开始转录: {Path.GetFileName(segmentPath)}");
                var segments = await _whisperBridge.TranscribeAsync(whisperConfig, segmentPath, cancellationToken);

                if (segments.Count > 0)
                {
                    foreach (var segment in segments)
                    {
                        _storage.AppendToDialogueLog(capturedAt, segment.Text, segment.Speaker);
                        onTranscript?.Invoke(segment);
                    }

                    var status = $"转录完成: {Path.GetFileName(segmentPath)}，句数 {segments.Count}";
                    onStatus?.Invoke(status);
                    _logService?.Info(status);
                }
                else
                {
                    var status = $"转录为空: {Path.GetFileName(segmentPath)}";
                    onStatus?.Invoke(status);
                    _logService?.Debug(status);
                }

                if (processingConfig.DeleteAudioAfterTranscribe && File.Exists(segmentPath))
                {
                    File.Delete(segmentPath);
                    _logService?.Debug($"已删除音频段: {Path.GetFileName(segmentPath)}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var msg = $"转录处理异常: {ex.Message}";
                onStatus?.Invoke(msg);
                _logService?.Warning(msg);
                await Task.Delay(processingConfig.TranscribePollingMs, cancellationToken);
            }

        _logService?.Info("转录 Worker 已停止");
    }

    public async Task RunLlmWorker(
        LlmConfig llmConfig,
        TriggerConfig triggerConfig,
        DigestState state,
        string systemPrompt,
        string protocolPrompt,
        string processingRequirementsPrompt,
        Dictionary<string, string> speakerNameMap,
        Action<string>? onStatus,
        Action<DigestState>? onDigestChanged,
        CancellationToken cancellationToken)
    {
        _logService?.Info($"摘要 Worker 已启动: 轮询间隔={triggerConfig.LlmPollingSeconds}s");

        while (!cancellationToken.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(triggerConfig.LlmPollingSeconds), cancellationToken);

                var dialogueLogText = _storage.ReadDialogueLog();
                var resolvedDialogue = _storage.ReadDialogueLogResolved(speakerNameMap);
                var currentHash = _storage.ComputeDialogueLogHash(dialogueLogText);
                var previousHash = _storage.LoadSubmitHash();

                if (string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logService?.Debug("对话日志无变化，跳过 LLM 调用");
                    continue;
                }

                _logService?.Info("检测到对话日志变化，触发 LLM 摘要调用");
                await ProcessLlmInvocation(llmConfig, state, systemPrompt, protocolPrompt, processingRequirementsPrompt,
                    resolvedDialogue, currentHash, speakerNameMap, onDigestChanged, onStatus, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var msg = $"摘要处理失败: {ex.Message}";
                onStatus?.Invoke(msg);
                _logService?.Warning(msg);
            }

        _logService?.Info("摘要 Worker 已停止");
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

    private async Task ProcessLlmInvocation(
        LlmConfig llmConfig,
        DigestState state,
        string systemPrompt,
        string protocolPrompt,
        string processingRequirementsPrompt,
        string dialogueLogText,
        string currentHash,
        Dictionary<string, string> speakerNameMap,
        Action<DigestState>? onDigestChanged,
        Action<string>? onStatus,
        CancellationToken cancellationToken)
    {
        _logService?.Info($"向 LLM 发送请求: 对话日志 {dialogueLogText.Length} 字符");
        var consistencyLexiconText = _storage.ReadCampaignConsistencyLexicon();
        var characterCardsText = _storage.ReadCampaignCharacterCards();
        var prompt = PromptComposer.BuildUserPromptResolved(
            dialogueLogText, state, consistencyLexiconText, characterCardsText, processingRequirementsPrompt,
            protocolPrompt, speakerNameMap);
        var response = await _llmClient.CompleteAsync(llmConfig, systemPrompt, prompt, cancellationToken);
        _logService?.Debug($"LLM 响应长度: {response.Length} 字符");
        var operations = EditProtocolParser.Parse(response);
        _logService?.Debug($"解析操作数: {operations.Count}");
        _storage.AppendLlmEditLog(DateTimeOffset.UtcNow, currentHash, response, operations);
        state.Apply(operations);
        _storage.SaveDigestState(state);
        _storage.SaveSubmitHash(currentHash);
        _storage.ExportCampaignDigest(state);
        _storage.ExportCampaignConsistency(state);
        _storage.ExportCampaignTasks(state);
        _storage.ExportCampaignStory(state);
        _storage.ExportHumanReadableDialogue(speakerNameMap);
        onDigestChanged?.Invoke(state);
        var status = $"摘录已更新，操作数: {operations.Count}";
        onStatus?.Invoke(status);
        _logService?.Info(status);
    }

    private static DateTimeOffset ParseTimestampFromFileName(string fileNameWithoutExtension)
    {
        if (DateTimeOffset.TryParseExact(
                fileNameWithoutExtension,
                "yyyyMMdd_HHmmss_fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var result))
            return result;

        return DateTimeOffset.Now;
    }
}