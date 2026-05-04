using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Infrastructure.Llm;

public sealed class StructuredLlmContainer
{
    private readonly ILlmClient _llmClient;
    private readonly ILogService? _logService;
    private readonly IReadOnlyList<IResponseParser> _parsers;
    private readonly IReadOnlyList<PromptSection> _promptSections;
    private readonly IPromptTemplateResolver _resolver;

    public StructuredLlmContainer(
        ILlmClient llmClient,
        IPromptTemplateResolver resolver,
        IReadOnlyList<PromptSection> promptSections,
        IReadOnlyList<IResponseParser> parsers,
        ILogService? logService = null)
    {
        _llmClient = llmClient;
        _resolver = resolver;
        _promptSections = promptSections;
        _parsers = parsers;
        _logService = logService;
    }

    public int ParserCount => _parsers.Count;

    public async Task<StructuredLlmResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> data,
        IReadOnlyDictionary<string, IncrementalDigestContainer> containers,
        IReadOnlyList<IIncrementalDataContainer> targets,
        LlmConfig llmConfig,
        CancellationToken cancellationToken = default,
        IAccumulatingDataProvider? accumulatingProvider = null,
        string? accumulatingKey = null)
    {
        if (targets.Count != _parsers.Count)
            throw new ArgumentException(
                $"Target count ({targets.Count}) must match parser count ({_parsers.Count}).");

        var effectiveData = data;
        if (accumulatingProvider is not null && accumulatingKey is not null)
            effectiveData = ApplyAccumulation(data, accumulatingProvider, accumulatingKey);

        var messages = _promptSections
            .Select(s => new ChatMessage(s.Role, _resolver.Resolve(s.Template, effectiveData, containers)))
            .ToList();

        var totalChars = messages.Sum(m => m.Content.Length);
        _logService?.Info($"结构化 LLM 请求: 总提示词 {totalChars} 字符 "
                          + $"({string.Join(", ", messages.Select(m => $"{m.Role}={m.Content.Length}"))})");

        var (response, usage) = await _llmClient.CompleteAsync(llmConfig, messages, cancellationToken);

        var totalOperations = 0;
        for (var i = 0; i < _parsers.Count; i++)
        {
            var operations = _parsers[i].Parse(response);
            if (operations.Count > 0)
            {
                targets[i].ApplyOperations(operations);
                totalOperations += operations.Count;
            }
        }

        _logService?.Info($"结构化 LLM 调用完成: {totalOperations} 个操作, "
                          + $"响应 {response.Length} 字符"
                          + (usage is not null
                              ? $", {usage.PromptTokens} in + {usage.CompletionTokens} out = {usage.TotalTokens} tokens"
                              : ""));

        return new StructuredLlmResult(response, usage);
    }

    private static IReadOnlyDictionary<string, string> ApplyAccumulation(
        IReadOnlyDictionary<string, string> data,
        IAccumulatingDataProvider provider,
        string key)
    {
        if (!data.TryGetValue(key, out var originalValue))
            return data;

        var accumulated = provider.Accumulate(key, originalValue);
        if (ReferenceEquals(accumulated, originalValue))
            return data;

        var mutated = new Dictionary<string, string>(data) { [key] = accumulated };
        return mutated;
    }
}

public sealed record StructuredLlmResult(string Response, LlmUsage? Usage);