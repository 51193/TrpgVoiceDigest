using TrpgVoiceDigest.Gui.Services;

namespace TrpgVoiceDigest.Tests;

public class AudioLevelMonitorTests
{
    [Fact]
    public void CalculateRms_ReturnsPositiveForNonSilentWave()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rms_{Guid.NewGuid():N}.wav");
        try
        {
            CreateTestWav(path, amplitude: 6000, sampleCount: 8000);
            var rms = AudioLevelMonitor.CalculateRms(path);
            Assert.True(rms > 0.01);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void CalculateRmsFromPcm16_ForSilence_ShouldReturnZero()
    {
        var pcm = new byte[3200];
        var rms = AudioLevelMonitor.CalculateRmsFromPcm16(pcm, pcm.Length);
        Assert.True(rms < 0.00001);
    }

    [Fact]
    public void CalculateRms_SilentWaveWithExtraChunk_ShouldBeNearZero()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rms_sil_{Guid.NewGuid():N}.wav");
        try
        {
            CreateSilentWaveWithListChunk(path, sampleCount: 8000);
            var rms = AudioLevelMonitor.CalculateRms(path);
            Assert.True(rms < 0.0001, $"expected near zero, actual={rms}");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void CreateTestWav(string path, short amplitude, int sampleCount)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataSize = sampleCount * 2;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16000);
        writer.Write(16000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);

        for (var i = 0; i < sampleCount; i++)
        {
            writer.Write((short)((i % 2 == 0) ? amplitude : -amplitude));
        }
    }

    private static void CreateSilentWaveWithListChunk(string path, int sampleCount)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataSize = sampleCount * 2;
        var listSize = 8;
        var riffSize = 4 + (8 + 16) + (8 + listSize) + (8 + dataSize);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(riffSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16000);
        writer.Write(16000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("LIST"u8.ToArray());
        writer.Write(listSize);
        writer.Write("INFO"u8.ToArray());
        writer.Write("ISFT"u8.ToArray());
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        for (var i = 0; i < sampleCount; i++)
        {
            writer.Write((short)0);
        }
    }
}
