namespace CoaiMcp.Core.Feature;

/// <summary>
/// How much coai's own list and search may serve an antigravity consultant in one turn
/// (todo/PLAN_agy_searches_through_coai.md §3) — modelled on <see cref="SourceBudget"/>, which bounds what a reviewer
/// is served from git; this bounds what is read from a WORKING TREE, which no git object caps.
/// </summary>
public static class LookupBudget
{
    /// <summary>Lookups one turn may ask for; the ninth and later are refused, naming the line.</summary>
    /// <remarks>Its own number, not <see cref="SourceBudget.RequestsPerTurn"/>'s: the two budgets bound different reads,
    /// and a change to what a reviewer is served must not silently widen what a consultant may walk (code round, codex).</remarks>
    public const int RequestsPerTurn = 8;

    /// <summary>Entries one <c>list</c> shows; past it the result says so and asks for a narrower folder.</summary>
    public const int ListEntries = 200;

    /// <summary>Hits one <c>search</c> shows.</summary>
    public const int SearchHits = 50;

    /// <summary>Characters of one matching line shown; a longer line is cut, and the cut is marked.</summary>
    public const int LineChars = 300;

    /// <summary>A file larger than this is not searched — counted and said, never silently skipped.</summary>
    public const long FileBytes = 1024 * 1024;

    /// <summary>Files one <c>search</c> walks before it stops — so a question over a whole disk of projects ends.</summary>
    public const int WalkFiles = 20_000;

    /// <summary>How long one <c>search</c> may walk before it stops with what it found.</summary>
    public static readonly TimeSpan WalkTime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long one block may take in all: past it the requests not yet started are not served, saying so — eight slow
    /// searches would otherwise hold the caller for eighty seconds before agy hears anything (code round, codex).
    /// </summary>
    public static readonly TimeSpan BlockTime = TimeSpan.FromSeconds(20);

    /// <summary>Bytes of results one turn may carry back to the model: 32 KB.</summary>
    public const int TurnBytes = 32 * 1024;

    /// <summary>Lookup turns one answer may take — the plan's three, its own number (code round, codex).</summary>
    public const int FollowUps = 3;
}

/// <summary>The caps one lookup runs under — <see cref="Default"/> in the product; a test narrows one to watch it bite.</summary>
public sealed record LookupLimits(
    int RequestsPerTurn,
    int ListEntries,
    int SearchHits,
    int LineChars,
    long FileBytes,
    int WalkFiles,
    TimeSpan WalkTime,
    int TurnBytes,
    TimeSpan BlockTime)
{
    public static LookupLimits Default { get; } = new(
        LookupBudget.RequestsPerTurn, LookupBudget.ListEntries, LookupBudget.SearchHits, LookupBudget.LineChars,
        LookupBudget.FileBytes, LookupBudget.WalkFiles, LookupBudget.WalkTime, LookupBudget.TurnBytes, LookupBudget.BlockTime);
}
