using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Gui.Models;
using TrpgVoiceDigest.Gui.Services;

namespace TrpgVoiceDigest.Gui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly SessionRunner _runner = new();
    private readonly SemaphoreSlim _sessionSwitchLock = new(1, 1);
    private CancellationTokenSource? _runningCts;
    private Task? _runningTask;

    [ObservableProperty] private ViewModelBase _currentPage;
    public ConfigViewModel ConfigPage { get; }
    public MonitorViewModel MonitorPage { get; } = new();

    public MainWindowViewModel()
    {
        ConfigPage = new ConfigViewModel();
        CurrentPage = ConfigPage;
        ConfigPage.StartRequested += StartSession;
        ConfigPage.LoadDefaults(ConfigConstants.DefaultConfigPath);
    }

    private void StartSession(TrpgVoiceDigest.Core.Config.AppConfig config, string campaignName, string sessionName)
    {
        _ = StartSessionAsync(config, campaignName, sessionName);
    }

    private async Task StartSessionAsync(TrpgVoiceDigest.Core.Config.AppConfig config, string campaignName, string sessionName)
    {
        await _sessionSwitchLock.WaitAsync();
        try
        {
            var previousCts = _runningCts;
            var previousTask = _runningTask;
            if (previousCts is not null)
            {
                previousCts.Cancel();
            }

            if (previousTask is not null)
            {
                try
                {
                    await previousTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when switching sessions quickly.
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Previous session error: {ex}");
                }
            }

            _runningCts = new CancellationTokenSource();
            MonitorPage.SetContext(campaignName, sessionName);
            CurrentPage = MonitorPage;
            _runningTask = Task.Run(async () =>
            {
                await _runner.RunAsync(
                    config,
                    campaignName,
                    sessionName,
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
