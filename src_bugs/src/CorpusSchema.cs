namespace CoaiBugs;

/// <summary>
/// The shape of the corpus database, as ordered steps.
/// </summary>
/// <remarks>
/// <para>The same discipline as <c>coai-mcp</c>'s <c>Schema</c>, run by the same
/// <see cref="CoaiMcp.Storage.SqliteMigrator"/>: <c>user_version</c> records how many steps a file
/// has run, and each new step is appended — never merged into an old one, because
/// <c>CREATE TABLE IF NOT EXISTS</c> creates nothing when the table is already there.</para>
/// <para><b>Step 1 is frozen.</b> It is the statement <c>bugs-v0.1.0</c> shipped, byte for byte, and
/// it is what the deployed <c>coai-bugs.db</c> already contains at <c>user_version = 0</c>. Editing
/// it would change what a NEW file gets while changing nothing in the field — two shapes under one
/// number. <c>TheSchemaIsFrozenTests</c> holds it against a fixture copied from the released tree,
/// so the freeze is a red test rather than a comment; a fixture built from THIS constant would
/// pass whatever was done to it, which is the hole that test closes.</para>
/// <para>The SQL comment inside step 1 about "a counter WITHOUT a clock" is part of those bytes and
/// stays. It is history now: step 2's remark is the promise as it stands.</para>
/// </remarks>
internal static class CorpusSchema
{
    /// <summary>The steps, in the order every file runs them. Append; never reorder, never edit.</summary>
    internal static readonly string[] Steps = [Tables, WhoUsedItAndWhoAdministeredIt];

    /// <summary>Step 1 — the whole schema as released in <c>bugs-v0.1.0</c>. NEVER EDIT.</summary>
    internal const string Tables = """
        CREATE TABLE IF NOT EXISTS quarantine (
            entry_id        TEXT PRIMARY KEY,
            language        TEXT NOT NULL,
            skeleton_before TEXT NOT NULL,
            skeleton_after  TEXT NOT NULL,
            received_utc    TEXT NOT NULL,
            -- WHICH key sent it, never who holds the key. It is here so one contributor's mistake
            -- can be undone in bulk without touching anybody else's work.
            key_id          TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS corpus (
            entry_id        TEXT PRIMARY KEY,
            language        TEXT NOT NULL,
            skeleton_before TEXT NOT NULL,
            skeleton_after  TEXT NOT NULL,
            promoted_utc    TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS api_keys (
            id           TEXT PRIMARY KEY,
            key_hash     TEXT NOT NULL UNIQUE,
            created_utc  TEXT NOT NULL,
            revoked_utc  TEXT NOT NULL DEFAULT '',
            -- OUR record of why a key exists, not the holder's data. "for the tuesday workshop",
            -- never a name or an address.
            note         TEXT NOT NULL DEFAULT '',
            -- A counter WITHOUT a clock, deliberately. With timestamps it would be a record of when
            -- a person we handed a key to was working. It is not a rate limit either, and the plan
            -- said so after a reviewer pointed out that a lifetime count has no window and no reset.
            submissions  INTEGER NOT NULL DEFAULT 0
        );
        """;

    /// <summary>
    /// Step 2 — the month a key was last used, and what administrators did. Appended 2026-09-17.
    /// </summary>
    /// <remarks>
    /// <para><b><c>last_seen_month</c>, not <c>_utc</c> and not <c>_date</c>.</b> A UTC calendar
    /// month, <c>yyyy-MM</c>, written only when an ingest is ACCEPTED and only when it differs from
    /// what is there. It answers "is this key alive" — used this month, last month, not since March
    /// — and cannot answer which day anybody worked, which is the promise the operator chose to keep
    /// when offered the narrower option. Empty means never used, and a UI says "never", not a blank.</para>
    /// <para><b><c>admin_audit</c> records exact times about ADMINISTRATORS</b>: who issued or revoked
    /// a key, and when. That is a log about the people holding administrative power, not about the
    /// people contributing. <c>target</c> is a key id and <c>action</c> is a verb from a closed set;
    /// a test pins that nothing else — not a note, not a key, not a hash — may appear in either.
    /// Swept to the newest <see cref="Corpus.MostAudit"/> rows in the same transaction as every write
    /// that crosses the mark.</para>
    /// </remarks>
    internal const string WhoUsedItAndWhoAdministeredIt = """
        ALTER TABLE api_keys ADD COLUMN last_seen_month TEXT NOT NULL DEFAULT '';

        CREATE TABLE IF NOT EXISTS admin_audit (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            admin_id  TEXT NOT NULL,
            action    TEXT NOT NULL,
            target    TEXT NOT NULL,
            at_utc    TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS admin_audit_by_time ON admin_audit (at_utc);
        """;
}
