using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Tests;

public class PromptComposerTests
{
    [Fact]
    public void BuildUserPrompt_IncludesMandatoryReviewChecklist()
    {
        var state = new DigestState();
        state.Entries["线索_1"] = new DigestEntry("酒馆有血迹", ["故事主线"]);
        state.ActiveTasks["任务_调查"] = "调查血迹来源";
        state.StoryEntries["进展_1"] = "队伍抵达酒馆";

        var prompt = PromptComposer.BuildUserPrompt(
            transcriptText: "我看到柜台后面有血。",
            state: state,
            consistencyLexiconText: "张三-旅店老板",
            characterCardsText: "### 人物卡：alice.md",
            protocolPrompt: "EMPTY");

        Assert.Contains("## 本轮处理要求（必须执行）", prompt);
        Assert.Contains("先基于上下文修正转录中的明显错别字、同音字、形近字与断句问题。", prompt);
        Assert.Contains("再逐条复核当前所有 digest/task/story 条目", prompt);
        Assert.Contains("只有在无新增信息且全部历史条目无需修订时，才返回 EMPTY。", prompt);
    }

    [Fact]
    public void BuildUserPrompt_ContainsTranscriptStateAndProtocolSections()
    {
        var state = new DigestState();

        var prompt = PromptComposer.BuildUserPrompt(
            transcriptText: "测试转录",
            state: state,
            consistencyLexiconText: "青铜钥匙-任务道具",
            characterCardsText: "### 人物卡：bob.md\n# 鲍勃",
            protocolPrompt: "digest add \"A\": {\"content\":\"B\",\"tags\":[\"世界观\"]}");

        Assert.Contains("## 当前场次对话文本", prompt);
        Assert.Contains("测试转录", prompt);
        Assert.Contains("## 当前摘录状态", prompt);
        Assert.Contains("## 一致性词汇表（名称或名称-简要描述）", prompt);
        Assert.Contains("青铜钥匙-任务道具", prompt);
        Assert.Contains("## Campaign人物卡（长期参考）", prompt);
        Assert.Contains("### 人物卡：bob.md", prompt);
        Assert.Contains("## 当前任务与故事状态", prompt);
        Assert.Contains("## 输出协议", prompt);
    }
}
