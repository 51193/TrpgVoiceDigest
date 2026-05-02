using System.Text;

namespace TrpgVoiceDigest.Core.Models;

public sealed class RefinedSentence
{
    public int Number { get; set; }
    public string Text { get; set; } = "";
}

public enum RefineAction
{
    Add,
    Edit,
    Remove,
    Empty
}

public sealed record RefineOperation(RefineAction Action, int? Number, string? Text) : IOperation;

public sealed class RefinementState : IIncrementalDataContainer
{
    public List<RefinedSentence> Sentences { get; init; } = [];

    public void ApplyAdd(string text, int? afterNumber = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        int insertIndex;
        int numberBase;

        if (afterNumber is not null && afterNumber.Value > 0)
        {
            var pos = Sentences.FindIndex(s => s.Number == afterNumber.Value);
            if (pos < 0)
            {
                Append(text);
                return;
            }

            insertIndex = pos + 1;
            numberBase = afterNumber.Value;
        }
        else
        {
            Append(text);
            return;
        }

        Sentences.Insert(insertIndex, new RefinedSentence { Number = numberBase + 1, Text = text.Trim() });
        RenumberFrom(insertIndex + 1);
    }

    public void ApplyEdit(int number, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var sentence = Sentences.Find(s => s.Number == number);
        if (sentence is null) return;

        sentence.Text = text.Trim();
    }

    public void ApplyRemove(int number)
    {
        var index = Sentences.FindIndex(s => s.Number == number);
        if (index < 0) return;

        Sentences.RemoveAt(index);
        if (index < Sentences.Count)
            RenumberFrom(index);
    }

    public void ApplyOperations(IReadOnlyList<RefineOperation> operations)
    {
        foreach (var op in operations)
            switch (op.Action)
            {
                case RefineAction.Add:
                    ApplyAdd(op.Text ?? "", op.Number);
                    break;
                case RefineAction.Edit:
                    if (op.Number is not null)
                        ApplyEdit(op.Number.Value, op.Text ?? "");
                    break;
                case RefineAction.Remove:
                    if (op.Number is not null)
                        ApplyRemove(op.Number.Value);
                    break;
                case RefineAction.Empty:
                    break;
            }
    }

    void IIncrementalDataContainer.ApplyOperations(IReadOnlyList<IOperation> operations)
    {
        var typed = operations.OfType<RefineOperation>().ToList();
        if (typed.Count > 0)
            ApplyOperations(typed);
    }

    public string BuildMarkdown()
    {
        if (Sentences.Count == 0)
            return "# 跑团剧本精炼\n\n暂无精炼内容。\n";

        var sb = new StringBuilder();
        sb.AppendLine("# 跑团剧本精炼");
        sb.AppendLine();
        foreach (var s in Sentences)
            sb.AppendLine(s.Text);
        sb.AppendLine();

        return sb.ToString();
    }

    private void Append(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var nextNumber = Sentences.Count == 0 ? 1 : Sentences[^1].Number + 1;
        Sentences.Add(new RefinedSentence { Number = nextNumber, Text = text.Trim() });
    }

    private void RenumberFrom(int startIndex)
    {
        if (startIndex < 0 || startIndex >= Sentences.Count) return;

        var baseNumber = startIndex == 0 ? 0 : Sentences[startIndex - 1].Number;
        for (var i = startIndex; i < Sentences.Count; i++)
            Sentences[i].Number = baseNumber + (i - startIndex + 1);
    }
}
