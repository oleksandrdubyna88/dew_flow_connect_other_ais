namespace CoaiBugs;

/// <summary>
/// What an ingest may do INSIDE the transaction <see cref="Corpus.Accept"/> opened for it.
/// </summary>
/// <remarks>
/// <para><b>Explicit, where an ambient flag was.</b> <c>Keep</c> used to ask the corpus whether a
/// batch happened to be open and join it if so — a leaky abstraction that a code round called
/// blocking: it is true only while everything on that path is synchronous and on one thread, and it
/// stops being true silently the moment anything becomes async. The scope IS the transaction from the
/// callee's side. It exists only for the length of <c>take</c>, it writes for one key in one month,
/// and there is no way to hold one outside a batch.</para>
/// <para>What it offers is exactly what a batch needs and nothing more: the room left in quarantine,
/// and storing a pair. Everything else on <see cref="Corpus"/> — issuing, revoking, promoting — is not
/// something an ingest does.</para>
/// </remarks>
public sealed class IngestScope
{
    private readonly Corpus _corpus;
    private readonly KeyId _key;
    private readonly UtcMonth _month;

    internal IngestScope(Corpus corpus, KeyId key, UtcMonth month)
    {
        _corpus = corpus;
        _key = key;
        _month = month;
    }

    /// <summary>Stores a pair inside the batch, for the batch's key, stamped with the batch's month.</summary>
    public (Kept Kept, string EntryId) Keep(string language, string before, string after) =>
        _corpus.KeepInside(language, before, after, _key, _month);

    /// <summary>How many pairs are waiting, as this transaction sees them.</summary>
    public int WaitingCount() => _corpus.WaitingCount();
}
