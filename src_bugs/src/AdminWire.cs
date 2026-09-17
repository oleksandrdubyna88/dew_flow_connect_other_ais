namespace CoaiBugs;

/// <summary>
/// What the admin API puts on the wire. Every one is an OBJECT, never a bare array.
/// </summary>
/// <remarks>
/// <para>A top-level array cannot gain a field later without breaking every reader, and it gives a
/// page nowhere to say whether there is another one. So even a listing is an object with its items
/// inside it.</para>
/// <para><b>Only <see cref="Issued"/> carries a key, and no shape here carries a hash.</b> That is
/// the type system enforcing the rule rather than a reviewer checking it: a response cannot leak a
/// credential it has no field for. <see cref="KeyListed"/> deliberately has no `key`.</para>
/// <para><b>Paging is keyset.</b> `NextBefore` is present only when another page exists, so it
/// doubles as "there is more" and a client pages until it is absent. There is no `skip`, because an
/// offset is not insert-stable — issuing a key between two requests shifts every boundary after it.
/// `Total` appears on the keys page only: `api_keys` is tens of rows so counting is free, while the
/// audit is 50 000 and counting it on every page would scan the table while holding the corpus gate.
/// </para>
/// </remarks>
internal static class AdminWire
{
    /// <summary>One key, as the tab lists it. No key value, no hash — see the remarks.</summary>
    /// <param name="Id">What a person reads and what the audit records.</param>
    /// <param name="Note">OUR record of why it exists, never the holder's identity.</param>
    /// <param name="CreatedUtc">An exact time: an administrator's action, not a contributor's.</param>
    /// <param name="RevokedUtc">Null while the key is in force.</param>
    /// <param name="LastSeenMonth">`yyyy-MM`, or null for never used — never a blank string.</param>
    /// <param name="Sent">Accepted ingests, lifetime. A counter with no clock.</param>
    /// <param name="Waiting">Its pairs still in quarantine.</param>
    internal sealed record KeyListed(
        string Id,
        string Note,
        string CreatedUtc,
        string? RevokedUtc,
        string? LastSeenMonth,
        int Sent,
        int Waiting);

    /// <summary>A page of keys, newest first.</summary>
    internal sealed record KeysPage(
        IReadOnlyList<KeyListed> Items,
        int Limit,
        int Total,
        long? NextBefore);

    /// <summary>The ONE response in this server that carries a key, and it carries it once.</summary>
    /// <remarks>
    /// It is never retrievable afterwards. If this response is lost in flight the key exists as a row
    /// and the recovery is the listing: it is newest-first with `createdUtc`, so an administrator
    /// sees a key created moments ago that they do not hold, and revokes it. That is why an
    /// idempotency token was not needed, and it only works because the listing is ordered that way.
    /// </remarks>
    internal sealed record Issued(string Id, string Key, string Note, string CreatedUtc);

    /// <summary>What revoking came to. Idempotent: a second press is a 200 with `changed: false`.</summary>
    /// <param name="RevokedUtc">
    /// The time of the ORIGINAL revocation when nothing changed — the fact being asked for, not the
    /// time of this attempt.
    /// </param>
    internal sealed record Revocation(string Id, string RevokedUtc, bool Changed);

    /// <summary>One administrative action. Exact times, because this is a log about administrators.</summary>
    internal sealed record AuditListed(
        long Id,
        string AdminId,
        string Action,
        string Target,
        string AtUtc);

    /// <summary>A page of the audit, newest first. No `total`; see the remarks.</summary>
    internal sealed record AuditPage(IReadOnlyList<AuditListed> Items, int Limit, long? NextBefore);

    /// <summary>One caller sending inside the current window.</summary>
    /// <param name="Id">The limiter subject, prefix intact: `key:…` or `admin-…`.</param>
    /// <param name="InWindow">Requests in the last minute.</param>
    /// <param name="Limited">Whether it is at or over its limit right now.</param>
    internal sealed record ActiveCaller(string Id, int InWindow, bool Limited);

    /// <summary>Who is sending right now. Persists nothing; the shape never varies when empty.</summary>
    internal sealed record ActiveNow(IReadOnlyList<ActiveCaller> Items, int WindowSeconds);

    /// <summary>What a new key is asked for with.</summary>
    internal sealed record IssueRequest(string? Note);
}
