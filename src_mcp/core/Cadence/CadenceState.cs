namespace CoaiMcp.Core.Cadence;

/// <summary>An epic whose code gate has passed — which a checkpoint does as well as a last round.</summary>
/// <param name="Number">The epic's number as the plan writes it.</param>
/// <param name="Verdict">The verdict that moved its code stage to done: proceed, good_enough or continue_anyway.</param>
/// <param name="ClosedUtc">When, ISO 8601.</param>
public sealed record ClosedEpic(int Number, string Verdict, string ClosedUtc);

/// <summary>An epic or a story the caller named as the place where being wrong is expensive.</summary>
/// <param name="Epic">The epic it lives in.</param>
/// <param name="Story">The story, as the plan numbers it (<c>7.2</c>), or empty for the whole epic.</param>
/// <param name="Reason">Why the caller named it — shown to the consultant and kept in the log.</param>
public sealed record RiskItem(int Epic, string Story, string Reason)
{
    /// <summary>How <c>consult</c>'s <c>epics</c> argument names it: <c>7/7.2</c>, or <c>7</c>.</summary>
    public string Key => Story.Length > 0 ? $"{Epic}/{Story}" : $"{Epic}";
}

/// <summary>
/// What one plan's cadence has recorded: the epics through the code gate, and the risk answer.
/// </summary>
/// <remarks>
/// <para>Keyed by repository and plan FILE NAME, never by session: with one gate for the whole task a session
/// carries several epics, and with one per epic every epic is its own session on its own branch.</para>
/// <para>Which groups and items are SATISFIED is not stored here — a consultation record, closed with an
/// outcome, is the only evidence of that, and a second copy would be one that can disagree.</para>
/// </remarks>
public sealed record CadenceState
{
    /// <summary>The plan as the caller last named it, repo-relative.</summary>
    public string Plan { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>The epics through the code gate, in number order.</summary>
    public IReadOnlyList<ClosedEpic> Closed { get => field ?? []; init; } = [];

    /// <summary>The epics and stories the caller named as risky.</summary>
    public IReadOnlyList<RiskItem> RiskItems { get => field ?? []; init; } = [];

    /// <summary>When the risk question was answered; empty until it is.</summary>
    public string RiskAnsweredUtc { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>The caller's reason when it named nothing.</summary>
    public string RiskNote { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>Whether the risk question has been answered, with items or with a reason for none.</summary>
    public bool RiskAnswered => RiskAnsweredUtc.Length > 0;

    /// <summary>Whether an epic's code gate has passed.</summary>
    public bool IsClosed(int epic) => Closed.Any(closed => closed.Number == epic);

    /// <summary>
    /// This record with one more epic through the code gate — idempotent, and the first verdict kept.
    /// </summary>
    /// <remarks>
    /// Called on every <c>review_code</c> and <c>status</c> to reconcile a close whose write failed after the
    /// session had already moved (the epic-1-3 consultation, point 5), so a second call must change nothing.
    /// </remarks>
    public CadenceState WithClosed(int epic, string verdict, string closedUtc) =>
        IsClosed(epic)
            ? this
            : this with { Closed = [.. Closed.Append(new ClosedEpic(epic, verdict, closedUtc)).OrderBy(c => c.Number)] };

    /// <summary>This record with the risk question answered.</summary>
    public CadenceState WithRisk(IReadOnlyList<RiskItem> items, string note, string answeredUtc) =>
        this with { RiskItems = items, RiskNote = note, RiskAnsweredUtc = answeredUtc };
}
