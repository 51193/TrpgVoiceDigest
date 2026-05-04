using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public sealed class AccumulatingDataProvider : IAccumulatingDataProvider
{
    private int _pointer = -1;
    private readonly int _maxChars;
    private readonly int _coldStartLines;
    private readonly int _retentionChars;

    public AccumulatingDataProvider(int maxChars, int coldStartLines = 40, int retentionChars = 1000)
    {
        _maxChars = maxChars;
        _coldStartLines = coldStartLines;
        _retentionChars = retentionChars;
    }

    public string Accumulate(string key, string currentValue)
    {
        if (string.IsNullOrWhiteSpace(currentValue))
            return currentValue;

        var lines = currentValue.Split('\n');
        if (lines.Length == 0)
            return currentValue;

        if (_pointer < 0)
            _pointer = Math.Max(0, lines.Length - _coldStartLines);

        _pointer = Math.Clamp(_pointer, 0, lines.Length - 1);

        var accumulated = lines[_pointer..];
        var text = string.Join('\n', accumulated);

        if (text.Length > _maxChars && accumulated.Length > 1)
        {
            var keptLines = CountLinesToRetain(accumulated);
            _pointer = lines.Length - keptLines;
            text = string.Join('\n', lines[_pointer..]);
        }

        return text;
    }

    private int CountLinesToRetain(string[] accumulated)
    {
        var chars = 0;
        for (var i = accumulated.Length - 1; i >= 0; i--)
        {
            chars += accumulated[i].Length;
            if (chars >= _retentionChars)
                return accumulated.Length - i;
        }
        return accumulated.Length;
    }
}
