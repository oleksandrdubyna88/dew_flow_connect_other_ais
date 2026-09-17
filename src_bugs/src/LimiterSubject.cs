namespace CoaiBugs;

/// <summary>Who a request counts as, for the rate limiter — a key, never an address.</summary>
/// <remarks>
/// <para><b>Two kinds, two prefixes</b>, so an administrator and a contributor can never share a
/// bucket even if their ids collided: <c>key:</c> for a contributor key on <c>/ingest</c>, and the
/// derived <c>admin-</c> id for an administrator on <c>/admin/*</c>. Story 2 builds the admin routes;
/// this is the identity they must hand the limiter, defined here so those routes cannot land in the
/// contributor bucket by default.</para>
/// <para><b>Nothing here is an address, and must not be.</b> The vhost clears every forwarding
/// header, so Kestrel sees <c>127.0.0.1</c> for everybody; an address bucket would be ONE bucket for
/// the whole internet — one flooder locking everyone out — and the only way to fix it would be
/// <c>ForwardedHeaders</c>, which re-admits a spoofable, promise-breaking address. Unauthenticated
/// traffic is nginx's job, at the one layer that legitimately sees the address and keeps it in
/// memory rather than in a log.</para>
/// </remarks>
public sealed record LimiterSubject
{
    /// <summary>The dictionary key: the kind's prefix and the id.</summary>
    public string Key { get; }

    private LimiterSubject(string key) => Key = key;

    /// <summary>A contributor key, by the id <see cref="Corpus.KeyFor"/> answered, on <c>/ingest</c>.</summary>
    public static LimiterSubject Contributor(string keyId) => new("key:" + keyId);

    /// <summary>
    /// An administrator, by the id derived from the presented admin key's hash, on <c>/admin/*</c>.
    /// </summary>
    /// <remarks>Story 2 calls this with <see cref="Corpus.HashOf"/> of the key it authenticated.</remarks>
    public static LimiterSubject Administrator(string keyHash) => new(AdminIdentity.Of(keyHash));
}
