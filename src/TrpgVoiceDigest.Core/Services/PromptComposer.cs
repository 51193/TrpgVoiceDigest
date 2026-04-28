using System.Text;
using System.Text.Json;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public static class PromptComposer
{
    public static string BuildUserPrompt(
        string transcriptText,
        DigestState state,
        string protocolPrompt)
    {
        var stateJson = JsonSerializer.Serialize(state.Entries, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var builder = new StringBuilder();
        builder.AppendLine("## 当前场次对话文本");
        builder.AppendLine(transcriptText);
        builder.AppendLine();
        builder.AppendLine("## 当前摘录状态");
        builder.AppendLine(stateJson);
        builder.AppendLine();
        builder.AppendLine("## 当前任务与故事状态");
        builder.AppendLine(JsonSerializer.Serialize(new
        {
            activeTasks = state.ActiveTasks,
            completedTasks = state.CompletedTasks,
            storyEntries = state.StoryEntries
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        builder.AppendLine();
        builder.AppendLine("## 输出协议");
        builder.AppendLine(protocolPrompt);
        return builder.ToString();
    }
}
