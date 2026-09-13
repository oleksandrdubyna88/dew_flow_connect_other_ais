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

    /// <summary>
    /// The caller's own text is bounded too — it is the one part of the prompt nothing else caps.
    /// </summary>
    /// <remarks>
    /// A problem statement is a paragraph or two; an agent that pastes a whole log into it buys a
    /// vendor bill rather than an answer, and the diff it would crowd out is the part the consultant
    /// was asked here for. Sixteen kilobytes is generous for prose and small beside the diff budget.
    /// The list is capped for the same reason, and by COUNT as well as length: "the files I suspect"
    /// stops meaning anything past a couple of dozen. (codex, code round.)
    /// </remarks>
    public const int ProblemBudget = 16 * 1024;

    public const int SuspectedFileCap = 25;

    private const int PathBudget = 400;

    /// <summary>What a carry budget falls back to when a caller does not state one.</summary>
    public const int DefaultCarryBudget = 20 * 1024;

    /// <summary>The caller's problem, trimmed to its budget with the cut SAID rather than silent.</summary>
    public static string BoundedProblem(string problem)
    {
        var text = problem.Trim();

        return text.Length <= ProblemBudget
            ? text
            : text[..ProblemBudget] + $"\n… (the caller's problem statement was cut here at {ProblemBudget} characters)";
    }

    /// <summary>The suspected files, bounded in count and in length, with the count said when it was cut.</summary>
    public static IReadOnlyList<string> BoundedFiles(IReadOnlyList<string> files)
    {
        var kept = files
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim() is { Length: > PathBudget } long_ ? long_[..PathBudget] + "…" : f.Trim())
            .Take(SuspectedFileCap)
            .ToList();

        if (files.Count > SuspectedFileCap)
        {
            kept.Add($"… and {files.Count - SuspectedFileCap} more the caller named (not listed)");
        }

        return kept;
    }

    /// <summary>
    /// The conversation so far, for a vendor that keeps none — newest turns first, bounded, and the
    /// trimming SAID inside the text.
    /// </summary>
    /// <remarks>
    /// <para>Built from the record's own turns. Without it the <c>WeRemember</c> arm is a shape with no
    /// behaviour behind it, and the first vendor to use it would silently answer every follow-up with
    /// no memory of the one before. (codex, code round — before any such vendor exists, which is the
    /// only cheap moment to fix it.)</para>
    /// <para>The budget is the one the ADAPTER declared, not a constant here: <c>WeRemember</c> carries
    /// a <c>CarryBudget</c>, and a private number that ignored it would be a second field with no
    /// reader — the same defect one layer down. (codex and gemini, second code round.)</para>
    /// </remarks>
    public static string Transcript(IReadOnlyList<(string Problem, string Advice)> turns, int carryBudget = DefaultCarryBudget)
    {
        var budget = carryBudget > 0 ? carryBudget : DefaultCarryBudget;
        var kept = new List<string>();
        var spent = 0;
        for (var i = turns.Count - 1; i >= 0; i--)
        {
            var block = $"The caller asked:\n  {Indent(turns[i].Problem)}\nYou answered:\n  {Indent(turns[i].Advice)}";
            if (spent + block.Length > budget)
            {
                // The NEWEST turn alone can be bigger than the whole budget — a pasted stack trace
                // does it — and dropping it would carry nothing but a note, leaving the consultant
                // with no idea what it last said. It is CUT instead, with the cut said inside the
                // text, which is the rule the extension's own carry already follows.
                // (codex and gemini, this story's plan round.)
                if (kept.Count == 0)
                {
                    // The marker's own length is RESERVED before the slice, so the carry stays inside
                    // the budget it advertises: keeping `budget` characters and then appending a
                    // marker put a 16 KB carry over 16 KB, on the one route whose context window is
                    // the smallest here. (codex, code round.)
                    var room = Math.Max(budget - CutMarker.Length, 0);
                    kept.Add(block[..Math.Min(block.Length, room)] + CutMarker);
                }

                // `i` earlier turns, not `i + 1`: turn `i` is the one just handled — cut and kept
                // when it was the newest, dropped otherwise. Saying `i + 1` on turn 1 announced that
                // one earlier turn was lost when there had never been one. (gemini, code round.)
                if (i > 0)
                {
                    kept.Insert(0, $"(the earlier {i} turn(s) of this conversation are not carried — the budget was reached)");
                }

                break;
            }

            kept.Insert(0, block);
            spent += block.Length;
        }

        return string.Join("\n\n", kept);
    }

    /// <summary>Said inside the text, so a cut is never something the reader has to infer.</summary>
    private const string CutMarker = "\n  … [CUT: this turn is longer than the whole carry budget]";

    private static string Indent(string text) => text.Trim().Replace("\n", "\n  ");

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
