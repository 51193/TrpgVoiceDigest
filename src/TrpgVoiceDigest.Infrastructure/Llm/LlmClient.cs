using System.Net;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using TrpgVoiceDigest.Core.Config;

namespace TrpgVoiceDigest.Infrastructure.Llm;

public sealed class LlmClient
{
    private static readonly Regex EnvNameRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private readonly HttpClient _httpClient;

    public LlmClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
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

        var apiKey = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = ReadFromLoginShellEnvironment(envName);
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"环境变量 {envName} 未设置。请在 shell 配置（如 ~/.bashrc）或当前终端设置该变量。");
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

    private static string? ReadFromLoginShellEnvironment(string envName)
    {
        return RunShellReadEnv("bash", $"-ic \"printenv {envName}\"")
               ?? RunShellReadEnv("bash", $"-lc \"printenv {envName}\"");
    }

    private static string? RunShellReadEnv(string shell, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
