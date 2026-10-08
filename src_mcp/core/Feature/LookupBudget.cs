namespace CoaiMcp.Core.Feature;

/// <summary>
/// How much coai's own list and search may serve an antigravity consultant in one turn
/// (todo/PLAN_agy_searches_through_coai.md §3) — modelled on <see cref="SourceBudget"/>, which bounds what a reviewer
/// is served from git; this bounds what is read from a WORKING TREE, which no git object caps.
/// </summary>
public static class LookupBudget
{
    /// <summary>Lookups one turn may ask for; the ninth and later are refused, naming the line.</summary>
    public const int RequestsPerTurn = SourceBudget.RequestsPerTurn;

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

    /// <summary>Bytes of results one turn may carry back to the model: 32 KB.</summary>
    public const int TurnBytes = 32 * 1024;

    /// <summary>Lookup turns one answer may take — the api rows' default follow-ups (<see cref="SourceBudget.DefaultFollowUps"/>).</summary>
    public const int FollowUps = SourceBudget.DefaultFollowUps;
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
    int TurnBytes)
{
    public static LookupLimits Default { get; } = new(
        LookupBudget.RequestsPerTurn, LookupBudget.ListEntries, LookupBudget.SearchHits, LookupBudget.LineChars,
        LookupBudget.FileBytes, LookupBudget.WalkFiles, LookupBudget.WalkTime, LookupBudget.TurnBytes);
}
