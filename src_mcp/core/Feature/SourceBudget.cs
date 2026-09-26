namespace CoaiMcp.Core.Feature;

/// <summary>
/// How much source a feature reviewer may be served — every cap of the source resolver, in ONE place.
/// </summary>
/// <remarks>
/// <para><b>Corrected by the feature-pack trial</b> (2026-09-26, 21 real features of this repository;
/// <c>research/RESULTS_feature_pack_trial.md</c>). The plan's first figures (§4.9) were 48 KB a turn
/// and 128 KB a reviewer; in the trial the 48 KB per-turn budget refused more requests than anything
/// else, so a turn is <see cref="TurnBytes">64 KB</see> now, a reviewer's total stays
/// <see cref="ReviewerBytes">128 KB</see> — two full turns — and <see cref="RequestsPerTurn">eight</see>
/// requests a turn is the ceiling the plan named. The same trial found reviewers asking for symbols by
/// their QUALIFIED names and being refused; that correction is <see cref="SymbolLookup"/>.</para>
/// <para>Every consumer reads these constants; no call site keeps its own number, because a guard
/// and a message that disagree is a support ticket (platform-limits.md).</para>
/// </remarks>
public static class SourceBudget
{
    /// <summary>Requests served in one turn; the ninth and later are refused, naming the file.</summary>
    public const int RequestsPerTurn = 8;

    /// <summary>Bytes of served source one turn may carry: 64 KB (the trial's correction of 48 KB).</summary>
    public const int TurnBytes = 64 * 1024;

    /// <summary>Bytes of served source one reviewer may receive over all its turns: 128 KB.</summary>
    public const int ReviewerBytes = 128 * 1024;

    /// <summary>The most lines one slice carries — a symbol or a span longer than this is cut, and the cut is named.</summary>
    public const int MaxLines = 400;

    /// <summary>A file at most this large is served whole when neither a symbol nor lines were asked; a bigger one serves its head.</summary>
    public const int WholeFileBytes = 16 * 1024;

    /// <summary>How many declarations of one name are served when a symbol has several — the rest are counted, not sent.</summary>
    public const int MaxOverloads = 3;

    /// <summary>How many of a file's declared names an unknown-symbol refusal lists.</summary>
    public const int NamesInARefusal = 20;

    /// <summary>How far into a file a NUL is looked for before the file is called binary — git's own heuristic window.</summary>
    public const int BinarySniffChars = 8000;
}
