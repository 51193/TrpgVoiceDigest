using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Infrastructure.Services;

public sealed class CampaignLogService : ILogService
{
    private readonly object _lock = new();
    private readonly string _logFilePath;

    public CampaignLogService(string logFilePath)
    {
        _logFilePath = logFilePath;
        var dir = Path.GetDirectoryName(logFilePath);
        if (dir is not null) Directory.CreateDirectory(dir);
    }

    public event Action<LogEntry>? OnEntryLogged;

    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);

        lock (_lock)
        {
            File.AppendAllText(_logFilePath,
                $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level.ToString().ToUpperInvariant()}] {entry.Message}{Environment.NewLine}");
        }

        OnEntryLogged?.Invoke(entry);
    }
}
