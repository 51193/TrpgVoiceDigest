using System.Text;
using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Core.Services;

public static class ConsistencyPromptComposer
{
    public static string BuildPrompt(
        ConsistencyState state,
        string mergedDialogueText,
        string refinementMarkdown)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## 当前精炼结果（最近的剧情摘要）");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(refinementMarkdown))
        {
            sb.AppendLine(refinementMarkdown);
        }
        else
        {
            sb.AppendLine("(暂无)");
        }
        sb.AppendLine();

        sb.AppendLine("## 当前上下文一致性表");
        sb.AppendLine();
        if (state.Entries.Count == 0)
        {
            sb.AppendLine("(空 — 请根据精炼结果添加新的专有名词条目)");
        }
        else
        {
            foreach (var entry in state.Entries)
                sb.AppendLine($"- **{entry.Key}**: {entry.Value}");
        }
        sb.AppendLine();

        sb.AppendLine("请根据以上精炼结果，检查并更新一致性表：");
        sb.AppendLine("- 添加新出现的人名、地名、组织名、机制名");
        sb.AppendLine("- 更新描述不准确的现有条目");
        sb.AppendLine("- 移除已不再相关的过期条目");

        return sb.ToString();
    }
}
