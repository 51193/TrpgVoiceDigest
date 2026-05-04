using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Gui.Models;
using TrpgVoiceDigest.Infrastructure.Services;

namespace TrpgVoiceDigest.Gui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly SemaphoreSlim _sessionSwitchLock = new(1, 1);

    [ObservableProperty] private ViewModelBase _currentPage;
    private CancellationTokenSource? _runningCts;
    private Task? _runningTask;

    public MainWindowViewModel()
    {
        ConfigPage = new ConfigViewModel();
        CurrentPage = ConfigPage;
        ConfigPage.StartRequested += StartCampaign;
        ConfigPage.LoadDefaults(ApplicationPathResolver.ResolveConfigFilePath(ConfigConstants.DefaultConfigPath));
    }

    public ConfigViewModel ConfigPage { get; }
    public MonitorViewModel MonitorPage { get; } = new();

    private void StartCampaign(AppConfig config, string campaignName)
    {
        _ = StartCampaignAsync(config, campaignName);
    }

    private async Task StartCampaignAsync(AppConfig config, string campaignName)
    {
        await _sessionSwitchLock.WaitAsync();
        try
        {
            var previousCts = _runningCts;
            var previousTask = _runningTask;
            if (previousCts is not null) previousCts.Cancel();

            if (previousTask is not null)
                try
                {
                    await previousTask;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Previous error: {ex}");
                }

            MonitorPage.SetContext(campaignName);
            var paths = ApplicationPathResolver.BuildCampaignPaths(config.Storage.CampaignRoot, campaignName);
            var logService = new CampaignLogService(paths.RuntimeLogPath);
            ApplicationPathResolver.SetLogger(logService);
            logService.OnEntryLogged += entry =>
                Dispatcher.UIThread.Post(() => MonitorPage.LogsPage.Append(entry));

            _runningCts = new CancellationTokenSource();
            CurrentPage = MonitorPage;

            var campaignService = new CampaignService();
            var token = _runningCts.Token;
            _runningTask = Task.Run(
                () => campaignService.RunAsync(
                    new CampaignOptions
                    {
                        Config = config,
                        CampaignName = campaignName,
                        LogService = logService,
                        OnStatus = status => Dispatcher.UIThread.Post(() => MonitorPage.StatusMessage = status),
                        OnTranscript = segment => Dispatcher.UIThread.Post(() =>
                            MonitorPage.TranscriptItems.Add(new TranscriptItem(
                                $"{segment.Start:hh\\:mm\\:ss}-{segment.End:hh\\:mm\\:ss}",
                                segment.Speaker ?? "",
                                segment.Text))),
                        OnRefinementChanged = state => Dispatcher.UIThread.Post(() =>
                            MonitorPage.RefinementMarkdown = state.ExportMarkdown())
                    },
                    token),
                token);
        }
        finally
        {
            _sessionSwitchLock.Release();
        }
    }
}