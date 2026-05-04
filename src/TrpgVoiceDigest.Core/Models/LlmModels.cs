using System.Text.Json;

namespace TrpgVoiceDigest.Core.Models;

public sealed record LlmUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int CacheHitTokens = 0,
    int CacheMissTokens = 0)
{
    public static LlmUsage? FromJsonElement(JsonElement? usageElement)
    {
        if (usageElement is not { ValueKind: JsonValueKind.Object } usage)
            return null;

        var prompt = usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var pv) ? pv : 0;
        var completion = usage.TryGetProperty("completion_tokens", out var ct) && ct.TryGetInt32(out var cv) ? cv : 0;
        var total = usage.TryGetProperty("total_tokens", out var tt) && tt.TryGetInt32(out var tv) ? tv : 0;
        var cacheHit = usage.TryGetProperty("prompt_cache_hit_tokens", out var ht) && ht.TryGetInt32(out var hv)
            ? hv
            : 0;
        var cacheMiss = usage.TryGetProperty("prompt_cache_miss_tokens", out var mt) && mt.TryGetInt32(out var mv)
            ? mv
            : 0;
        return new LlmUsage(prompt, completion, total, cacheHit, cacheMiss);
    }
}