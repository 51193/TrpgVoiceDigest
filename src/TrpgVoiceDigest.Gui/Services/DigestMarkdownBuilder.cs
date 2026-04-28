using System;
using System.Text;
using System.Linq;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Gui.Services;

public static class DigestMarkdownBuilder
{
    public static string Build(DigestState state)
    {
        if (state.Entries.Count == 0)
        {
            return "# 当前摘录\n\n暂无摘录条目。";
        }

        var grouped = state.Entries
            .SelectMany(x => x.Value.Tags.Select(tag => new { Tag = tag, Key = x.Key, Value = x.Value.Content }))
            .GroupBy(x => x.Tag)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("# 当前摘录");
        sb.AppendLine();
        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            foreach (var item in group)
            {
                sb.AppendLine($"- **{item.Key}**: {item.Value}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
