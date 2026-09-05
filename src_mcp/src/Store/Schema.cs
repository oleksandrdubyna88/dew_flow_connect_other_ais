namespace CoaiMcp.Store;

/// <summary>
/// The shape of the rounds database, as SQL.
/// </summary>
/// <remarks>
/// <para>Every statement is <c>IF NOT EXISTS</c>, so opening an existing database is the same code
/// path as creating one. There is no migration framework and there deliberately is not going to be
/// one: this is a PROJECTION of the session files, so a schema that has to change is a file that
/// can be deleted and written again.</para>
/// <para>Times are ISO-8601 UTC strings, per the family's UTC rule — sortable and comparable as
/// text, converted for a reader only in the UI.</para>
/// </remarks>
internal static class Schema
{
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
}
