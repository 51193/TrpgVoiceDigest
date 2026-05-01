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

    public static string BuildUserPromptResolved(
        string transcriptText,
        DigestState state,
        string consistencyLexiconText,
        string characterCardsText,
        string processingRequirementsPrompt,
        string protocolPrompt,
        IReadOnlyDictionary<string, string> speakerNameMap)
    {
        var resolvedTranscript = ResolveSpeakerNames(transcriptText, speakerNameMap);
        return BuildUserPrompt(resolvedTranscript, state, consistencyLexiconText, characterCardsText,
            processingRequirementsPrompt, protocolPrompt);
    }

    private static string ResolveSpeakerNames(string dialogueText, IReadOnlyDictionary<string, string> speakerNameMap)
    {
        if (speakerNameMap.Count == 0 || string.IsNullOrWhiteSpace(dialogueText)) return dialogueText;

        var sb = new StringBuilder();
        foreach (var line in dialogueText.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            var match = System.Text.RegularExpressions.Regex.Match(trimmed,
                @"^\[(?<time>\d{2}:\d{2}:\d{2})\]\s*(?:\[(?<speaker>[^\]]+)\]:?\s*)?(?<text>.+)$");
            if (!match.Success)
            {
                sb.AppendLine(line);
                continue;
            }

            var time = match.Groups["time"].Value;
            var speaker = match.Groups["speaker"].Success ? match.Groups["speaker"].Value : "";
            var text = match.Groups["text"].Value.Trim();

            if (speaker.Length > 0 && speakerNameMap.TryGetValue(speaker, out var resolved))
                speaker = resolved;

            var speakerPart = speaker.Length > 0 ? $"[{speaker}]: " : "";
            sb.AppendLine($"[{time}] {speakerPart}{text}");
        }

        return sb.ToString().TrimEnd();
    }
}