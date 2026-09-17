namespace CoaiBugs;

/// <summary>What a key has done — or that there is no such key, which is a different fact from zero.</summary>
/// <remarks>
/// <c>UsageOf</c> answered zero and empty for a key that does not exist, and a code round was right
/// that absent is not zero: an operator reading "0 submissions, never used" about a mistyped id would
/// believe the key exists and is idle. The two are different cases of a closed union, so a caller
/// has to say what it does about each.
/// </remarks>
public abstract record Usage
{
    private Usage()
    {
    }

    /// <summary>No key has this id — issued under another, or never issued.</summary>
    public sealed record NoSuchKey : Usage;

    /// <summary>The key exists; this is what it has done.</summary>
    public sealed record Known(SubmissionCount Submissions, LastSeen LastSeen) : Usage;
}
