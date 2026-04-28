using TrpgVoiceDigest.Infrastructure.Audio;

namespace TrpgVoiceDigest.Tests;

public class AudioLevelCalculatorTests
{
    [Fact]
    public void CalculateRmsFromPcm16_ForSilence_ShouldReturnZero()
    {
        var pcm = new byte[3200];
        var rms = AudioLevelCalculator.CalculateRmsFromPcm16(pcm, pcm.Length);
        Assert.True(rms < 0.00001);
    }

    [Fact]
    public void CalculateRmsFromPcm16_ForNonZeroSignal_ShouldReturnPositive()
    {
        var pcm = new byte[400];
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = (short)((i % 4 == 0) ? 16000 : -16000);
            pcm[i] = (byte)(sample & 0xFF);
            pcm[i + 1] = (byte)((sample >> 8) & 0xFF);
        }

        var rms = AudioLevelCalculator.CalculateRmsFromPcm16(pcm, pcm.Length);
        Assert.True(rms > 0.01);
    }
}
