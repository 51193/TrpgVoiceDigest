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
        ConfigPage.StartRequested += StartSession;
        ConfigPage.LoadDefaults(ApplicationPathResolver.ResolveConfigFilePath(ConfigConstants.DefaultConfigPath));
    }

    public ConfigViewModel ConfigPage { get; }
    public MonitorViewModel MonitorPage { get; } = new();

    private void StartSession(AppConfig config, string campaignName, string sessionName)
    {
        _ = StartSessionAsync(config, campaignName, sessionName);
    }

    private async Task StartSessionAsync(AppConfig config, string campaignName, string sessionName)
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
                    Debug.WriteLine($"Previous session error: {ex}");
                }

            MonitorPage.SetContext(campaignName, sessionName);
            var paths = ApplicationPathResolver.BuildSessionPaths(config.Storage.CampaignRoot, campaignName, sessionName);
            var logService = new SessionLogService(paths.SessionLogPath);
            ApplicationPathResolver.SetLogger(logService);
            logService.OnEntryLogged += entry =>
                Dispatcher.UIThread.Post(() => MonitorPage.LogsPage.Append(entry));

            _runningCts = new CancellationTokenSource();
            CurrentPage = MonitorPage;

            var sessionService = new SessionService();
            var token = _runningCts.Token;
            _runningTask = Task.Run(
                () => sessionService.RunAsync(
                    new SessionOptions
                    {
                        Config = config,
                        CampaignName = campaignName,
                        SessionName = sessionName,
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