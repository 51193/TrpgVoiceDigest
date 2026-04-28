using System.Threading.Channels;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Audio;
using TrpgVoiceDigest.Infrastructure.Config;
using TrpgVoiceDigest.Infrastructure.Llm;
using TrpgVoiceDigest.Infrastructure.Services;
using TrpgVoiceDigest.Infrastructure.Storage;
using TrpgVoiceDigest.Infrastructure.Whisper;

if (args.Length < 2)
{
    Console.WriteLine("用法: dotnet run --project src/TrpgVoiceDigest.Cli -- <campaign> <session> [configPath]");
    return;
}

var campaignName = args[0];
var sessionName = args[1];
var configPath = args.Length > 2 ? args[2] : ConfigConstants.DefaultConfigPath;

var config = JsonConfigLoader.Load(configPath);
var paths = SessionPathBuilder.Build(config.Storage.CampaignRoot, campaignName, sessionName);
var storage = new SessionStorage();
storage.EnsureDirectories(paths);

var digestState = storage.LoadDigestState(paths);
var pipeline = new DigestPipeline(
    paths,
    storage,
    new AudioCaptureService(),
    new WhisperBridge(),
    new LlmClient(new HttpClient()));

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
    pipeline.RunCaptureWorker(config.Audio, segmentChannel.Writer, Console.WriteLine, cts.Token),
    pipeline.RunTranscribeWorker(config.Whisper, config.Processing, segmentChannel.Reader, transcriptionSignalChannel.Writer, Console.WriteLine, null, cts.Token),
    pipeline.RunLlmWorker(config.Llm, config.Trigger, digestState, systemPrompt, protocolPrompt, transcriptionSignalChannel.Reader, Console.WriteLine, null, cts.Token)
};

await Task.WhenAll(workers);

storage.SaveDigestState(paths, digestState);
storage.ExportCampaignDigest(paths, digestState);
storage.ExportCampaignConsistency(paths, digestState);
storage.ExportCampaignTasks(paths, digestState);
storage.ExportCampaignStory(paths, digestState);
Console.WriteLine("已停止。");
