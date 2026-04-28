using System;
using System.IO;

namespace TrpgVoiceDigest.Gui.Services;

public static class AudioLevelMonitor
{
    public static double CalculateRmsFromPcm16(byte[] pcmBytes, int length)
    {
        if (pcmBytes is null || length <= 1)
        {
            return 0;
        }

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

    public static double CalculateRms(string wavPath)
    {
        if (!File.Exists(wavPath))
        {
            return 0;
        }

        using var stream = File.OpenRead(wavPath);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 12)
        {
            return 0;
        }

        if (new string(reader.ReadChars(4)) != "RIFF")
        {
            return 0;
        }

        _ = reader.ReadInt32(); // RIFF chunk size
        if (new string(reader.ReadChars(4)) != "WAVE")
        {
            return 0;
        }

        var dataChunkOffset = -1L;
        var dataChunkSize = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();
            if (chunkSize < 0)
            {
                return 0;
            }

            if (chunkId == "data")
            {
                dataChunkOffset = stream.Position;
                dataChunkSize = chunkSize;
                break;
            }

            stream.Position += chunkSize;
            if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
            {
                stream.Position += 1; // Word alignment padding.
            }
        }

        if (dataChunkOffset < 0 || dataChunkSize <= 1)
        {
            return 0;
        }

        stream.Position = dataChunkOffset;
        var remaining = Math.Min(dataChunkSize, (int)(stream.Length - stream.Position));
        var buffer = new byte[Math.Min(4096, remaining)];
        var aggregate = new byte[Math.Min(dataChunkSize, (int)(stream.Length - stream.Position))];
        var written = 0;
        while (remaining > 1 && written < aggregate.Length)
        {
            var request = Math.Min(buffer.Length, Math.Min(remaining, aggregate.Length - written));
            var read = stream.Read(buffer, 0, request);
            if (read <= 1)
            {
                break;
            }
            Buffer.BlockCopy(buffer, 0, aggregate, written, read);
            written += read;

            remaining -= read;
        }
        return CalculateRmsFromPcm16(aggregate, written);
    }
}
