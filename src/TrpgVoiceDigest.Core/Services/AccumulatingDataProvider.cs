using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public sealed class AccumulatingDataProvider : IAccumulatingDataProvider
{
    private int _pointer = -1;
    private readonly int _maxChars;
    private readonly int _coldStartLines;
    private const double ChopRatio = 1.0 / 3.0;

    public AccumulatingDataProvider(int maxChars, int coldStartLines = 40)
    {
        _maxChars = maxChars;
        _coldStartLines = coldStartLines;
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
            var keepCount = Math.Max(1, (int)(accumulated.Length * ChopRatio));
            _pointer = lines.Length - keepCount;
            text = string.Join('\n', lines[_pointer..]);
        }

        return text;
    }
}
