namespace TrpgVoiceDigest.Infrastructure.Audio;

public static class PlatformAudioInputDiscovery
{
    public static IAudioInputDiscovery CreateDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsAudioInputDiscovery();
        }

        return new LinuxAudioInputDiscovery();
    }
}
