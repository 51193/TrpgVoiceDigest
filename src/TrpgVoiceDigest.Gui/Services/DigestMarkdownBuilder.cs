using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Gui.Services;

public static class DigestMarkdownBuilder
{
    public static string BuildDigest(DigestState state)
    {
        var groups = state.GetTagGroupsExcludingTag(DigestState.ConsistencyTag);
        if (groups.Count == 0)
        {
            return "# 当前摘录\n\n暂无摘录条目。";
        }

        var sb = new StringBuilder();
        sb.AppendLine("# 当前摘录");
        sb.AppendLine();
        foreach (var group in groups)
        {
            sb.AppendLine($"## {group.Tag}");
            foreach (var (key, content) in group.Items)
            {
                sb.AppendLine($"- **{key}**: {content}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string BuildConsistency(DigestState state)
    {
        var groups = state.GetTagGroupsByTag(DigestState.ConsistencyTag);
        if (groups.Count == 0)
        {
            return "# 一致性参考\n\n暂无一致性条目。";
        }

        var sb = new StringBuilder();
        sb.AppendLine("# 一致性参考");
        sb.AppendLine();
        foreach (var group in groups)
        {
            sb.AppendLine($"## {group.Tag}");
            foreach (var (key, content) in group.Items)
            {
                sb.AppendLine($"- **{key}**: {content}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string BuildActiveTasks(DigestState state)
    {
        return BuildSimpleKvpSection("# 活跃任务", state.ActiveTasks, "暂无活跃任务。");
    }

    public static string BuildCompletedTasks(DigestState state)
    {
        return BuildSimpleKvpSection("# 已完成任务", state.CompletedTasks, "暂无已完成任务。");
    }

    public static string BuildStory(DigestState state)
    {
        return BuildSimpleKvpSection("# 故事进展", state.StoryEntries, "暂无故事进展记录。");
    }

    private static string BuildSimpleKvpSection(string title, IReadOnlyDictionary<string, string> entries, string emptyText)
    {
        if (entries.Count == 0)
        {
            return $"{title}\n\n{emptyText}";
        }

        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine();
        foreach (var pair in entries.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"- **{pair.Key}**: {pair.Value}");
        }

        return sb.ToString();
    }
}
