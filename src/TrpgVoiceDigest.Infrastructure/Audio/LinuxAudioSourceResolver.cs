using System.Text.RegularExpressions;

namespace TrpgVoiceDigest.Infrastructure.Audio;

public static class LinuxAudioSourceResolver
{
    private static readonly Regex DefaultSinkRegex =
        new(@"^Default Sink:\s*(?<sink>\S+)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    internal static List<string> ParseSourcesOutput(string output)
    {
        var result = new List<string>();
        var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var cols = line.Split('\t', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length >= 2) result.Add(cols[1]);
        }

        return result;
    }

    internal static string? ParseDefaultSink(string pactlInfoOutput)
    {
        if (string.IsNullOrWhiteSpace(pactlInfoOutput)) return null;

        var match = DefaultSinkRegex.Match(pactlInfoOutput);
        if (!match.Success) return null;

        var sink = match.Groups["sink"].Value.Trim();
        return string.IsNullOrWhiteSpace(sink) ? null : sink;
    }
}