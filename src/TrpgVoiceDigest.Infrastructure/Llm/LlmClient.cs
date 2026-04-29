using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Infrastructure.Llm;

public sealed class LlmClient
{
    private static readonly Regex EnvNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private readonly HttpClient _httpClient;
    private readonly IEnvironmentKeyResolver _environmentKeyResolver;

    public LlmClient(HttpClient httpClient, IEnvironmentKeyResolver? environmentKeyResolver = null)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
        _environmentKeyResolver = environmentKeyResolver ?? new PlatformEnvironmentKeyResolver();
    }

    public async Task<string> CompleteAsync(
        LlmConfig config,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var envName = config.ApiKeyEnv?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(envName) || !EnvNameRegex.IsMatch(envName))
        {
            throw new InvalidOperationException(
                $"Llm.ApiKeyEnv 配置无效。该字段应填写环境变量名（例如 DEEPSEEK_API_KEY），当前值: '{config.ApiKeyEnv}'.");
        }

        var apiKey = _environmentKeyResolver.Resolve(envName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"环境变量 {envName} 未设置。请在当前进程环境中设置该变量（Linux 可在登录 shell 中导出后重启应用）。");
        }

        var maxRetries = Math.Max(0, config.RetryCount);
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var body = new
                {
                    model = config.Model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userPrompt }
                    },
                    temperature = config.Temperature,
                    max_tokens = config.MaxTokens
                };
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(config.TimeoutSeconds, 5)));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                using var response = await _httpClient.SendAsync(request, linkedCts.Token);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(linkedCts.Token);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
                       ?? "EMPTY";
            }
            catch (HttpRequestException ex) when (attempt < maxRetries &&
                                                  (!ex.StatusCode.HasValue ||
                                                   ex.StatusCode == HttpStatusCode.TooManyRequests ||
                                                   (int)ex.StatusCode.Value >= 500))
            {
                var delay = 1000 * (int)Math.Pow(2, attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }

        return "EMPTY";
    }
}
