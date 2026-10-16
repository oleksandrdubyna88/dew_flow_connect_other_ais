using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// The plan section of a plan reviewer's prompt — the instruction, the heading, the plan between its two marker lines,
/// and a reminder just before the end.
/// </summary>
/// <remarks>
/// <para>Parent D12 (<c>todo/PLAN_the_plan_gate_reads_the_committed_plan.md</c>): a plan the classifier cut, or a body
/// that is only a pointer, was reviewed clean. The marker lines name the plan by <see cref="DocumentRules.ArtifactIdOf"/> —
/// a plan cannot forge them, because the id is the hash of the very text that contains it — and a reviewer that cannot see
/// both is told to say so in a finding of a fixed title, which <see cref="PlanIncomplete"/> reads.</para>
/// <para>A detector, not a proof: it catches a cut tail, a cut start, a pointer-only body and a reviewer's own statement
/// that it saw no plan. It cannot say what a model attended to.</para>
/// </remarks>
internal static class PlanSection
{
    /// <summary>The title of the finding a reviewer answers when the plan did not reach it whole.</summary>
    internal static string IncompleteTitle(string id) => "PLAN_INCOMPLETE " + id;

    /// <summary>
    /// The section: the instruction, the heading, the begin line, the optional source line, the plan, the reminder, the end line.
    /// </summary>
    /// <remarks>
    /// BOTH ends are marked. The end line catches a cut tail. The begin line catches a cut START — the instruction itself
    /// lives there — and the reminder, which sits just before the end line where a reviewer whose context lost its head
    /// still reads it, restates the rule in one line.
    /// </remarks>
    /// <param name="sourceLine">What the server read the plan from; empty for pasted text.</param>
    internal static string Compose(string planText, string sourceLine)
    {
        var id = DocumentRules.ArtifactIdOf(planText);
        var source = sourceLine.Length > 0 ? sourceLine + "\n" : string.Empty;

        return Instruction(id) + "\n\n## The plan under review\n" + BeginLine(id) + "\n" + source + "\n" + planText + "\n\n"
            + Reminder(id) + "\n" + EndLine(id);
    }

    private static string BeginLine(string id) => "BEGIN OF PLAN " + id;

    private static string EndLine(string id) => "END OF PLAN " + id;

    private static string Instruction(string id) =>
        $"The plan below is marked by `{BeginLine(id)}` (the first line under its heading) and `{EndLine(id)}` (its last line). "
        + "If you cannot see BOTH, or the section holds no plan body (only a path or a pointer), answer with exactly one "
        + $"blocking finding titled exactly `{IncompleteTitle(id)}` and nothing else.";

    private static string Reminder(string id) =>
        $"Reminder: if the first line under the heading was not `{BeginLine(id)}`, answer with exactly one blocking finding "
        + $"titled exactly `{IncompleteTitle(id)}` and nothing else.";
}
