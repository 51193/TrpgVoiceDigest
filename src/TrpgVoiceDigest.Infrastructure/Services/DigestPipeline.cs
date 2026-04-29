using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
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

    public DigestPipeline(
        SessionPaths paths,
        SessionStorage storage,
        AudioCaptureService audioCapture,
        WhisperProcessRunner whisperBridge,
        LlmClient llmClient)
    {
        _paths = paths;
        _storage = storage;
        _audioCapture = audioCapture;
        _whisperBridge = whisperBridge;
        _llmClient = llmClient;
    }

    public async Task RunCaptureWorker(
        AudioConfig audioConfig,
        ChannelWriter<SegmentJob> writer,
        Action<string>? onStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.Now;
                var segmentDirectory = Path.Combine(_paths.SessionDirectory, "audio_segments");
                var segmentPath = Path.Combine(segmentDirectory, $"{now:yyyyMMdd_HHmmss_fff}.wav");
                await _audioCapture.CaptureSegmentAsync(audioConfig, segmentPath, cancellationToken);
                await writer.WriteAsync(new SegmentJob(segmentPath, now), cancellationToken);
                onStatus?.Invoke($"录音段已入队: {Path.GetFileName(segmentPath)}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            writer.TryComplete();
        }
    }

    public async Task RunTranscribeWorker(
        WhisperConfig whisperConfig,
        ProcessingConfig processingConfig,
        ChannelReader<SegmentJob> reader,
        ChannelWriter<int> signalWriter,
        Action<string>? onStatus,
        Action<TranscriptSegment>? onTranscript,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var job in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var segments = await _whisperBridge.TranscribeAsync(whisperConfig, job.WavPath, cancellationToken);
                    if (segments.Count > 0)
                    {
                        _storage.AppendTranscriptBatch(_paths, segments, job.CapturedAt);
                        foreach (var segment in segments)
                        {
                            onTranscript?.Invoke(segment);
                        }

                        await signalWriter.WriteAsync(segments.Count, cancellationToken);
                        onStatus?.Invoke($"转录完成: {Path.GetFileName(job.WavPath)}，句数 {segments.Count}");
                    }
                    else
                    {
                        onStatus?.Invoke($"转录为空: {Path.GetFileName(job.WavPath)}");
                    }

                    if (processingConfig.DeleteAudioAfterTranscribe && File.Exists(job.WavPath))
                    {
                        File.Delete(job.WavPath);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    onStatus?.Invoke($"转录失败（已保留音频段）: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunLlmWorker(
        LlmConfig llmConfig,
        TriggerConfig triggerConfig,
        DigestState state,
        string systemPrompt,
        string protocolPrompt,
        ChannelReader<int> signalReader,
        Action<string>? onStatus,
        Action<DigestState>? onDigestChanged,
        CancellationToken cancellationToken)
    {
        var triggerState = new TriggerState();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var gotSignal = false;
                while (signalReader.TryRead(out var count))
                {
                    triggerState.IncreaseSentenceCount(count);
                    gotSignal = true;
                }

                var now = DateTimeOffset.UtcNow;
                if (!gotSignal && !triggerState.ShouldRun(triggerConfig.EverySentences, triggerConfig.EverySeconds, now))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                triggerState.MarkRun(now);

                var transcriptText = _storage.ReadAllTranscriptText(_paths);
                var currentHash = _storage.ComputeTranscriptHash(transcriptText);
                var previousHash = _storage.LoadSubmitHash(_paths);
                if (!string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    var consistencyLexiconText = _storage.ReadCampaignConsistencyLexicon(_paths);
                    var characterCardsText = _storage.ReadCampaignCharacterCards(_paths);
                    var prompt = PromptComposer.BuildUserPrompt(
                        transcriptText,
                        state,
                        consistencyLexiconText,
                        characterCardsText,
                        protocolPrompt);
                    var response = await _llmClient.CompleteAsync(llmConfig, systemPrompt, prompt, cancellationToken);
                    var operations = EditProtocolParser.Parse(response);
                    _storage.AppendLlmEditLog(_paths, DateTimeOffset.UtcNow, currentHash, response, operations);
                    state.Apply(operations);
                    _storage.SaveDigestState(_paths, state);
                    _storage.SaveSubmitHash(_paths, currentHash);
                    _storage.ExportCampaignDigest(_paths, state);
                    _storage.ExportCampaignConsistency(_paths, state);
                    _storage.ExportCampaignTasks(_paths, state);
                    _storage.ExportCampaignStory(_paths, state);
                    onDigestChanged?.Invoke(state);
                    onStatus?.Invoke($"摘录已更新，操作数: {operations.Count}");
                }

                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                onStatus?.Invoke($"摘要处理失败: {ex.Message}");
                await Task.Delay(500, cancellationToken);
            }
        }
    }
}
