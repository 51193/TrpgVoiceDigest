namespace TrpgVoiceDigest.Core.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);

public interface ILogService
{
    void Log(LogLevel level, string message);
    event Action<LogEntry>? OnEntryLogged;
}

public static class LogServiceExtensions
{
    public static void Debug(this ILogService log, string message)
    {
        log.Log(LogLevel.Debug, message);
    }

    public static void Info(this ILogService log, string message)
    {
        log.Log(LogLevel.Info, message);
    }

    public static void Warning(this ILogService log, string message)
    {
        log.Log(LogLevel.Warning, message);
    }

    public static void Error(this ILogService log, string message)
    {
        log.Log(LogLevel.Error, message);
    }
}