namespace TrpgVoiceDigest.Core.Models;

public interface IOperation;

public interface IIncrementalDataContainer
{
    void ApplyOperations(IReadOnlyList<IOperation> operations);
}

public interface IAccumulatingDataProvider
{
    string Accumulate(string key, string currentValue);
}

public interface IResponseParser
{
    IReadOnlyList<IOperation> Parse(string response);
}

public sealed record ChatMessage(string Role, string Content);

public sealed record PromptSection(string Role, string Template);

public sealed class ParserBinding
{
    public ParserBinding(IResponseParser parser, IIncrementalDataContainer target)
    {
        Parser = parser;
        Target = target;
    }

    public IResponseParser Parser { get; }
    public IIncrementalDataContainer Target { get; set; }
}