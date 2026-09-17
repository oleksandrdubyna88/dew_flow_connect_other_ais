namespace CoaiBugs;

/// <summary>
/// How an administrator is named — in the audit, and in the rate limiter for an admin route.
/// </summary>
/// <remarks>
/// A type rather than a string, and one that cannot be made from an arbitrary string outside this
/// assembly: the audit is an exact-time record about administrators, and the finding was that story
/// 2's API would otherwise be one call away from writing whatever a caller handed it into that
/// record. The two ways in are <see cref="Cli"/> and <see cref="Of"/>.
/// </remarks>
public sealed record AdminId
{
    /// <summary>The administrator behind a one-shot mode: somebody with a shell on the host.</summary>
    public static readonly AdminId Cli = new("cli");

    /// <summary>As stored in <c>admin_audit.admin_id</c>.</summary>
    public string Value { get; }

    private AdminId(string value) => Value = value;

    /// <summary>
    /// Derived from an admin key's hash: <c>admin-</c> and its first eight hex digits. Deterministic,
    /// no table.
    /// </summary>
    /// <remarks>
    /// Defined in story 1 because the rate limiter needs an identity for <c>/admin/*</c> that can
    /// never share a bucket with a contributor key. Story 2 supplies the hash of the presented admin
    /// key — <see cref="Corpus.HashOf"/>'s sixty-four hex characters; a shorter string is a
    /// programming error, not an input, and is refused as one.
    /// </remarks>
    public static AdminId Of(string keyHash)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keyHash.Length, 8, nameof(keyHash));

        return new("admin-" + keyHash[..8]);
    }

    /// <summary>An id read back from the audit, as this type wrote it.</summary>
    internal static AdminId Stored(string value) => new(value);
}
