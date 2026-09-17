namespace CoaiBugs;

/// <summary>
/// The batch, while it is open: what a caller may do inside one accepted ingest and nowhere else.
/// </summary>
/// <remarks>
/// <para>It carries the key and the month so a caller cannot store a pair under a different key than
/// the one the batch was authenticated for — the values come from <see cref="Corpus.Accept"/>, not
/// from the caller.</para>
/// <para><b>It is a LEASE, and it expires when the batch commits.</b> The code round found that a
/// caller could keep this object, let <c>Accept</c> return, revoke the key, and then call
/// <see cref="Keep"/>: the write would land on the connection with no transaction around it, no
/// re-check of the key and no counter — quarantine rows for a revoked key that were never part of an
/// accepted ingest. A captured reference is a capability, and nothing was revoking it. So
/// <c>Accept</c> spends the scope on its way out, whichever way it leaves, and every operation here
/// refuses afterwards rather than writing outside the transaction it was made for. (codex.)</para>
/// </remarks>
public sealed class IngestScope
{
    private readonly Corpus _corpus;
    private readonly KeyId _key;
    private readonly UtcMonth _month;
    private bool _spent;

    internal IngestScope(Corpus corpus, KeyId key, UtcMonth month)
    {
        _corpus = corpus;
        _key = key;
        _month = month;
    }

    /// <summary>Ends the lease. Called by <see cref="Corpus.Accept"/> however the batch finishes.</summary>
    internal void Spend() => _spent = true;

    /// <summary>Stores a pair inside the batch, for the batch's key, stamped with the batch's month.</summary>
    public (Kept Kept, string EntryId) Keep(string language, string before, string after)
    {
        MustBeOpen();

        return _corpus.KeepInside(language, before, after, _key, _month);
    }

    /// <summary>How many pairs are waiting, as this transaction sees them.</summary>
    public int WaitingCount()
    {
        MustBeOpen();

        return _corpus.WaitingCount();
    }

    private void MustBeOpen()
    {
        if (_spent)
        {
            throw new InvalidOperationException(
                "this ingest batch has already committed; its scope cannot be used afterwards. A "
                + "write through a spent scope would have no transaction around it, no re-check of "
                + "the key and no counter — hold the scope only for the body of the callback.");
        }
    }
}
