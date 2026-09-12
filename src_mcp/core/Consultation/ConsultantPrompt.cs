using System.Text;

namespace CoaiMcp.Core.Consultation;

/// <summary>Everything one turn's prompt is built from. Pure data; the server fills it.</summary>
/// <param name="Instruction">The consultant's own prompt, from the catalog (override-first).</param>
/// <param name="WorkingTree">The shaped diff — empty when this turn does not carry one.</param>
/// <param name="TreeUnchangedSinceTurnOne">A remembering vendor's later turn: say the tree has not moved instead of re-sending it.</param>
/// <param name="CarriedTranscript">A forgetful vendor's earlier turns, already bounded. Empty otherwise.</param>
/// <param name="PreviousAnswerLost">The previous turn was interrupted after the vendor accepted it.</param>
public sealed record ConsultantPromptInput(
    string Instruction,
    TurnBudget Budget,
    string Nonce,
    string Problem,
    IReadOnlyList<string> SuspectedFiles,
    string Branch,
    string HeadSha,
    string WorkingTree = "",
    bool TreeUnchangedSinceTurnOne = false,
    string CarriedTranscript = "",
    bool PreviousAnswerLost = false);

/// <summary>
/// Composes the prompt a consultant reads, in an order that is a rule rather than a habit.
/// </summary>
/// <remarks>
/// <para>The long untrusted thing — the diff — is fenced and comes early; the short thing that must
/// survive it — the caller's problem — comes LAST. <c>chatPrompt.ts</c> wrote the rule down first:
/// recency goes to what must not be lost in a long turn.</para>
/// <para>The budget line is here and nowhere else (see <see cref="TurnBudget"/>). What the consultant
/// HAS is composed here rather than written into the prompt file, because the file is a person's to
/// edit and the fact about the launch is ours to state.</para>
/// </remarks>
public static class ConsultantPrompt
{
    /// <summary>
    /// The whole shaped diff is bounded here — a third of the review budget.
    /// </summary>
    /// <remarks>
    /// Turn 1 carries the diff and <c>agy</c> has no prompt cache (phase 0b: 13.9k tokens in, then
    /// 30.6k), so the expensive turn is the first and it is the one to bound. A 76 KB prompt reached
    /// codex on stdin in five seconds, so this is a spending decision, not a transport one.
    /// </remarks>
    public const int DiffBudget = 64 * 1024;

    public static string Compose(ConsultantPromptInput input)
    {
        var text = new StringBuilder();
        text.AppendLine(input.Instruction.TrimEnd()).AppendLine();
        text.AppendLine("## What you have");
        text.AppendLine("A READ-ONLY checkout in your working directory, and the uncommitted change below. "
                        + "Do not edit anything. The AI asking is blocked on your answer.");
        text.AppendLine();
        text.AppendLine("## The budget");
        text.AppendLine(input.Budget.Line());
        text.AppendLine();
        AppendTree(text, input);
        AppendMemory(text, input);
        AppendQuestion(text, input);

        return text.ToString();
    }

    private static void AppendTree(StringBuilder text, ConsultantPromptInput input)
    {
        if (input.WorkingTree.Length > 0)
        {
            text.AppendLine($"## The uncommitted change in this working tree (on `{input.Branch}` at {input.HeadSha})");
            text.AppendLine(ConsultationFence.Material("the working tree", input.Nonce, input.WorkingTree));
            text.AppendLine();
        }
        else if (input.TreeUnchangedSinceTurnOne)
        {
            text.AppendLine("## The working tree");
            text.AppendLine("The change you were shown in turn 1 has not moved: the caller is blocked while you answer.");
            text.AppendLine();
        }
    }

    private static void AppendMemory(StringBuilder text, ConsultantPromptInput input)
    {
        if (input.CarriedTranscript.Length > 0)
        {
            text.AppendLine("## What has been said so far");
            text.AppendLine(ConsultationFence.Material("the conversation so far", input.Nonce, input.CarriedTranscript));
            text.AppendLine();
        }

        if (input.PreviousAnswerLost)
        {
            text.AppendLine("Your previous answer did not arrive — the process ended before it was read. Repeat it briefly before going on.");
            text.AppendLine();
        }
    }

    private static void AppendQuestion(StringBuilder text, ConsultantPromptInput input)
    {
        if (input.SuspectedFiles.Count > 0)
        {
            text.AppendLine("## Files the caller suspects");
            foreach (var file in input.SuspectedFiles)
            {
                text.AppendLine($"- {file}");
            }

            text.AppendLine();
        }

        text.AppendLine(input.Budget.Turn == 1
            ? "## The question"
            : "## What the caller verified since your last advice, and asks now");
        text.Append(input.Problem.Trim());
    }
}
