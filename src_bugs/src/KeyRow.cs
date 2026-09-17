namespace CoaiBugs;

/// <summary>One key as the Users tab sees it — what it is for, and what it has done.</summary>
/// <remarks>
/// <para><b>No key and no hash.</b> Neither is a field here, and that is the type doing the work
/// rather than a reviewer noticing: a listing cannot leak a credential it has no room for. The only
/// response in this server that ever carries a key is the single successful issuance, which builds
/// its own shape.</para>
/// <para><b><see cref="Cursor"/> is the paging handle, not an identity.</b> It is the row's `rowid`
/// and it is never shown to a person — `Id` is what a person reads. It exists so the next page can
/// be asked for by "the row before this one" instead of by an offset that shifts when a key is
/// issued between two requests.</para>
/// <para><see cref="Revoked"/> is null for a key in force, which is the one place in this file a
/// null is the honest answer: "not revoked" is an absence, and the alternative — a sentinel time —
/// is how a revoked-at of 0001-01-01 ends up rendered to somebody.</para>
/// </remarks>
/// <param name="Cursor">The row's paging handle; see the remarks.</param>
/// <param name="Id">The key id, which is what a person reads and what the audit records.</param>
/// <param name="Note">OUR record of why the key exists — never the holder's identity.</param>
/// <param name="Created">When it was issued. An exact time, about an administrator's action.</param>
/// <param name="Revoked">When it was stopped, or null while it is in force.</param>
/// <param name="Sent">Accepted ingests, lifetime. A counter, with no clock.</param>
/// <param name="LastSeen">The month of its latest accepted ingest, or never.</param>
/// <param name="Waiting">Its pairs still in quarantine — the second of the tab's two counts.</param>
public sealed record KeyRow(
    long Cursor,
    KeyId Id,
    string Note,
    UtcInstant Created,
    UtcInstant? Revoked,
    SubmissionCount Sent,
    LastSeen LastSeen,
    int Waiting);
