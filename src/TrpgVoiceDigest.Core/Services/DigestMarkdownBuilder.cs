using System.Text;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public static class DigestMarkdownBuilder
{
    public static string BuildGroupedSection(string title, IReadOnlyList<DigestTagGroup> groups, string emptyText = "- (none)")
    {
        if (groups.Count == 0)
            return $"{title}\n\n{emptyText}\n";

        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine();
        foreach (var group in groups)
        {
            sb.AppendLine($"## {group.Tag}");
            foreach (var (key, content) in group.Items)
                sb.AppendLine($"- **{key}**: {content}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string BuildKvpSection(string title, IReadOnlyDictionary<string, string> entries, string emptyText = "- (none)")
    {
        if (entries.Count == 0)
            return $"{title}\n\n{emptyText}\n";

        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine();
        foreach (var pair in entries.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"- **{pair.Key}**: {pair.Value}");

        return sb.ToString();
    }

    public static string BuildDigest(DigestState state) =>
        BuildGroupedSection("# 当前摘录", state.GetTagGroupsExcludingTag(DigestState.ConsistencyTag), "暂无摘录条目。");

    public static string BuildConsistency(DigestState state) =>
        BuildGroupedSection("# 一致性参考", state.GetTagGroupsByTag(DigestState.ConsistencyTag), "暂无一致性条目。");

    public static string BuildActiveTasks(DigestState state) =>
        BuildKvpSection("# 活跃任务", state.ActiveTasks, "暂无活跃任务。");

    public static string BuildCompletedTasks(DigestState state) =>
        BuildKvpSection("# 已完成任务", state.CompletedTasks, "暂无已完成任务。");

    public static string BuildStory(DigestState state) =>
        BuildKvpSection("# 故事进展", state.StoryEntries, "暂无故事进展记录。");
}
