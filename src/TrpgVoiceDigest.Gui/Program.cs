using System;
using System.Diagnostics;
using Avalonia;
using TrpgVoiceDigest.Infrastructure.Services;

namespace TrpgVoiceDigest.Gui;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Trace.Listeners.Add(new ConsoleTraceListener());

        try
        {
            ApplicationPathResolver.Initialize();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"启动失败: {ex}");
            Console.Error.Flush();
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}