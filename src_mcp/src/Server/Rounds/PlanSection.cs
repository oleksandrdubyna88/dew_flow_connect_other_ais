using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// The plan section of a plan reviewer's prompt — the instruction, the heading, the plan, and the line that says
/// where the plan ends.
/// </summary>
/// <remarks>
/// <para>Parent D12 (<c>todo/PLAN_the_plan_gate_reads_the_committed_plan.md</c>): a plan the classifier cut, or a body
/// that is only a pointer, was reviewed clean. The last line names the plan by <see cref="DocumentRules.ArtifactIdOf"/> —
/// a plan cannot forge it, because the id is the hash of the very text that contains it — and a reviewer that cannot see
/// it is told to say so in a finding of a fixed title, which <see cref="PlanIncomplete"/> reads.</para>
/// <para>A detector, not a proof: it catches a cut tail, a pointer-only body and a reviewer's own statement that it saw
/// no plan. It cannot say what a model attended to.</para>
/// </remarks>
internal static class PlanSection
{
    /// <summary>The title of the one finding a reviewer answers when the plan did not reach it whole.</summary>
    internal static string IncompleteTitle(string id) => "PLAN_INCOMPLETE " + id;

    /// <summary>The section: the instruction, a blank line, the heading, the optional source line, the plan, its end line.</summary>
    /// <param name="sourceLine">What the server read the plan from; empty for pasted text.</param>
    internal static string Compose(string planText, string sourceLine)
    {
        var id = DocumentRules.ArtifactIdOf(planText);
        var source = sourceLine.Length > 0 ? sourceLine + "\n" : string.Empty;

        return Instruction(id) + "\n\n## The plan under review\n" + source + "\n" + planText + "\n\n" + EndLine(id);
    }

    private static string EndLine(string id) => "END OF PLAN " + id;

    private static string Instruction(string id) =>
        $"The last line of the plan below is `{EndLine(id)}`. If you cannot see that line, or the section holds no plan "
        + "body (only a path or a pointer), answer with exactly one blocking finding titled exactly "
        + $"`{IncompleteTitle(id)}` and nothing else.";
}
