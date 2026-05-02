using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Cli.Services;

public sealed class ConsoleLogService : ILogService
{
    public event Action<LogEntry>? OnEntryLogged;

    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);

        var color = level switch
        {
            LogLevel.Debug => ConsoleColor.DarkGray,
            LogLevel.Info => ConsoleColor.Gray,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };

        Console.ForegroundColor = color;
        Console.WriteLine($"[{entry.Timestamp:HH:mm:ss}] [{level.ToString().ToUpperInvariant(),-7}] {message}");
        Console.ResetColor();

        OnEntryLogged?.Invoke(entry);
    }
}
