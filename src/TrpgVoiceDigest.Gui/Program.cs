using System;
using System.IO;
using Avalonia;

namespace TrpgVoiceDigest.Gui;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(appDir) && Directory.Exists(appDir))
            Directory.SetCurrentDirectory(appDir);

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