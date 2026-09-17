namespace CoaiBugs;

/// <summary>Who a request counts as, for the rate limiter — a key, never an address.</summary>
/// <remarks>
/// <para><b>Two kinds, and they never share a bucket</b>, whatever their ids: a contributor key on
/// <c>/ingest</c>, and an administrator on <c>/admin/*</c> by the id derived from the admin key's
/// hash. Story 2 builds the admin routes; this is the identity they must hand the limiter, defined
/// here so those routes cannot land in the contributor bucket by default.</para>
/// <para><b>A struct, keyed by kind and id</b>, because one is made on every request: the first
/// version was a record class holding a concatenated key — two allocations per request on the hot
/// path, which a code round called out. The dictionary hashes the two fields, and nothing is
/// allocated beyond the id string the request already holds.</para>
/// <para><b>Nothing here is an address, and must not be.</b> The vhost clears every forwarding
/// header, so Kestrel sees <c>127.0.0.1</c> for everybody; an address bucket would be ONE bucket for
/// the whole internet — one flooder locking everyone out — and the only way to fix it would be
/// <c>ForwardedHeaders</c>, which re-admits a spoofable, promise-breaking address. Unauthenticated
/// traffic is nginx's job, at the one layer that legitimately sees the address and keeps it in
/// memory rather than in a log.</para>
/// </remarks>
public readonly record struct LimiterSubject
{
    /// <summary>Which side of the boundary the id belongs to.</summary>
    public SubjectKind Kind { get; }

    /// <summary>The key id, or the derived administrator id.</summary>
    public string Id { get; }

    private LimiterSubject(SubjectKind kind, string id)
    {
        Kind = kind;
        Id = id;
    }

    /// <summary>A contributor key, by the id <see cref="Corpus.KeyFor"/> answered, on <c>/ingest</c>.</summary>
    public static LimiterSubject Contributor(string keyId) => new(SubjectKind.Contributor, keyId);

    /// <summary>
    /// An administrator, by the id derived from the presented admin key's hash, on <c>/admin/*</c>.
    /// </summary>
    /// <remarks>Story 2 calls this with <see cref="Corpus.HashOf"/> of the key it authenticated.</remarks>
    public static LimiterSubject Administrator(string keyHash) =>
        new(SubjectKind.Administrator, AdminId.Of(keyHash).Value);

    /// <summary>For display — <c>/admin/active</c>, a log line — never the dictionary key.</summary>
    public string Key => Kind == SubjectKind.Contributor ? "key:" + Id : Id;
}

/// <summary>The two kinds of caller the limiter tells apart.</summary>
public enum SubjectKind
{
    /// <summary>A contributor, by key id.</summary>
    Contributor,

    /// <summary>An administrator, by derived id.</summary>
    Administrator,
}
