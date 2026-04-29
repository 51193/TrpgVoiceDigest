using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly SessionPaths _paths;
    private readonly SessionStorage _storage;
    private readonly AudioCaptureService _audioCapture;
    private readonly WhisperProcessRunner _whisperBridge;
    private readonly LlmClient _llmClient;
    private readonly ILogService? _logService;

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
            {
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
        {
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
                        _storage.AppendToDialogueLog(capturedAt, segment.Text);
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
        Action<string>? onStatus,
        Action<DigestState>? onDigestChanged,
        CancellationToken cancellationToken)
    {
        _logService?.Info($"摘要 Worker 已启动: 轮询间隔={triggerConfig.LlmPollingSeconds}s");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(triggerConfig.LlmPollingSeconds), cancellationToken);

                var dialogueLogText = _storage.ReadDialogueLog();
                var currentHash = _storage.ComputeDialogueLogHash(dialogueLogText);
                var previousHash = _storage.LoadSubmitHash();

                if (string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logService?.Debug("对话日志无变化，跳过 LLM 调用");
                    continue;
                }

                _logService?.Info("检测到对话日志变化，触发 LLM 摘要调用");
                await ProcessLlmInvocation(llmConfig, state, systemPrompt, protocolPrompt, processingRequirementsPrompt, dialogueLogText, currentHash, onDigestChanged, onStatus, cancellationToken);
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
        }

        _logService?.Info("摘要 Worker 已停止");
    }

    private async Task ProcessLlmInvocation(
        LlmConfig llmConfig,
        DigestState state,
        string systemPrompt,
        string protocolPrompt,
        string processingRequirementsPrompt,
        string dialogueLogText,
        string currentHash,
        Action<DigestState>? onDigestChanged,
        Action<string>? onStatus,
        CancellationToken cancellationToken)
    {
        _logService?.Info($"向 LLM 发送请求: 对话日志 {dialogueLogText.Length} 字符");
        var consistencyLexiconText = _storage.ReadCampaignConsistencyLexicon();
        var characterCardsText = _storage.ReadCampaignCharacterCards();
        var prompt = PromptComposer.BuildUserPrompt(
            dialogueLogText, state, consistencyLexiconText, characterCardsText, processingRequirementsPrompt, protocolPrompt);
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
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal,
                out var result))
        {
            return result;
        }

        return DateTimeOffset.Now;
    }
}
