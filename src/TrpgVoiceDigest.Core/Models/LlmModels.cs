namespace TrpgVoiceDigest.Core.Models;

public sealed record LlmUsage(int PromptTokens, int CompletionTokens, int TotalTokens)
{
    public static LlmUsage? FromJsonElement(System.Text.Json.JsonElement? usageElement)
    {
        if (usageElement is not { ValueKind: System.Text.Json.JsonValueKind.Object } usage)
            return null;

        var prompt = usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var pv) ? pv : 0;
        var completion = usage.TryGetProperty("completion_tokens", out var ct) && ct.TryGetInt32(out var cv) ? cv : 0;
        var total = usage.TryGetProperty("total_tokens", out var tt) && tt.TryGetInt32(out var tv) ? tv : 0;
        return new LlmUsage(prompt, completion, total);
    }
}
