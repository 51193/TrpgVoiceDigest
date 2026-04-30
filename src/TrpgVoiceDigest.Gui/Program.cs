using System;
using Avalonia;
using TrpgVoiceDigest.Infrastructure.Services;

namespace TrpgVoiceDigest.Gui;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationPathResolver.Initialize();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
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