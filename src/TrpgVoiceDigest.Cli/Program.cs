using TrpgVoiceDigest.Cli.Services;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Infrastructure.Config;
using TrpgVoiceDigest.Infrastructure.Services;

namespace TrpgVoiceDigest.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var configPath = ConfigConstants.DefaultConfigPath;
        string? campaignName = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" or "-c" when i + 1 < args.Length:
                    configPath = args[++i];
                    break;
                case "--campaign" or "-n" when i + 1 < args.Length:
                    campaignName = args[++i];
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
            }
        }

        var logService = new ConsoleLogService();

        try
        {
            ApplicationPathResolver.Initialize(logger: logService);

            var resolvedConfigPath = ApplicationPathResolver.ResolveConfigFilePath(configPath);
            if (!File.Exists(resolvedConfigPath))
            {
                logService.Error($"配置文件不存在: {resolvedConfigPath}");
                return 1;
            }

            var config = JsonConfigLoader.Load(resolvedConfigPath);
            logService.Info($"已加载配置: {resolvedConfigPath}");

            campaignName ??= config.Ui.LastCampaignName;

            if (string.IsNullOrWhiteSpace(campaignName))
            {
                logService.Error("未指定 Campaign 名称。请使用 --campaign 参数指定，或在配置 Ui.LastCampaignName 中设置。");
                return 1;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                logService.Info("正在停止...");
                cts.Cancel();
            };

            var campaignService = new CampaignService();
            await campaignService.RunAsync(
                new CampaignOptions
                {
                    Config = config,
                    CampaignName = campaignName,
                    LogService = logService,
                    OnStatus = status => logService.Info($"[状态] {status}"),
                    OnTranscript = segment =>
                        logService.Info($"[对话] {segment.Speaker ?? "?"}: {segment.Text}"),
                    OnRefinementChanged = state =>
                        logService.Info($"[精炼] 已更新: {state.Count} 条句子")
                },
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            logService.Info("已取消");
        }
        catch (Exception ex)
        {
            logService.Error($"致命错误: {ex}");
            return 1;
        }

        logService.Info("结束");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("TrpgVoiceDigest CLI - 语音转录与精炼工具");
        Console.WriteLine();
        Console.WriteLine("用法: TrpgVoiceDigest.Cli [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  -c, --config <path>    配置文件路径 (默认: config/app.config.json)");
        Console.WriteLine("  -n, --campaign <name>  Campaign 名称 (默认: 使用配置 Ui.LastCampaignName)");
        Console.WriteLine("  -h, --help             显示帮助信息");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  TrpgVoiceDigest.Cli -n MyCampaign");
        Console.WriteLine("  TrpgVoiceDigest.Cli -c /path/to/config.json");
    }
}
