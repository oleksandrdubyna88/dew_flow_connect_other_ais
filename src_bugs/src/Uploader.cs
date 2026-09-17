namespace CoaiBugs;

/// <summary>Who a <c>/ingest</c> request turned out to be — a contributor, an administrator, or nobody.</summary>
/// <remarks>
/// <para><b>Two credentials reach one endpoint, and they are not the same caller.</b> A contributor
/// holds a row in <c>api_keys</c>: counted, stamped with a month, revocable in another process
/// between the gate and the write. An administrator holds a line in <c>COAI_BUGS_ADMIN_KEYS</c>:
/// no row, no counter, no month, and immutable for the process's lifetime. They take different
/// paths through <see cref="Corpus"/> for those reasons — <see cref="Corpus.Accept"/> and
/// <see cref="Corpus.AcceptAdmin"/> — and a union is how the gate hands the endpoint the one fact
/// that decides which, instead of the endpoint guessing from a string's shape.</para>
/// <para><b><see cref="Nobody"/> exists so the gate needs no null.</b> "No usable credential" is an
/// answer, not an absence, and it is the answer a 401 is written for. The doctrine forbids a null in
/// business logic; a third case costs one line and makes the switch that follows exhaustive.</para>
/// <para><b>Each kind is limited by its own setting</b>, because the credential decides the bucket:
/// a contributor against <c>COAI_BUGS_RATE_PER_MINUTE</c> and an administrator against
/// <c>COAI_BUGS_ADMIN_RATE_PER_MINUTE</c>. The subjects carry different kinds, so the two can never
/// share a window however their ids are spelled.</para>
/// </remarks>
internal abstract record Uploader
{
    private Uploader()
    {
    }

    /// <summary>No configured credential matched. A revoked key and an unknown one both land here.</summary>
    /// <remarks>
    /// One case for both, so the 401 cannot be used to discover which keys are real — the same rule
    /// <see cref="Corpus.KeyFor"/> and <see cref="AdminKeys.Match"/> each keep on their own side.
    /// </remarks>
    public sealed record Nobody : Uploader;

    /// <summary>A contributor key in force at the gate. Counted and stamped when the batch commits.</summary>
    public sealed record Contributor(KeyId Key) : Uploader;

    /// <summary>A configured administrator. Stored and attributed, counted against no key.</summary>
    public sealed record Administrator(AdminId Id) : Uploader;

    /// <summary>What to call this caller in a message addressed to it.</summary>
    /// <remarks>
    /// A 429 says "at most N requests a minute per …", and the word matters more than it looks: the
    /// two limits are different environment variables, so telling an administrator the number is
    /// "per key" sends them to the contributor setting to change a limit that is not the one they
    /// reached. A test presented an admin credential at the admin limit and read the wrong word.
    /// </remarks>
    public string Noun => this switch
    {
        Contributor => "key",
        Administrator => "administrator",
        _ => throw new InvalidOperationException("nobody is told nothing; the answer is a bare 401"),
    };

    /// <summary>The limiter bucket this caller counts against.</summary>
    public LimiterSubject Subject => this switch
    {
        Contributor contributor => LimiterSubject.Contributor(contributor.Key),
        Administrator administrator => LimiterSubject.Administrator(administrator.Id),
        _ => throw new InvalidOperationException(
            "nobody has no limiter subject; the gate answers 401 before asking for one"),
    };
}
