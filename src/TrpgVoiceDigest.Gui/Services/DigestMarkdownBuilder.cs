using System.Text;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Gui.Services;

public static class DigestMarkdownBuilder
{
    public static string Build(DigestState state)
    {
        var groups = state.GetTagGroups();
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
}
