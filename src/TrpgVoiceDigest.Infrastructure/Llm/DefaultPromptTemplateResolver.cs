using System.Text.RegularExpressions;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Infrastructure.Llm;

public sealed partial class DefaultPromptTemplateResolver : IPromptTemplateResolver
{
    private readonly string _appRoot;
    private readonly ILogService? _log;

    public DefaultPromptTemplateResolver(string appRoot, ILogService? log = null)
    {
        _appRoot = appRoot;
        _log = log;
    }

    public string Resolve(
        string template,
        IReadOnlyDictionary<string, string>? data = null,
        IReadOnlyDictionary<string, IncrementalDigestContainer>? containers = null)
    {
        data ??= new Dictionary<string, string>();
        containers ??= new Dictionary<string, IncrementalDigestContainer>();

        return TokenRegex().Replace(template, match =>
        {
            var expression = match.Groups[1].Value.Trim();

            if (expression.StartsWith("include:", StringComparison.Ordinal))
            {
                var filePath = expression["include:".Length..].Trim();
                return ResolveInclude(filePath, data!, containers!);
            }

            if (expression.StartsWith("container:", StringComparison.Ordinal))
            {
                var rest = expression["container:".Length..];
                return ResolveContainerToken(rest, containers!);
            }

            if (data!.TryGetValue(expression, out var value))
                return value;

            throw new ArgumentException(
                $"Magic string '{{{{{expression}}}}}' not found in data bindings. "
                + $"Available keys: [{string.Join(", ", data.Keys)}]");
        });
    }

    [GeneratedRegex("\\{\\{([^}]+)\\}\\}")]
    private static partial Regex TokenRegex();

    private string ResolveInclude(
        string filePath,
        IReadOnlyDictionary<string, string> data,
        IReadOnlyDictionary<string, IncrementalDigestContainer> containers)
    {
        var resolvedPath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.GetFullPath(Path.Combine(_appRoot, filePath));

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException(
                $"Include file not found: '{filePath}' (resolved: '{resolvedPath}')");

        var content = File.ReadAllText(resolvedPath);
        _log?.Debug($"模板 include: '{filePath}' ({content.Length} 字符)");
        return Resolve(content, data, containers);
    }

    private static string ResolveContainerToken(
        string rest,
        IReadOnlyDictionary<string, IncrementalDigestContainer> containers)
    {
        var parts = rest.Split(':', 2);
        var key = parts[0];
        var format = parts.Length > 1 ? parts[1] : "md";

        if (!containers.TryGetValue(key, out var container))
            throw new ArgumentException(
                $"Container '{{{key}}}' not found in containers dict. "
                + $"Available: [{string.Join(", ", containers.Keys)}]");

        return format switch
        {
            "json" => container.ExportJson(),
            "md" => container.ExportMarkdown(),
            _ => throw new ArgumentException(
                $"Unknown container format '{format}' for container '{key}'. Use 'json' or 'md'.")
        };
    }
}