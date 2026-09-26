namespace CoaiMcp.Store;

/// <summary>
/// What the feature review asks of the gate's history: which work, in which repository, and whose
/// rejections are already counted.
/// </summary>
/// <param name="DataDir">The data directory whose <c>coai.db</c> is read — read-only, never created.</param>
/// <param name="RepoPath">The repository the work lives in; rounds of other repositories never belong.</param>
/// <param name="BaseSha">The resolved commit before the first epic (a full id).</param>
/// <param name="HeadSha">The resolved head the feature review reads (a full id).</param>
/// <param name="EpicBranches">
/// <c>epics[].branch</c> as the caller wrote them. A trunk name (<c>main</c>, <c>master</c>) and a
/// detached <c>HEAD</c> are dropped: every piece of work in a repository passes through them, so they
/// tie nothing to THIS work.
/// </param>
/// <param name="PlanText">The plan at head; its first <c># </c> heading is what rule (c) matches.</param>
/// <param name="ExcludeSessionId">
/// The feature review's own session key. Its rejections are the gate's current business — already
/// counted and discounted there — and attaching them again would show the reviewer its own answers.
/// </param>
public sealed record GateHistoryAsk(
    string DataDir,
    string RepoPath,
    string BaseSha,
    string HeadSha,
    IReadOnlyList<string> EpicBranches,
    string PlanText,
    string ExcludeSessionId);

/// <summary>Which rule tied a round or a consultation to the work — none of them a proof.</summary>
[Flags]
public enum Admission
{
    None = 0,

    /// <summary>Rule (b): it ran on a branch the caller named as one of the epics'.</summary>
    EpicBranch = 1,

    /// <summary>Rule (a): the commit it read is in <c>base..head</c>.</summary>
    CommitInRange = 2,

    /// <summary>Rule (c): a plan round whose subject or opening line is the plan's heading.</summary>
    PlanHeading = 4,
}

/// <summary>One round the history attached, and why.</summary>
public sealed record AttachedRound(
    long Id,
    string SessionId,
    string Branch,
    string Stage,
    int Number,
    string Subject,
    string StartedUtc,
    Admission Admission)
{
    /// <summary>The caller's own epic branch is the strongest association there is; it ranks first.</summary>
    public bool FromAnEpic => Admission.HasFlag(Admission.EpicBranch);
}

/// <summary>A rejected finding, with the reason the caller gave — de-duplicated across rounds.</summary>
/// <param name="Times">How many rejections this one stands for: the same remark rejected again counts here.</param>
public sealed record HistoryRejection(
    AttachedRound Round,
    string Severity,
    string Category,
    string File,
    int Line,
    string Title,
    string Reason,
    int Times = 1);

/// <summary>A consultation the history attached, and why.</summary>
public sealed record HistoryConsultation(
    string Kind,
    string Branch,
    string Status,
    string Outcome,
    string StartedUtc,
    string Problem,
    string Advice,
    Admission Admission)
{
    public bool FromAnEpic => Admission.HasFlag(Admission.EpicBranch);
}

/// <summary>
/// The gate's history of one piece of work: what was attached, what was not, and what could not be
/// asked.
/// </summary>
/// <param name="Unavailable">
/// Empty when the history answered; otherwise the ONE sentence the context carries instead — the
/// round continues without it.
/// </param>
/// <param name="Rounds">Every round attached, whatever it found.</param>
/// <param name="Rejections">The rejections of those rounds, de-duplicated, in the order they are shown.</param>
/// <param name="RejectionsFound">How many rejections there were before de-duplication.</param>
/// <param name="Consultations">The consultations attached, in the order they are shown.</param>
/// <param name="NotAttached">Rounds since the base commit that no rule tied to this work.</param>
/// <param name="Notes">What was switched off or could not be asked, each a sentence.</param>
public sealed record GateHistory(
    string Unavailable,
    IReadOnlyList<AttachedRound> Rounds,
    IReadOnlyList<HistoryRejection> Rejections,
    int RejectionsFound,
    IReadOnlyList<HistoryConsultation> Consultations,
    int NotAttached,
    IReadOnlyList<string> Notes)
{
    /// <summary>The history could not be read; <paramref name="why"/> is the sentence that says so.</summary>
    public static GateHistory Failed(string why) => new(why, [], [], 0, [], 0, []);
}
