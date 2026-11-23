namespace CoaiMcp.Core.Work;

/// <summary>One thing wrong with a plan. <see cref="Path"/> is the file (or folder) from the feature folder, <see cref="Node"/> the item's folder path, <see cref="Message"/> the sentence.</summary>
public sealed record WorkError(string Code, string Path, string Node, string Message);

/// <summary>A task that must run on the <c>important</c> model list, and why.</summary>
public sealed record WorkImportant(string Node, string Reason);

/// <summary>How many items of each kind the plan has on disk.</summary>
public sealed record WorkCounts(int Epochs, int Epics, int Stories, int Tasks);

/// <summary>
/// What the validator answers: no feature file, or the counts, the errors (sorted, at most <see cref="WorkReport.MaxListed"/>), the notes and the
/// important tasks. <see cref="Truncated"/> says a list was cut.
/// </summary>
public sealed record WorkReport(
    bool FeatureFound,
    WorkCounts Counts,
    IReadOnlyList<WorkError> Errors,
    IReadOnlyList<string> Notes,
    IReadOnlyList<WorkImportant> Important,
    bool Truncated)
{
    /// <summary>The longest list of errors or notes in an answer.</summary>
    public const int MaxListed = 200;

    public bool Valid => FeatureFound && Errors.Count == 0;
}
