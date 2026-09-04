namespace CoaiBench.Model;

/// <summary>One plan and the commit that implemented it — the unit a run is measured over.</summary>
/// <param name="Name">How the tables refer to it.</param>
/// <param name="PlanFile">The plan or scope text, as a path in the repository.</param>
/// <param name="Commit">The commit to review, or empty for a plan-only case.</param>
/// <param name="BaseRef">What that commit is read against — normally its parent.</param>
public sealed record Case(string Name, string PlanFile, string Commit = "", string BaseRef = "");

/// <summary>What a stage is asked to do. `Both` is the ordinary shape of a real change.</summary>
public enum Stages
{
    Plans,
    Diffs,
    Both,
}

/// <summary>
/// One reviewer's line in a round, kept whole.
/// </summary>
/// <remarks>
/// Recorded rather than summarised because the summary anybody wants is decided AFTER the run. A
/// bench that keeps only its own counters can answer one question, and it is always the question
/// somebody thought of first.
/// </remarks>
public sealed record Finding(
    string Severity = "",
    string Category = "",
    string File = "",
    int Line = 0,
    string Title = "",
    string Why = "",
    string Fix = "",
    string Role = "",
    bool IsGating = false,
    IReadOnlyList<string>? Providers = null)
{
    /// <summary>
    /// Whether this finding was worth having — filled by the JUDGE pass, never by the run.
    /// </summary>
    /// <remarks>
    /// Deliberately absent until somebody judges. Counting findings ranks noise above insight: read
    /// one at a time against the code they name, the ranking by count inverted
    /// (<c>research/RESULTS_findings_that_are_worth_something.md</c>). So the bench records, and a
    /// second pass decides.
    /// </remarks>
    public string Useful { get; init; } = "unjudged";

    /// <summary>Why the judge said so, in its own words. Empty while unjudged.</summary>
    public string Verdict { get; init; } = string.Empty;
}

/// <summary>What one stage of one round cost and produced.</summary>
public sealed record StageResult(
    string Stage,
    double Seconds,
    string Verdict = "",
    string Error = "",
    int GatingCount = 0,
    string Reviewers = "",
    long TokensIn = 0,
    long TokensOut = 0,
    double? CostUsd = null)
{
    public IReadOnlyList<Finding> Findings { get; init; } = [];
}

/// <summary>
/// One run: one case, one arm of the matrix, one server process, from start to finish.
/// </summary>
/// <param name="Case">Which plan and commit.</param>
/// <param name="Arm">What made this run different from its neighbours — the label the table groups by.</param>
/// <param name="Repeat">Which repetition of that cell this is, from 1.</param>
/// <param name="Lane">Which parallel lane ran it, from 1. Always 1 when nothing is parallel.</param>
public sealed record RunRecord(Case Case, string Arm, int Repeat, int Lane)
{
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;

    public DateTime FinishedUtc { get; init; }

    public IReadOnlyList<StageResult> Stages { get; init; } = [];

    /// <summary>A failure of the HARNESS, which is a different thing from a round that failed.</summary>
    public string HarnessError { get; init; } = string.Empty;

    /// <summary>The last of the server's own stderr, for when nothing else explains it.</summary>
    public string ServerSaid { get; init; } = string.Empty;

    /// <summary>
    /// What the session file said afterwards — checked, never assumed.
    /// </summary>
    /// <remarks>
    /// An answer and the state behind it are different things, and believing the first was the whole
    /// of an afternoon's defect: findings came back numbered while the round on disk still said
    /// `running` with nothing pending, so every index pointed into a list nobody had written.
    /// </remarks>
    public Running.SessionOnDisk? OnDisk { get; init; }
}
