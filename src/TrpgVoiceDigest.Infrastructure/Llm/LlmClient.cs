using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TrpgVoiceDigest.Core.Config;
using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Infrastructure.Llm;

public sealed class LlmClient : ILlmClient
{
    private static readonly Regex EnvNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private readonly IEnvironmentKeyResolver _environmentKeyResolver;
    private readonly HttpClient _httpClient;
    private readonly ILogService? _logService;

    public LlmClient(HttpClient httpClient, IEnvironmentKeyResolver? environmentKeyResolver = null,
        ILogService? logService = null)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
        _environmentKeyResolver = environmentKeyResolver ?? new PlatformEnvironmentKeyResolver();
        _logService = logService;
    }

    public async Task<(string Content, LlmUsage? Usage)> CompleteAsync(
        LlmConfig config,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        return await CompleteAsync(config, new ChatMessage[]
        {
            new("system", systemPrompt),
            new("user", userPrompt)
        }, cancellationToken);
    }

    public async Task<(string Content, LlmUsage? Usage)> CompleteAsync(
        LlmConfig config,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var envName = config.ApiKeyEnv?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(envName) || !EnvNameRegex.IsMatch(envName))
            throw new InvalidOperationException(
                $"Llm.ApiKeyEnv 配置无效。该字段应填写环境变量名（例如 DEEPSEEK_API_KEY），当前值: '{config.ApiKeyEnv}'.");

        var apiKey = _environmentKeyResolver.Resolve(envName);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"环境变量 {envName} 未设置。请在当前进程环境中设置该变量（Linux 可在登录 shell 中导出后重启应用）。");

        var maxRetries = Math.Max(0, config.RetryCount);
        for (var attempt = 0; attempt <= maxRetries; attempt++)
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var messagesArray = new JsonArray();
                foreach (var msg in messages)
                    messagesArray.Add(new JsonObject { ["role"] = msg.Role, ["content"] = msg.Content });

                var bodyNode = new JsonObject
                {
                    ["model"] = config.Model,
                    ["messages"] = messagesArray,
                    ["temperature"] = config.Temperature,
                    ["max_tokens"] = config.MaxTokens
                };

                if (config.ThinkingEnabled)
                {
                    bodyNode["thinking"] = new JsonObject { ["type"] = "enabled" };
                    var tier = ResolveThinkingTier(config);
                    if (tier.Length > 0)
                        bodyNode["reasoning_effort"] = tier;
                    _logService?.Info($"LLM 思考模式已启用: reasoning_effort={tier}");
                }
                else
                {
                    bodyNode["thinking"] = new JsonObject { ["type"] = "disabled" };
                }

                var bodyJson = bodyNode.ToJsonString();
                request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

                using var timeoutCts =
                    new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(config.TimeoutSeconds, 5)));
                using var linkedCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                using var response = await _httpClient.SendAsync(request, linkedCts.Token);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(linkedCts.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var message = root.GetProperty("choices")[0].GetProperty("message");

                var content = message.GetProperty("content").GetString() ?? "EMPTY";

                if (config.ThinkingEnabled && message.TryGetProperty("reasoning_content", out var reasoning))
                {
                    var reasoningText = reasoning.GetString();
                    if (!string.IsNullOrWhiteSpace(reasoningText))
                        _logService?.Debug($"LLM 思考内容: {reasoningText.Length} 字符");
                }

                var usage = root.TryGetProperty("usage", out var usageElement)
                    ? LlmUsage.FromJsonElement(usageElement)
                    : null;

                if (usage is not null)
                    _logService?.Info(
                        $"LLM 请求成功: 响应 {content.Length} 字符, "
                        + $"tokens: {usage.PromptTokens} in + {usage.CompletionTokens} out = {usage.TotalTokens} total");
                else
                    _logService?.Info($"LLM 请求成功: 响应 {content.Length} 字符");

                return (content, usage);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries &&
                                                  (!ex.StatusCode.HasValue ||
                                                   ex.StatusCode == HttpStatusCode.TooManyRequests ||
                                                   (int)ex.StatusCode.Value >= 500))
            {
                var delay = 1000 * (int)Math.Pow(2, attempt);
                _logService?.Warning(
                    $"LLM 请求重试 (attempt {attempt + 1}/{maxRetries}, delay {delay}ms): {ex.StatusCode}");
                await Task.Delay(delay, cancellationToken);
            }

        return ("EMPTY", null);
    }

    private static string ResolveThinkingTier(LlmConfig config)
    {
        var tier = (config.ThinkingTier ?? "").Trim().ToLowerInvariant();
        return tier switch
        {
            "max" => "max",
            "high" => "high",
            _ => "high"
        };
    }
}