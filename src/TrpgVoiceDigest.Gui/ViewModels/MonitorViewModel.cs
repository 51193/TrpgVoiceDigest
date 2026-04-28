using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TrpgVoiceDigest.Gui.Models;

namespace TrpgVoiceDigest.Gui.ViewModels;

public partial class MonitorViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isVoiceActive;
    [ObservableProperty] private string _statusMessage = "等待启动";
    [ObservableProperty] private string _currentCampaign = string.Empty;
    [ObservableProperty] private string _currentSession = string.Empty;
    [ObservableProperty] private string _digestMarkdown = "# 当前摘录\n\n暂无摘录条目。";
    [ObservableProperty] private string _consistencyMarkdown = "# 一致性参考\n\n暂无一致性条目。";
    [ObservableProperty] private string _activeTasksMarkdown = "# 活跃任务\n\n暂无活跃任务。";
    [ObservableProperty] private string _completedTasksMarkdown = "# 已完成任务\n\n暂无已完成任务。";
    [ObservableProperty] private string _storyMarkdown = "# 故事进展\n\n暂无故事进展记录。";
    [ObservableProperty] private string _effectiveInputDevice = "unknown";
    [ObservableProperty] private string _meterStrategy = "unknown";
    [ObservableProperty] private double _lastRms;
    [ObservableProperty] private double _onThreshold;
    [ObservableProperty] private double _offThreshold;
    [ObservableProperty] private int _meterSuccessCount;
    [ObservableProperty] private int _meterErrorCount;
    [ObservableProperty] private string _lastMeterAt = "-";

    public ObservableCollection<TranscriptItem> TranscriptItems { get; } = [];

    public IBrush LampBrush => IsVoiceActive ? Brushes.LimeGreen : Brushes.DimGray;

    partial void OnIsVoiceActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(LampBrush));
    }

    public void SetContext(string campaignName, string sessionName)
    {
        CurrentCampaign = campaignName;
        CurrentSession = sessionName;
        TranscriptItems.Clear();
        DigestMarkdown = "# 当前摘录\n\n暂无摘录条目。";
        ConsistencyMarkdown = "# 一致性参考\n\n暂无一致性条目。";
        ActiveTasksMarkdown = "# 活跃任务\n\n暂无活跃任务。";
        CompletedTasksMarkdown = "# 已完成任务\n\n暂无已完成任务。";
        StoryMarkdown = "# 故事进展\n\n暂无故事进展记录。";
        EffectiveInputDevice = "unknown";
        MeterStrategy = "unknown";
        LastRms = 0;
        MeterSuccessCount = 0;
        MeterErrorCount = 0;
        LastMeterAt = "-";
    }
}
