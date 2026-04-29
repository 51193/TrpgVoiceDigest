using TrpgVoiceDigest.Infrastructure.Audio;

namespace TrpgVoiceDigest.Tests;

public class WindowsAudioInputDiscoveryTests
{
    [Fact]
    public void ParseDshowDevices_ShouldExtractAudioDeviceNames()
    {
        const string stderr = """
                              [dshow @ 00000248f8fd7f40] DirectShow audio devices (some may be both video and audio devices)
                              [dshow @ 00000248f8fd7f40]  "Microphone (USB PnP Audio Device)"
                              [dshow @ 00000248f8fd7f40]  "Stereo Mix (Realtek(R) Audio)"
                              [dshow @ 00000248f8fd7f40]  "@device_cm_{1234}\wave_{5678}"
                              """;

        var devices = WindowsAudioInputDiscovery.ParseDshowDevices(stderr);

        Assert.Equal(2, devices.Count);
        Assert.Contains("Microphone (USB PnP Audio Device)", devices);
        Assert.Contains("Stereo Mix (Realtek(R) Audio)", devices);
    }
}