namespace TrpgVoiceDigest.Core.Services;

public static class PromptWindowHelper
{
    public static int TotalChars(IReadOnlyDictionary<string, string> sections)
    {
        return sections.Values.Sum(v => v.Length);
    }

    public static string TrimLinesFromStart(string text, int maxChars)
    {
        if (maxChars <= 0) return string.Empty;
        if (text.Length <= maxChars) return text;

        var lines = text.Split('\n');
        for (var start = 0; start < lines.Length; start++)
        {
            var remaining = lines.Skip(start).ToArray();
            var len = remaining.Sum(l => l.Length) + Math.Max(0, remaining.Length - 1);
            if (len <= maxChars)
                return string.Join('\n', remaining);
        }

        return string.Empty;
    }
}
