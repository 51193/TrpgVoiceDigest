using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Gui.ViewModels;

public partial class LogsViewModel : ViewModelBase
{
    private readonly StringBuilder _buffer = new();

    [ObservableProperty] private string _logText = string.Empty;

    public void Append(LogEntry entry)
    {
        _buffer.AppendLine(
            $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level.ToString().ToUpperInvariant()}] {entry.Message}");
        LogText = _buffer.ToString();
    }

    [RelayCommand]
    public void Clear()
    {
        _buffer.Clear();
        LogText = string.Empty;
    }
}