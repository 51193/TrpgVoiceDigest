using TrpgVoiceDigest.Infrastructure.Audio;

namespace TrpgVoiceDigest.Tests;

public class LinuxAudioSourceResolverTests
{
    [Fact]
    public void ParseSourcesOutput_ShouldExtractDeviceNames()
    {
        const string output = """
                              52	alsa_input.pci-0000_00_1f.3.analog-stereo	module-alsa-card.c	s16le 2ch 48000Hz	RUNNING
                              53	alsa_output.pci-0000_00_1f.3.analog-stereo.monitor	module-alsa-card.c	s16le 2ch 48000Hz	IDLE
                              """;

        var list = LinuxAudioSourceResolver.ParseSourcesOutput(output);

        Assert.Equal(2, list.Count);
        Assert.Contains("alsa_output.pci-0000_00_1f.3.analog-stereo.monitor", list);
    }

    [Fact]
    public void ParseDefaultSink_ShouldReturnSinkName()
    {
        const string pactlInfo = """
                                 Server String: /run/user/1000/pulse/native
                                 Default Sink: alsa_output.pci-0000_00_1f.3.analog-stereo
                                 Default Source: alsa_input.pci-0000_00_1f.3.analog-stereo
                                 """;

        var sink = LinuxAudioSourceResolver.ParseDefaultSink(pactlInfo);
        Assert.Equal("alsa_output.pci-0000_00_1f.3.analog-stereo", sink);
    }
}