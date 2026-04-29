using TrpgVoiceDigest.Core.Models;
using TrpgVoiceDigest.Core.Services;

namespace TrpgVoiceDigest.Tests;

public class PromptComposerTests
{
    private const string ProcessingRequirements = "## 本轮处理要求（必须执行）\n\n1. ASR 纠错优先\n2. 一致性对齐\n3. 全量条目复核";

    [Fact]
    public void BuildUserPrompt_IncludesProcessingRequirements()
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
            processingRequirementsPrompt: ProcessingRequirements,
            protocolPrompt: "EMPTY");

        Assert.Contains("## 本轮处理要求（必须执行）", prompt);
        Assert.Contains("ASR 纠错优先", prompt);
        Assert.Contains("一致性对齐", prompt);
        Assert.Contains("全量条目复核", prompt);
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
            processingRequirementsPrompt: ProcessingRequirements,
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
