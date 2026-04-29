using System.Text;
using System.Text.Json;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public static class PromptComposer
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };

    public static string BuildUserPrompt(
        string transcriptText,
        DigestState state,
        string consistencyLexiconText,
        string characterCardsText,
        string processingRequirementsPrompt,
        string protocolPrompt)
    {
        var stateJson = JsonSerializer.Serialize(state.Entries, IndentedOptions);

        var builder = new StringBuilder();
        builder.AppendLine("## 当前场次对话文本");
        builder.AppendLine(transcriptText);
        builder.AppendLine();
        builder.AppendLine("## 当前摘录状态");
        builder.AppendLine(stateJson);
        builder.AppendLine();
        builder.AppendLine("## 一致性词汇表（名称或名称-简要描述）");
        builder.AppendLine(string.IsNullOrWhiteSpace(consistencyLexiconText) ? "(空)" : consistencyLexiconText);
        builder.AppendLine();
        builder.AppendLine("## Campaign人物卡（长期参考）");
        builder.AppendLine("以下人物卡为长期背景参考，请始终据此保持人物身份、关系和目标的一致性。");
        builder.AppendLine(string.IsNullOrWhiteSpace(characterCardsText) ? "(空)" : characterCardsText);
        builder.AppendLine();
        builder.AppendLine("## 当前任务与故事状态");
        builder.AppendLine(JsonSerializer.Serialize(new
        {
            activeTasks = state.ActiveTasks,
            completedTasks = state.CompletedTasks,
            storyEntries = state.StoryEntries
        }, IndentedOptions));
        builder.AppendLine();
        builder.AppendLine(processingRequirementsPrompt);
        builder.AppendLine();
        builder.AppendLine("## 输出协议");
        builder.AppendLine(protocolPrompt);
        return builder.ToString();
    }
}