namespace TrpgVoiceDigest.Infrastructure.Audio;

public static class AudioLevelCalculator
{
    public static double CalculateRmsFromPcm16(byte[] pcmBytes, int length)
    {
        if (pcmBytes is null || length <= 1) return 0;

        long samples = 0;
        double sumSquares = 0;
        for (var i = 0; i + 1 < length; i += 2)
        {
            var sample = BitConverter.ToInt16(pcmBytes, i) / 32768.0;
            sumSquares += sample * sample;
            samples++;
        }

        return samples == 0 ? 0 : Math.Sqrt(sumSquares / samples);
    }
}