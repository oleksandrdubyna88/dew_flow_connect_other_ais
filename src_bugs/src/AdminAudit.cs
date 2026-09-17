using System.Globalization;

namespace CoaiBugs;

/// <summary>What an administrator did — the closed set of verbs <c>admin_audit.action</c> may hold.</summary>
/// <remarks>Stored lowercase, by <see cref="AuditActions.Word"/>; a test pins that nothing else appears.</remarks>
public enum AuditAction
{
    /// <summary>A key was minted.</summary>
    Issue,

    /// <summary>A key was ended.</summary>
    Revoke,
}

/// <summary>The one spelling of each action, so the column holds words and never enum numbers.</summary>
public static class AuditActions
{
    public static string Word(this AuditAction action) => action switch
    {
        AuditAction.Issue => "issue",
        AuditAction.Revoke => "revoke",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "is not an audited action"),
    };
}

/// <summary>Who did an administrative thing, and when — what an audit row adds to a mutation.</summary>
/// <remarks>
/// <para><b>Exact times, about administrators.</b> <c>AtUtc</c> is an ISO-8601 instant, and that is
/// deliberate: this is a log about the people holding administrative power, not about the people
/// contributing. A contributor is never named here, because the server never holds a contributor's
/// name — only key ids — and the row's <c>target</c> is a key id.</para>
/// <para><c>AdminId</c> is <see cref="AdminIdentity.Cli"/> for a one-shot run on the box, and the
/// derived id of an admin key (<see cref="AdminIdentity.Of"/>) once the admin API exists.</para>
/// </remarks>
public sealed record Audit(string AdminId, string AtUtc)
{
    /// <summary>An audit stamped now, from the injected clock.</summary>
    public static Audit By(string adminId, TimeProvider clock) =>
        new(adminId, clock.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
}

/// <summary>
/// How an administrator is named — in the audit, and in the rate limiter for an admin route.
/// </summary>
public static class AdminIdentity
{
    /// <summary>The administrator behind a one-shot mode: somebody with a shell on the host.</summary>
    public const string Cli = "cli";

    /// <summary>
    /// Derived from an admin key's hash: <c>admin-</c> and its first eight hex digits. Deterministic,
    /// no table.
    /// </summary>
    /// <remarks>
    /// Defined here, in story 1, because the rate limiter needs an identity for <c>/admin/*</c> that
    /// can never share a bucket with a contributor key. Story 2 supplies the hash of the presented
    /// admin key; this is the spelling it must use, and <see cref="LimiterSubject.Administrator"/>
    /// is where it goes. The hash is <see cref="Corpus.HashOf"/>'s sixty-four hex characters; a
    /// shorter string is a programming error, not an input, and is refused as one.
    /// </remarks>
    public static string Of(string keyHash)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keyHash.Length, 8, nameof(keyHash));

        return "admin-" + keyHash[..8];
    }
}
