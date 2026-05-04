using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TrpgVoiceDigest.Gui.Models;

namespace TrpgVoiceDigest.Gui.ViewModels;

public partial class MonitorViewModel : ViewModelBase
{
    [ObservableProperty] private string _currentCampaign = string.Empty;
    [ObservableProperty] private string _effectiveInputDevice = "unknown";
    [ObservableProperty] private bool _isVoiceActive;
    [ObservableProperty] private string _lastMeterAt = "-";
    [ObservableProperty] private double _lastRms;
    [ObservableProperty] private int _meterErrorCount;
    [ObservableProperty] private string _meterStrategy = "unknown";
    [ObservableProperty] private int _meterSuccessCount;
    [ObservableProperty] private double _offThreshold;
    [ObservableProperty] private double _onThreshold;
    [ObservableProperty] private string _refinementMarkdown = "# 精炼对话\n\n暂无精炼内容。";
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _statusMessage = "等待启动";

    public ObservableCollection<TranscriptItem> TranscriptItems { get; } = [];
    public LogsViewModel LogsPage { get; } = new();

    public IBrush LampBrush => IsVoiceActive ? Brushes.LimeGreen : Brushes.DimGray;

    partial void OnIsVoiceActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(LampBrush));
    }

    public void SetContext(string campaignName)
    {
        CurrentCampaign = campaignName;
        SelectedTabIndex = 0;
        TranscriptItems.Clear();
        RefinementMarkdown = "# 精炼对话\n\n暂无精炼内容。";
        EffectiveInputDevice = "unknown";
        MeterStrategy = "unknown";
        LastRms = 0;
        MeterSuccessCount = 0;
        MeterErrorCount = 0;
        LastMeterAt = "-";
        LogsPage.Clear();
    }
}