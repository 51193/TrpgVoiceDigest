using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Infrastructure.Services;

public sealed class SessionLogService : ILogService
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public event Action<LogEntry>? OnEntryLogged;

    public SessionLogService(string logFilePath)
    {
        _logFilePath = logFilePath;
        var dir = Path.GetDirectoryName(logFilePath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }
    }

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
