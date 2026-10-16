using System.Text;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>What a completed plan round records about the text it reviewed — one call from the engine, so the engine gains one line.</summary>
/// <remarks>
/// <para>For a committed read the reader's <see cref="PlanSource"/> comes with the stage; for a pasted plan it is made
/// here from the text itself (<c>Origin</c> text, the UTF-8 byte count, the id of the text sent). Either way
/// <see cref="PlanSource.Commit"/> is the engine's OWN resolved commit, so a pasted round — which no reader ever
/// resolved — has one too, and a committed one agrees with the commit the round ran on.</para>
/// <para>Only a PLAN round has one: a code, document or feature round reviews no plan, and a skipped round is recorded
/// by another road that never calls this.</para>
/// </remarks>
internal static class PlanSources
{
    public static PlanSource? Stamp(StageRun stage, string planText, string sha) =>
        stage.Stage == Stage.PlanReview
            ? (stage.PlanSource ?? Pasted(planText)) with { Commit = sha }
            : null;

    private static PlanSource Pasted(string planText) =>
        new(PlanOrigin.Text, string.Empty, string.Empty, string.Empty, Encoding.UTF8.GetByteCount(planText), DocumentRules.ArtifactIdOf(planText));
}
