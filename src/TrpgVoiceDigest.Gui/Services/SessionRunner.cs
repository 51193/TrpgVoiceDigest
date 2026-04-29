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
using TrpgVoiceDigest.Infrastructure.Audio;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Services;
using TrpgVoiceDigest.Infrastructure.Storage;
using TrpgVoiceDigest.Infrastructure.Whisper;

namespace TrpgVoiceDigest.Gui.Services;

public sealed class SessionRunner
{
    private readonly IAudioInputDiscovery _audioInputDiscovery;
    private readonly AudioCaptureService _audioCaptureService = new();

    public SessionRunner(IAudioInputDiscovery? audioInputDiscovery = null)
    {
        _audioInputDiscovery = audioInputDiscovery ?? PlatformAudioInputDiscovery.CreateDefault();
    }

    public async Task RunAsync(
        AppConfig config,
        string campaignName,
        string sessionName,
        Action<bool> onVoiceActiveChanged,
        Action<MeterDiagnostics> onMeterDiagnostics,
        Action<TranscriptSegment> onTranscript,
        Action<string> onDigestMarkdownChanged,
        Action<string> onConsistencyMarkdownChanged,
        Action<string> onActiveTasksMarkdownChanged,
        Action<string> onCompletedTasksMarkdownChanged,
        Action<string> onStoryMarkdownChanged,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        var paths = SessionPathBuilder.Build(config.Storage.CampaignRoot, campaignName, sessionName);
        var storage = new SessionStorage(paths);
        storage.EnsureDirectories();
        var state = storage.LoadDigestState();
        PushMarkdownViews(state, onDigestMarkdownChanged, onConsistencyMarkdownChanged, onActiveTasksMarkdownChanged, onCompletedTasksMarkdownChanged, onStoryMarkdownChanged);

        var pipeline = new DigestPipeline(
            paths,
            storage,
            _audioCaptureService,
            new WhisperProcessRunner(),
            new LlmClient(new HttpClient()));

        var systemPrompt = File.ReadAllText(config.Prompts.SystemPromptPath);
        var protocolPrompt = File.ReadAllText(config.Prompts.ProtocolPromptPath);
        var channels = new PipelineChannels(config.Processing.SegmentQueueCapacity);

        var workers = new List<Task>
        {
            RunMeterWorker(config, onVoiceActiveChanged, onMeterDiagnostics, onStatus, cancellationToken),
            pipeline.RunCaptureWorker(config.Audio, channels.SegmentChannel.Writer, onStatus, cancellationToken),
            pipeline.RunTranscribeWorker(config.Whisper, config.Processing, channels.SegmentChannel.Reader, channels.TranscriptionSignal.Writer, onStatus, onTranscript, cancellationToken),
            pipeline.RunLlmWorker(config.Llm, config.Trigger, state, systemPrompt, protocolPrompt, channels.TranscriptionSignal.Reader, onStatus, s =>
                PushMarkdownViews(s, onDigestMarkdownChanged, onConsistencyMarkdownChanged, onActiveTasksMarkdownChanged, onCompletedTasksMarkdownChanged, onStoryMarkdownChanged), cancellationToken)
        };

        await Task.WhenAll(workers);
    }

    private static void PushMarkdownViews(
        DigestState state,
        Action<string> onDigest,
        Action<string> onConsistency,
        Action<string> onActiveTasks,
        Action<string> onCompletedTasks,
        Action<string> onStory)
    {
        onDigest(state.BuildDigestMarkdown());
        onConsistency(state.BuildConsistencyMarkdown());
        onActiveTasks(state.BuildActiveTasksMarkdown());
        onCompletedTasks(state.BuildCompletedTasksMarkdown());
        onStory(state.BuildStoryMarkdown());
    }

    private async Task RunMeterWorker(
        AppConfig config,
        Action<bool> onVoiceActiveChanged,
        Action<MeterDiagnostics> onMeterDiagnostics,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        var resolveResult = _audioInputDiscovery.Resolve(config.Audio);
        onMeterDiagnostics(new MeterDiagnostics(
            resolveResult.EffectiveInputDevice,
            resolveResult.Strategy,
            0,
            0,
            0,
            Math.Max(config.Audio.VoiceRmsThreshold, AudioConfig.MinVoiceRmsThreshold),
            Math.Max(config.Audio.VoiceRmsThreshold, AudioConfig.MinVoiceRmsThreshold) * 0.7,
            "-"));

        var isActive = false;
        var activeStreak = 0;
        var inactiveStreak = 0;
        var successCount = 0;
        var errorCount = 0;
        var threshold = Math.Max(config.Audio.VoiceRmsThreshold, AudioConfig.MinVoiceRmsThreshold);
        var offThreshold = threshold * 0.7;
        var samplesPerWindow = Math.Max(64, config.Audio.SampleRate * Math.Max(config.Processing.MeterWindowMs, 80) / 1000);
        var bytesPerWindow = samplesPerWindow * 2;
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

                var rms = AudioLevelCalculator.CalculateRmsFromPcm16(windowBuffer, filled);
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
        }
        finally
        {
            meterProcess.Dispose();
        }
    }
}
