using System.Text;
using System.Text.Json;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public static class RefinementPromptComposer
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };

    public static string BuildUserPrompt(
        string mergedDialogueText,
        RefinementState state,
        string refinementRequirementsPrompt,
        string protocolPrompt)
    {
        var stateJson = JsonSerializer.Serialize(new
        {
            sentences = state.Sentences.Select(s => new { s.Number, s.Text })
        }, IndentedOptions);

        var builder = new StringBuilder();
        builder.AppendLine("## 当前轮次合并对话（带句子编号）");
        builder.AppendLine(mergedDialogueText);
        builder.AppendLine();
        builder.AppendLine("## 当前精炼状态");
        builder.AppendLine(stateJson);
        builder.AppendLine();
        builder.AppendLine(refinementRequirementsPrompt);
        builder.AppendLine();
        builder.AppendLine("## 输出协议");
        builder.AppendLine(protocolPrompt);
        return builder.ToString();
    }
}
