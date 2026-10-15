namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The model a ledger line names (decision 75 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): the one that RAN, not the one the row said.
/// </summary>
/// <remarks>
/// An adapter that resolves a default for a row that names none (Kimi's <c>kimi-code/k3-256k</c>, Vibe's <c>mistral-medium-3.5</c>) puts it on the launch it
/// builds, and the row's own model stays empty — so a line written from the row said nothing for a model that was run. Every other adapter copies the row's
/// model into its launch, so the two agree for them, and a CLI that runs on its own default names none and the line stays empty rather than inventing one.
/// ONE rule for every ledger path — a reviewer's line, a consultation's, a check's, a question row's.
/// </remarks>
public static class ModelRan
{
    /// <summary>The launch's resolved model when it names one, the row's otherwise.</summary>
    public static string Of(string launchs, string rows) => launchs.Length > 0 ? launchs : rows;
}
