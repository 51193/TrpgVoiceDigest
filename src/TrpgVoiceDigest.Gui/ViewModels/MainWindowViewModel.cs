using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Services;
using TrpgVoiceDigest.Gui.Models;
using TrpgVoiceDigest.Gui.Services;
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
        ConfigPage.LoadDefaults(ConfigConstants.DefaultConfigPath);
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
            var paths = SessionPathBuilder.Build(config.Storage.CampaignRoot, campaignName, sessionName);
            var logService = new SessionLogService(paths.SessionLogPath);
            logService.OnEntryLogged += entry =>
                Dispatcher.UIThread.Post(() => MonitorPage.LogsPage.Append(entry));

            _runningCts = new CancellationTokenSource();
            CurrentPage = MonitorPage;

            var runner = new SessionRunner(logService);
            _runningTask = Task.Run(async () =>
            {
                await runner.RunAsync(
                    config,
                    campaignName,
                    sessionName,
                    paths,
                    logService,
                    voiceActive => Dispatcher.UIThread.Post(() => MonitorPage.IsVoiceActive = voiceActive),
                    meter => Dispatcher.UIThread.Post(() =>
                    {
                        MonitorPage.EffectiveInputDevice = meter.EffectiveInputDevice;
                        MonitorPage.MeterStrategy = meter.MeterStrategy;
                        MonitorPage.LastRms = meter.LastRms;
                        MonitorPage.MeterSuccessCount = meter.MeterSuccessCount;
                        MonitorPage.MeterErrorCount = meter.MeterErrorCount;
                        MonitorPage.OnThreshold = meter.OnThreshold;
                        MonitorPage.OffThreshold = meter.OffThreshold;
                        MonitorPage.LastMeterAt = meter.LastMeterAt;
                    }),
                    segment => Dispatcher.UIThread.Post(() =>
                        MonitorPage.TranscriptItems.Add(
                            new TranscriptItem(
                                $"{segment.Start:hh\\:mm\\:ss}-{segment.End:hh\\:mm\\:ss}",
                                segment.Text))),
                    markdown => Dispatcher.UIThread.Post(() => MonitorPage.DigestMarkdown = markdown),
                    markdown => Dispatcher.UIThread.Post(() => MonitorPage.ConsistencyMarkdown = markdown),
                    markdown => Dispatcher.UIThread.Post(() => MonitorPage.ActiveTasksMarkdown = markdown),
                    markdown => Dispatcher.UIThread.Post(() => MonitorPage.CompletedTasksMarkdown = markdown),
                    markdown => Dispatcher.UIThread.Post(() => MonitorPage.StoryMarkdown = markdown),
                    status => Dispatcher.UIThread.Post(() => MonitorPage.StatusMessage = status),
                    _runningCts.Token);
            });
        }
        finally
        {
            _sessionSwitchLock.Release();
        }
    }
}