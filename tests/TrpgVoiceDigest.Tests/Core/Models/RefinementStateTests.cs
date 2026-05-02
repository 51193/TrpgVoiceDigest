using TrpgVoiceDigest.Core.Models;

namespace TrpgVoiceDigest.Tests.Core.Models;

public class RefinementStateTests
{
    [Fact]
    public void ApplyAdd_WithoutNumber_AppendsAtEnd()
    {
        var state = new RefinementState();
        state.ApplyAdd("第一句");
        state.ApplyAdd("第二句");

        Assert.Equal(2, state.Sentences.Count);
        Assert.Equal(1, state.Sentences[0].Number);
        Assert.Equal("第一句", state.Sentences[0].Text);
        Assert.Equal(2, state.Sentences[1].Number);
        Assert.Equal("第二句", state.Sentences[1].Text);
    }

    [Fact]
    public void ApplyAdd_WithNumber_InsertsAfter()
    {
        var state = new RefinementState();
        state.ApplyAdd("第一句");
        state.ApplyAdd("第三句");
        state.ApplyAdd("第二句", afterNumber: 1);

        Assert.Equal(3, state.Sentences.Count);
        Assert.Equal(1, state.Sentences[0].Number);
        Assert.Equal("第一句", state.Sentences[0].Text);
        Assert.Equal(2, state.Sentences[1].Number);
        Assert.Equal("第二句", state.Sentences[1].Text);
        Assert.Equal(3, state.Sentences[2].Number);
        Assert.Equal("第三句", state.Sentences[2].Text);
    }

    [Fact]
    public void ApplyAdd_WithInvalidAfterNumber_FallsBackToAppend()
    {
        var state = new RefinementState();
        state.ApplyAdd("第一句");
        state.ApplyAdd("第二句", afterNumber: 99);

        Assert.Equal(2, state.Sentences.Count);
        Assert.Equal(2, state.Sentences[1].Number);
        Assert.Equal("第二句", state.Sentences[1].Text);
    }

    [Fact]
    public void ApplyEdit_UpdatesText()
    {
        var state = new RefinementState();
        state.ApplyAdd("第一句");
        state.ApplyAdd("第二句");
        state.ApplyEdit(1, "修改后的第一句");

        Assert.Equal("修改后的第一句", state.Sentences[0].Text);
        Assert.Equal("第二句", state.Sentences[1].Text);
    }

    [Fact]
    public void ApplyEdit_NonexistentNumber_NoChange()
    {
        var state = new RefinementState();
        state.ApplyAdd("第一句");
        state.ApplyEdit(99, "不应该存在");

        Assert.Single(state.Sentences);
        Assert.Equal("第一句", state.Sentences[0].Text);
    }

    [Fact]
    public void ApplyRemove_RemovesAndRenumbers()
    {
        var state = new RefinementState();
        state.ApplyAdd("第一句");
        state.ApplyAdd("第二句");
        state.ApplyAdd("第三句");
        state.ApplyRemove(2);

        Assert.Equal(2, state.Sentences.Count);
        Assert.Equal(1, state.Sentences[0].Number);
        Assert.Equal("第一句", state.Sentences[0].Text);
        Assert.Equal(2, state.Sentences[1].Number);
        Assert.Equal("第三句", state.Sentences[1].Text);
    }

    [Fact]
    public void ApplyRemove_LastElement_NoRenumberNeeded()
    {
        var state = new RefinementState();
        state.ApplyAdd("第一句");
        state.ApplyAdd("第二句");
        state.ApplyRemove(2);

        Assert.Single(state.Sentences);
        Assert.Equal(1, state.Sentences[0].Number);
    }

    [Fact]
    public void ApplyOperations_BatchEdit()
    {
        var state = new RefinementState();
        state.ApplyAdd("第一句");
        state.ApplyAdd("第二句");

        var ops = new List<RefineOperation>
        {
            new(RefineAction.Edit, 1, "修改1"),
            new(RefineAction.Add, null, "第三句"),
            new(RefineAction.Remove, 2, null),
        };
        state.ApplyOperations(ops);

        Assert.Equal(2, state.Sentences.Count);
        Assert.Equal(1, state.Sentences[0].Number);
        Assert.Equal("修改1", state.Sentences[0].Text);
        Assert.Equal(2, state.Sentences[1].Number);
        Assert.Equal("第三句", state.Sentences[1].Text);
    }

    [Fact]
    public void ApplyOperations_EmptyAction_Noop()
    {
        var state = new RefinementState();
        state.ApplyAdd("第一句");

        var ops = new List<RefineOperation> { new(RefineAction.Empty, null, null!) };
        state.ApplyOperations(ops);

        Assert.Single(state.Sentences);
    }

    [Fact]
    public void BuildMarkdown_EmptyState()
    {
        var state = new RefinementState();
        var md = state.BuildMarkdown();

        Assert.Contains("暂无精炼内容", md);
    }

    [Fact]
    public void BuildMarkdown_WithSentences()
    {
        var state = new RefinementState();
        state.ApplyAdd("第一句");
        state.ApplyAdd("第二句");

        var md = state.BuildMarkdown();

        Assert.Contains("第一句", md);
        Assert.Contains("第二句", md);
    }

    [Fact]
    public void ApplyAdd_EmptyText_Ignored()
    {
        var state = new RefinementState();
        state.ApplyAdd("");
        state.ApplyAdd("  ");

        Assert.Empty(state.Sentences);
    }
}
