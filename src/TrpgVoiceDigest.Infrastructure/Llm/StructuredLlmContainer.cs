using System.Text.RegularExpressions;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Infrastructure.Llm;

public sealed partial class StructuredLlmContainer
{
    [GeneratedRegex("\\{\\{([a-z_][a-z0-9_]*)\\}\\}")]
    private static partial Regex MagicStringRegex();

    private readonly ILlmClient _llmClient;
    private readonly IReadOnlyList<PromptSection> _promptSections;
    private readonly IReadOnlyList<IResponseParser> _parsers;
    private readonly ILogService? _logService;

    public int ParserCount => _parsers.Count;

    public StructuredLlmContainer(
        ILlmClient llmClient,
        IReadOnlyList<PromptSection> promptSections,
        IReadOnlyList<IResponseParser> parsers,
        ILogService? logService = null)
    {
        _llmClient = llmClient;
        _promptSections = promptSections;
        _parsers = parsers;
        _logService = logService;
    }

    /// <summary>
    /// 执行一次结构化 LLM 调用。
    /// </summary>
    /// <param name="data">magic string 替换数据</param>
    /// <param name="targets">增量容器列表，按索引与构造时注入的 parsers 一一对应</param>
    /// <param name="llmConfig">LLM 配置</param>
    /// <param name="cancellationToken"></param>
    public async Task<StructuredLlmResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> data,
        IReadOnlyList<IIncrementalDataContainer> targets,
        LlmConfig llmConfig,
        CancellationToken cancellationToken = default)
    {
        if (targets.Count != _parsers.Count)
            throw new ArgumentException(
                $"Target count ({targets.Count}) must match parser count ({_parsers.Count}).");

        var messages = _promptSections
            .Select(s => new ChatMessage(s.Role, Substitute(s.Template, data)))
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

    private static string Substitute(string template, IReadOnlyDictionary<string, string> data)
    {
        return MagicStringRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (data.TryGetValue(key, out var value))
                return value;
            throw new ArgumentException(
                $"Magic string '{{{{{key}}}}}' not found in data bindings.");
        });
    }
}

public sealed record StructuredLlmResult(string Response, LlmUsage? Usage);
