using System.Threading.Channels;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Audio;
using TrpgVoiceDigest.Infrastructure.Config;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Storage;
using TrpgVoiceDigest.Infrastructure.Whisper;
using TrpgVoiceDigest.Core.Models;

if (args.Length < 2)
{
    Console.WriteLine("用法: dotnet run --project src/TrpgVoiceDigest.Cli -- <campaign> <session> [configPath]");
    return;
}

var campaignName = args[0];
var sessionName = args[1];
var configPath = args.Length > 2 ? args[2] : "config/app.config.json";

var config = JsonConfigLoader.Load(configPath);
var paths = SessionPathBuilder.Build(config.Storage.CampaignRoot, campaignName, sessionName);
var storage = new SessionStorage();
storage.EnsureDirectories(paths);

var digestState = storage.LoadDigestState(paths);
var audioCapture = new AudioCaptureService();
var whisperBridge = new WhisperBridge();
var llmClient = new LlmClient(new HttpClient());

var systemPrompt = File.ReadAllText(config.Prompts.SystemPromptPath);
var protocolPrompt = File.ReadAllText(config.Prompts.ProtocolPromptPath);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var segmentChannel = Channel.CreateBounded<SegmentJob>(new BoundedChannelOptions(Math.Max(1, config.Processing.SegmentQueueCapacity))
{
    SingleWriter = true,
    SingleReader = true,
    FullMode = BoundedChannelFullMode.Wait
});
var transcriptionSignalChannel = Channel.CreateUnbounded<int>();

Console.WriteLine("开始监听。按 Ctrl+C 停止。");

var workers = new List<Task>
{
    RunCaptureWorker(config, paths, segmentChannel.Writer, cts.Token),
    RunTranscribeWorker(config, paths, segmentChannel.Reader, transcriptionSignalChannel.Writer, cts.Token),
    RunLlmWorker(config, paths, digestState, systemPrompt, protocolPrompt, transcriptionSignalChannel.Reader, cts.Token)
};

await Task.WhenAll(workers);

storage.SaveDigestState(paths, digestState);
storage.ExportCampaignDigest(paths, digestState);
Console.WriteLine("已停止。");
return;

async Task RunCaptureWorker(TrpgVoiceDigest.Core.Config.AppConfig cfg, SessionPaths p, ChannelWriter<SegmentJob> writer, CancellationToken token)
{
    try
    {
        while (!token.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;
            var segmentPath = Path.Combine(p.AudioSegmentDirectory, $"{now:yyyyMMdd_HHmmss_fff}.wav");
            await audioCapture.CaptureSegmentAsync(cfg.Audio, segmentPath, token);
            await writer.WriteAsync(new SegmentJob(segmentPath, now), token);
            Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] 录音段已入队");
        }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
    }
    finally
    {
        writer.TryComplete();
    }
}

async Task RunTranscribeWorker(TrpgVoiceDigest.Core.Config.AppConfig cfg, SessionPaths p, ChannelReader<SegmentJob> reader, ChannelWriter<int> signalWriter, CancellationToken token)
{
    try
    {
        await foreach (var job in reader.ReadAllAsync(token))
        {
            try
            {
                var segments = await whisperBridge.TranscribeAsync(cfg.Whisper, job.WavPath, token);
                if (segments.Count > 0)
                {
                    storage.AppendTranscriptBatch(p, segments, job.CapturedAt);
                    await signalWriter.WriteAsync(segments.Count, token);
                    Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] 转录完成: {Path.GetFileName(job.WavPath)}，句数 {segments.Count}");
                }
                else
                {
                    Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] 转录为空: {Path.GetFileName(job.WavPath)}");
                }

                if (cfg.Processing.DeleteAudioAfterTranscribe && File.Exists(job.WavPath))
                {
                    File.Delete(job.WavPath);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"转录失败（已保留音频段）: {ex.Message}");
            }
        }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
    }
}

async Task RunLlmWorker(TrpgVoiceDigest.Core.Config.AppConfig cfg, SessionPaths p, DigestState state, string sysPrompt, string protoPrompt, ChannelReader<int> signalReader, CancellationToken token)
{
    var triggerState = new TriggerState();
    while (!token.IsCancellationRequested)
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
            if (!gotSignal && !triggerState.ShouldRun(cfg.Trigger.EverySentences, cfg.Trigger.EverySeconds, now))
            {
                await Task.Delay(500, token);
                continue;
            }

            triggerState.MarkRun(now);

            var transcriptText = storage.ReadAllTranscriptText(p);
            var currentHash = storage.ComputeTranscriptHash(transcriptText);
            var previousHash = storage.LoadSubmitHash(p);
            if (!string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase))
            {
                var prompt = PromptComposer.BuildUserPrompt(transcriptText, state, protoPrompt);
                var response = await llmClient.CompleteAsync(cfg.Llm, sysPrompt, prompt, token);
                var operations = EditProtocolParser.Parse(response);
                state.Apply(operations);
                storage.SaveDigestState(p, state);
                storage.SaveSubmitHash(p, currentHash);
                storage.ExportCampaignDigest(p, state);
                Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] 摘录已更新，操作数: {operations.Count}");
            }

            await Task.Delay(500, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"摘要处理失败: {ex.Message}");
            await Task.Delay(500, token);
        }
    }
}

internal sealed record SegmentJob(string WavPath, DateTimeOffset CapturedAt);
