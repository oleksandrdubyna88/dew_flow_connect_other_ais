namespace CoaiBugs;

/// <summary>
/// What an accepted ingest came to — or that the key was no longer in force when the write began.
/// </summary>
/// <remarks>
/// The gate checks the key and the write happens a moment later, and a <c>--revoke</c> can commit in
/// that moment. <see cref="Corpus.Accept"/> checks again inside its transaction, and this is how it
/// says which way it went: a caller has to decide what a 401-after-the-gate looks like rather than
/// finding out from a row that was written for a dead key.
/// </remarks>
public abstract record Accepted<T>
{
    private Accepted()
    {
    }

    /// <summary>The batch was judged, its pairs stored, the key counted and stamped — one commit.</summary>
    public sealed record Stored(T Answer) : Accepted<T>;

    /// <summary>Revoked between the gate and the write. Nothing was written, nothing counted.</summary>
    public sealed record KeyNotInForce : Accepted<T>;
}
