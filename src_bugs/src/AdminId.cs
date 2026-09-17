using System.Text.RegularExpressions;

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
public sealed partial record AdminId
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
    /// <para>Defined in story 1 because the rate limiter needs an identity for <c>/admin/*</c> that
    /// can never share a bucket with a contributor key. The admin API supplies the hash of the
    /// presented admin key — <see cref="Corpus.HashOf"/>'s sixty-four hex characters; a shorter
    /// string is a programming error, not an input, and is refused as one.</para>
    /// <para><b>It is a TRUNCATION, so a collision is silent.</b> Eight hex digits is 32 bits: two
    /// admin keys whose hashes share them would be one id in the audit and one bucket in the
    /// limiter, with nothing anywhere saying so. With a handful of administrators the probability is
    /// negligible, and it is written down rather than assumed because the audit's whole job is saying
    /// who did it. Lengthening the id is the fix if that ever stops being true; the id is derived and
    /// stored in `admin_audit.admin_id`, so changing it would rename past administrators in the
    /// trail, which is why it is not done pre-emptively.</para>
    /// </remarks>
    public static AdminId Of(string keyHash)
    {
        // The DOCUMENTED contract, enforced. It used to accept any string of eight characters or
        // more, so `Of("not-a-hash-at-all")` produced a perfectly plausible `admin-not-a-ha` and the
        // sixty-four-hex-character requirement lived only in this comment. Story 2 will call it with
        // whatever it authenticated, and a boundary that documents a shape without checking it is a
        // boundary that will one day be handed something else. (CodeRabbit, #348.)
        if (!Hash().IsMatch(keyHash))
        {
            throw new ArgumentException(
                "an administrator id is derived from Corpus.HashOf's sixty-four lowercase hex "
                + "characters; anything else is a programming error at this boundary, not an input",
                nameof(keyHash));
        }

        return new("admin-" + keyHash[..8]);
    }

    /// <summary>What <see cref="Corpus.HashOf"/> produces, which is the only thing <see cref="Of"/> takes.</summary>
    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Hash();

    /// <summary>An id read back from the audit, as this type wrote it.</summary>
    internal static AdminId Stored(string value) => new(value);
}
