namespace CoaiBugs;

/// <summary>When a key was last used: never, or a month — and never anything finer.</summary>
/// <remarks>
/// A closed union rather than an empty-or-not string, because "never" and "a month" are different
/// facts a UI must show differently, and a reader that has to know the empty string means never is a
/// reader that will one day render a blank.
/// </remarks>
public abstract record LastSeen
{
    private LastSeen()
    {
    }

    /// <summary>The key has never made an accepted ingest. A UI says "never", not a blank.</summary>
    public sealed record Never : LastSeen;

    /// <summary>The month of its latest accepted ingest.</summary>
    public sealed record In(UtcMonth Month) : LastSeen;
}
