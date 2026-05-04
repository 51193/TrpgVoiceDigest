namespace TrpgVoiceDigest.Core.Models;

public sealed class RefinedSentence
{
    public int Number { get; set; }
    public string Text { get; set; } = "";
}

public enum RefineAction
{
    Add,
    Edit,
    Remove,
    Empty
}

public sealed record RefineOperation(RefineAction Action, int? Number, string? Text) : IOperation;