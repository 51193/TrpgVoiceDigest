namespace TrpgVoiceDigest.Core.Models;

public interface IOperation;

public interface IIncrementalDataContainer
{
    void ApplyOperations(IReadOnlyList<IOperation> operations);
}

public interface IResponseParser
{
    IReadOnlyList<IOperation> Parse(string response);
}

public sealed record ChatMessage(string Role, string Content);

public sealed record PromptSection(string Role, string Template);

public sealed class ParserBinding
{
    public IResponseParser Parser { get; }
    public IIncrementalDataContainer Target { get; set; }

    public ParserBinding(IResponseParser parser, IIncrementalDataContainer target)
    {
        Parser = parser;
        Target = target;
    }
}
