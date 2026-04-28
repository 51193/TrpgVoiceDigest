using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Audio;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Storage;
using TrpgVoiceDigest.Infrastructure.Whisper;

namespace TrpgVoiceDigest.Gui.Services;

public sealed class SessionRunner
{
    private readonly SessionStorage _storage = new();
    private readonly AudioCaptureService _audioCaptureService = new();
    private readonly WhisperBridge _whisperBridge = new();
    private readonly LlmClient _llmClient = new(new HttpClient());

    public async Task RunAsync(
        AppConfig config,
        string campaignName,
        string sessionName,
        Action<bool> onVoiceActiveChanged,
        Action<MeterDiagnostics> onMeterDiagnostics,
        Action<TranscriptSegment> onTranscript,
        Action<string> onDigestMarkdownChanged,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        var paths = SessionPathBuilder.Build(config.Storage.CampaignRoot, campaignName, sessionName);
        _storage.EnsureDirectories(paths);
        var state = _storage.LoadDigestState(paths);
        onDigestMarkdownChanged(DigestMarkdownBuilder.Build(state));

        var systemPrompt = File.ReadAllText(config.Prompts.SystemPromptPath);
        var protocolPrompt = File.ReadAllText(config.Prompts.ProtocolPromptPath);
        var segmentChannel = Channel.CreateBounded<SegmentJob>(new BoundedChannelOptions(Math.Max(1, config.Processing.SegmentQueueCapacity))
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var transcriptionSignalChannel = Channel.CreateUnbounded<int>();

        var workers = new List<Task>
        {
            RunMeterWorker(config, paths, onVoiceActiveChanged, onMeterDiagnostics, onStatus, cancellationToken),
            RunCaptureWorker(config, paths, segmentChannel.Writer, onStatus, cancellationToken),
            RunTranscribeWorker(
                config,
                paths,
                segmentChannel.Reader,
                transcriptionSignalChannel.Writer,
                onTranscript,
                onStatus,
                cancellationToken),
            RunLlmWorker(config, paths, state, systemPrompt, protocolPrompt, transcriptionSignalChannel.Reader, onDigestMarkdownChanged, onStatus, cancellationToken)
        };

        if (config.Processing.TranscribeWorkerCount != 1)
        {
            onStatus($"为避免并发冲突，转录固定使用单消费者队列（当前配置值 {config.Processing.TranscribeWorkerCount} 已忽略）。");
        }

        await Task.WhenAll(workers);
    }

    private async Task RunMeterWorker(
        AppConfig config,
        SessionPaths paths,
        Action<bool> onVoiceActiveChanged,
        Action<MeterDiagnostics> onMeterDiagnostics,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        var resolveResult = LinuxAudioSourceResolver.Resolve(config.Audio.InputDevice);
        onMeterDiagnostics(new MeterDiagnostics(
            resolveResult.EffectiveInputDevice,
            resolveResult.Strategy,
            0,
            0,
            0,
            Math.Max(config.Audio.VoiceRmsThreshold, 0.005),
            Math.Max(config.Audio.VoiceRmsThreshold, 0.005) * 0.7,
            "-"));

        var isActive = false;
        var activeStreak = 0;
        var inactiveStreak = 0;
        var successCount = 0;
        var errorCount = 0;
        var threshold = Math.Max(config.Audio.VoiceRmsThreshold, 0.005);
        var offThreshold = threshold * 0.7;
        var samplesPerWindow = Math.Max(64, config.Audio.SampleRate * Math.Max(config.Processing.MeterWindowMs, 80) / 1000);
        var bytesPerWindow = samplesPerWindow * 2; // s16 mono
        var windowBuffer = new byte[bytesPerWindow];
        var meterProcess = _audioCaptureService.StartMeterStream(config.Audio, resolveResult.EffectiveInputDevice);
        var meterStream = meterProcess.StandardOutput.BaseStream;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var filled = 0;
                while (filled < bytesPerWindow)
                {
                    var read = await meterStream.ReadAsync(windowBuffer.AsMemory(filled, bytesPerWindow - filled), cancellationToken);
                    if (read <= 0)
                    {
                        throw new InvalidOperationException("实时音频流中断。");
                    }

                    filled += read;
                }

                var rms = AudioLevelMonitor.CalculateRmsFromPcm16(windowBuffer, filled);
                successCount++;

                if (!isActive)
                {
                    if (rms >= threshold)
                    {
                        activeStreak++;
                        inactiveStreak = 0;
                    }
                    else
                    {
                        inactiveStreak++;
                        activeStreak = 0;
                    }

                    if (activeStreak >= 2)
                    {
                        isActive = true;
                        onVoiceActiveChanged(true);
                        activeStreak = 0;
                        inactiveStreak = 0;
                    }
                }
                else
                {
                    if (rms < offThreshold)
                    {
                        inactiveStreak++;
                        activeStreak = 0;
                    }
                    else
                    {
                        activeStreak++;
                        inactiveStreak = 0;
                    }

                    if (inactiveStreak >= 2)
                    {
                        isActive = false;
                        onVoiceActiveChanged(false);
                        activeStreak = 0;
                        inactiveStreak = 0;
                    }
                }

                onMeterDiagnostics(new MeterDiagnostics(
                    resolveResult.EffectiveInputDevice,
                    resolveResult.Strategy,
                    rms,
                    successCount,
                    errorCount,
                    threshold,
                    offThreshold,
                    DateTimeOffset.Now.ToString("HH:mm:ss.fff")));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                errorCount++;
                onStatus($"状态灯采样失败: {ex.Message}");
                onVoiceActiveChanged(false);
                isActive = false;
                activeStreak = 0;
                inactiveStreak = 0;
                onMeterDiagnostics(new MeterDiagnostics(
                    resolveResult.EffectiveInputDevice,
                    resolveResult.Strategy,
                    0,
                    successCount,
                    errorCount,
                    threshold,
                    offThreshold,
                    DateTimeOffset.Now.ToString("HH:mm:ss.fff")));
                break;
            }

            try
            {
                await Task.Delay(Math.Max(50, config.Processing.MeterIntervalMs), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        try
        {
            if (!meterProcess.HasExited)
            {
                meterProcess.Kill(true);
            }
        }
        catch
        {
            // Ignore shutdown race.
        }
        finally
        {
            meterProcess.Dispose();
        }
    }

    private async Task RunCaptureWorker(
        AppConfig config,
        SessionPaths paths,
        ChannelWriter<SegmentJob> writer,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.Now;
                var segmentPath = Path.Combine(paths.AudioSegmentDirectory, $"{now:yyyyMMdd_HHmmss_fff}.wav");
                await _audioCaptureService.CaptureSegmentAsync(config.Audio, segmentPath, cancellationToken);
                await writer.WriteAsync(new SegmentJob(segmentPath, now), cancellationToken);
                onStatus($"录音段已入队: {Path.GetFileName(segmentPath)}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task RunTranscribeWorker(
        AppConfig config,
        SessionPaths paths,
        ChannelReader<SegmentJob> reader,
        ChannelWriter<int> transcriptionSignalWriter,
        Action<TranscriptSegment> onTranscript,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var job in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var segments = await _whisperBridge.TranscribeAsync(config.Whisper, job.WavPath, cancellationToken);
                    if (segments.Count > 0)
                    {
                        _storage.AppendTranscriptBatch(paths, segments, job.CapturedAt);
                        foreach (var segment in segments)
                        {
                            onTranscript(segment);
                        }

                        await transcriptionSignalWriter.WriteAsync(segments.Count, cancellationToken);
                        onStatus($"转录完成: {Path.GetFileName(job.WavPath)}，句数 {segments.Count}");
                    }
                    else
                    {
                        onStatus($"转录为空: {Path.GetFileName(job.WavPath)}");
                    }

                    if (config.Processing.DeleteAudioAfterTranscribe && File.Exists(job.WavPath))
                    {
                        File.Delete(job.WavPath);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    onStatus($"转录失败（已保留音频段）: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
    }

    private async Task RunLlmWorker(
        AppConfig config,
        SessionPaths paths,
        DigestState state,
        string systemPrompt,
        string protocolPrompt,
        ChannelReader<int> transcriptionSignalReader,
        Action<string> onDigestMarkdownChanged,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        var triggerState = new TriggerState();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var gotSignal = false;
                while (transcriptionSignalReader.TryRead(out var count))
                {
                    triggerState.IncreaseSentenceCount(count);
                    gotSignal = true;
                }

                var now = DateTimeOffset.UtcNow;
                if (!gotSignal && !triggerState.ShouldRun(config.Trigger.EverySentences, config.Trigger.EverySeconds, now))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                triggerState.MarkRun(now);

                var transcriptText = _storage.ReadAllTranscriptText(paths);
                var currentHash = _storage.ComputeTranscriptHash(transcriptText);
                var previousHash = _storage.LoadSubmitHash(paths);
                if (!string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase))
                {
                    var prompt = PromptComposer.BuildUserPrompt(transcriptText, state, protocolPrompt);
                    var response = await _llmClient.CompleteAsync(config.Llm, systemPrompt, prompt, cancellationToken);
                    var operations = EditProtocolParser.Parse(response);
                    state.Apply(operations);
                    _storage.SaveDigestState(paths, state);
                    _storage.SaveSubmitHash(paths, currentHash);
                    _storage.ExportCampaignDigest(paths, state);
                    onDigestMarkdownChanged(DigestMarkdownBuilder.Build(state));
                    onStatus($"摘录已更新，操作数: {operations.Count}");
                }

                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                onStatus($"摘要处理失败: {ex.Message}");
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private sealed record SegmentJob(string WavPath, DateTimeOffset CapturedAt);
    public sealed record MeterDiagnostics(
        string EffectiveInputDevice,
        string MeterStrategy,
        double LastRms,
        int MeterSuccessCount,
        int MeterErrorCount,
        double OnThreshold,
        double OffThreshold,
        string LastMeterAt);
}
