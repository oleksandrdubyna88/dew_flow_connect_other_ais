namespace CoaiMcp.Store;

/// <summary>
/// The shape of the rounds database, as SQL.
/// </summary>
/// <remarks>
/// <para>An ordered list of steps and a version number on the file — not a framework, and
/// deliberately not going to become one. This is a PROJECTION of the session files, so a change
/// that cannot be expressed as one additive step is a reason to delete the file and write it again
/// rather than to acquire a migration engine.</para>
/// <para>The creating statements are <c>IF NOT EXISTS</c> so a half-written file heals, but that is
/// NOT what makes an existing database current: <c>CREATE TABLE IF NOT EXISTS</c> creates nothing
/// when the table is there, so a column added later arrives as its own <c>ALTER</c> step.</para>
/// <para>Times are ISO-8601 UTC strings, per the family's UTC rule — sortable and comparable as
/// text, converted for a reader only in the UI.</para>
/// </remarks>
internal static class Schema
{
    /// <summary>
    /// The schema, as ordered steps. The file records how many it has had (<c>user_version</c>).
    /// </summary>
    /// <remarks>
    /// Append, never edit: step 0 is what every existing database already ran, and rewriting it
    /// would change nothing on disk while changing what a new file gets — two shapes with one
    /// version number, which is the failure this exists to prevent. A step that ADDS a column
    /// therefore goes at the end as its own <c>ALTER</c>, never into <see cref="Tables"/>.
    /// </remarks>
    internal static readonly string[] Steps = [Tables, Search, WhoCalled];

    internal const string Tables = """
        CREATE TABLE IF NOT EXISTS sessions (
            id          TEXT PRIMARY KEY,
            repo_path   TEXT NOT NULL,
            branch      TEXT NOT NULL,
            opened_utc  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS rounds (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id    TEXT NOT NULL REFERENCES sessions(id),
            stage         TEXT NOT NULL,
            number        INTEGER NOT NULL,
            subject       TEXT NOT NULL DEFAULT '',
            status        TEXT NOT NULL,
            verdict       TEXT NOT NULL,
            gating        INTEGER NOT NULL DEFAULT 0,
            started_utc   TEXT NOT NULL,
            completed_utc TEXT NOT NULL,
            tokens_in     INTEGER NOT NULL DEFAULT 0,
            tokens_out    INTEGER NOT NULL DEFAULT 0,
            cost_usd      REAL,
            -- What the caller SAID this change was for, and the commit the reviewers actually read.
            -- Without them a finding cannot be read back against the thing it was about.
            plan_text     TEXT NOT NULL DEFAULT '',
            head_sha      TEXT NOT NULL DEFAULT '',
            caller        TEXT NOT NULL DEFAULT '',
            -- How the caller closed the gate. Filled by `resolve`; -1 means it never did.
            accepted      INTEGER NOT NULL DEFAULT -1,
            rejected      INTEGER NOT NULL DEFAULT -1,
            -- What the calling agent was DOING in the stretch this round closes: its own transcript
            -- between the previous round and this one, trimmed. JSON, and empty when there is none.
            agent_log     TEXT NOT NULL DEFAULT '',
            UNIQUE (session_id, stage, number)
        );

        CREATE TABLE IF NOT EXISTS reviewers (
            round_id  INTEGER NOT NULL REFERENCES rounds(id),
            provider  TEXT NOT NULL,
            role      TEXT NOT NULL,
            status    TEXT NOT NULL,
            findings  INTEGER NOT NULL DEFAULT 0,
            seconds   REAL NOT NULL DEFAULT 0,
            note      TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS findings (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            round_id     INTEGER NOT NULL REFERENCES rounds(id),
            -- The number `resolve` calls it by. It is how a decision made in a later call finds
            -- the finding it was about, so it is stored rather than derived from the row order.
            ordinal      INTEGER NOT NULL,
            severity     TEXT NOT NULL DEFAULT '',
            category     TEXT NOT NULL DEFAULT '',
            file         TEXT NOT NULL DEFAULT '',
            line         INTEGER NOT NULL DEFAULT 0,
            title        TEXT NOT NULL DEFAULT '',
            why          TEXT NOT NULL DEFAULT '',
            fix          TEXT NOT NULL DEFAULT '',
            role         TEXT NOT NULL DEFAULT '',
            is_gating    INTEGER NOT NULL DEFAULT 0,
            providers    TEXT NOT NULL DEFAULT '',
            -- Empty until the caller decides: 'accept' or 'reject', with the reason a rejection
            -- must carry. An unresolved finding is a real state, not a missing value.
            resolution   TEXT NOT NULL DEFAULT '',
            reason       TEXT NOT NULL DEFAULT '',
            resolved_utc TEXT NOT NULL DEFAULT '',
            -- The finding repeats one the caller already rejected, and a reviewer raised it anyway.
            -- The gate discounts these; recorded because a rejection that keeps coming back is a
            -- different thing from a first disagreement, and the more interesting one.
            re_raised    INTEGER NOT NULL DEFAULT 0,
            UNIQUE (round_id, ordinal)
        );

        CREATE INDEX IF NOT EXISTS rounds_by_time   ON rounds (started_utc DESC);
        CREATE INDEX IF NOT EXISTS findings_by_round ON findings (round_id);
        -- The defended list reads this and nothing else, over the whole table, on every refresh.
        CREATE INDEX IF NOT EXISTS findings_re_raised ON findings (re_raised, resolution);
        """;

    /// <summary>
    /// Full-text search over what a finding SAYS, kept in step by triggers.
    /// </summary>
    /// <remarks>
    /// An external-content table: the text lives once, in `findings`, and FTS5 keeps only its index.
    /// The triggers are the price of that and are the documented shape for it — miss one and the
    /// index silently drifts from the table, which is worse than having no search.
    /// </remarks>
    internal const string Search = """
        CREATE VIRTUAL TABLE IF NOT EXISTS findings_fts USING fts5 (
            title, why, fix, file, content='findings', content_rowid='id'
        );

        CREATE TRIGGER IF NOT EXISTS findings_ai AFTER INSERT ON findings BEGIN
            INSERT INTO findings_fts (rowid, title, why, fix, file)
            VALUES (new.id, new.title, new.why, new.fix, new.file);
        END;

        CREATE TRIGGER IF NOT EXISTS findings_ad AFTER DELETE ON findings BEGIN
            INSERT INTO findings_fts (findings_fts, rowid, title, why, fix, file)
            VALUES ('delete', old.id, old.title, old.why, old.fix, old.file);
        END;

        CREATE TRIGGER IF NOT EXISTS findings_au AFTER UPDATE ON findings BEGIN
            INSERT INTO findings_fts (findings_fts, rowid, title, why, fix, file)
            VALUES ('delete', old.id, old.title, old.why, old.fix, old.file);
            INSERT INTO findings_fts (rowid, title, why, fix, file)
            VALUES (new.id, new.title, new.why, new.fix, new.file);
        END;
        """;

    /// <summary>
    /// Which AI called the round, and which model it said it was running (issue #174).
    /// </summary>
    /// <remarks>
    /// <para>An <c>ALTER</c> rather than four more lines in <see cref="Tables"/>, and that is the
    /// whole point of the step list: <c>CREATE TABLE IF NOT EXISTS</c> creates nothing when the
    /// table is already there, so a column added to it would be missing for ever on every file an
    /// older build made — and the writer is best-effort, so it would swallow the error every round
    /// rather than report it once.</para>
    /// <para><c>caller</c> already here is the calling agent's SESSION id. These say who that agent
    /// is. Every one defaults to empty, which is what is true of a round recorded before the
    /// columns existed: nobody asked it, so it never answered.</para>
    /// </remarks>
    internal const string WhoCalled = """
        ALTER TABLE rounds ADD COLUMN caller_vendor         TEXT NOT NULL DEFAULT '';
        ALTER TABLE rounds ADD COLUMN caller_client         TEXT NOT NULL DEFAULT '';
        ALTER TABLE rounds ADD COLUMN caller_client_version TEXT NOT NULL DEFAULT '';
        ALTER TABLE rounds ADD COLUMN caller_model          TEXT NOT NULL DEFAULT '';
        """;
}
