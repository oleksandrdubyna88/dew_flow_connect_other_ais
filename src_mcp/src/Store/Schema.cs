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
    /// <summary>
    /// The schema, as ordered steps. The file records how many it has had (<c>user_version</c>).
    /// </summary>
    /// <remarks>
    /// Append, never edit: step 0 is what every existing database already ran, and rewriting it
    /// would change nothing on disk while changing what a new file gets — two shapes with one
    /// version number, which is the failure this exists to prevent.
    /// </remarks>
    internal static readonly string[] Steps = [Tables, Search, Locators, SessionCommits];

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
    /// Addressable rounds: a locator reserved before anything runs, and the answer it earned.
    /// </summary>
    /// <remarks>
    /// <para><b>This table is not a projection.</b> Everything above it records what already
    /// happened and may be rebuilt from the session files; a row here is the ONLY record that a
    /// particular round was reserved, so losing it loses the round. The reservation tool therefore
    /// refuses when the database will not open, where the round tools carry on without it.</para>
    /// <para><b>Why the result is stored whole.</b> The findings table is keyed by the round's
    /// ordinal and is rewritten when a round is re-recorded; the pending list in the session file is
    /// the LAST round's findings and nothing else. Neither can answer "what did round X conclude"
    /// after round X+1 ran. <c>result_json</c> is the answer as it was sent, so a read-back and the
    /// original reply are the same document rather than two reconstructions that agree today.</para>
    /// </remarks>
    internal const string Locators = """
        CREATE TABLE IF NOT EXISTS round_locators (
            -- The locator. All three parts, always: a locator missing one names no round, and the
            -- primary key alone would not stop an empty string standing in for a part.
            provider_id   TEXT NOT NULL CHECK (length(provider_id) > 0),
            session_id    TEXT NOT NULL CHECK (length(session_id)  > 0),
            round_id      TEXT NOT NULL CHECK (length(round_id)    > 0),
            -- The caller's own idempotency key. Reserving twice with the same one returns the same
            -- locator instead of a second round, which is what makes a crashed caller's retry safe.
            client_token  TEXT NOT NULL CHECK (length(client_token) > 0),
            stage         TEXT NOT NULL,
            state         TEXT NOT NULL CHECK (state IN ('reserved','running','completed','failed')),
            repo_path     TEXT NOT NULL,
            branch        TEXT NOT NULL,
            -- The attestation, pinned at reservation and re-checked before the round runs.
            repo_identity TEXT NOT NULL,
            base_ref      TEXT NOT NULL,
            base_sha      TEXT NOT NULL,
            head_sha      TEXT NOT NULL,
            tree_sha      TEXT NOT NULL,
            subject_hash  TEXT NOT NULL,
            -- WHO is running it, written when the round is claimed for dispatch and cleared
            -- when it leaves `running`. Not a lease: it carries no expiry and is never
            -- compared against a clock. It says which machine and which process owned the
            -- round, so a server on ANOTHER machine can see that a running round is not its
            -- to release.
            owner_machine  TEXT NOT NULL DEFAULT '',
            owner_pid      INTEGER NOT NULL DEFAULT 0,
            owner_instance TEXT NOT NULL DEFAULT '',
            reserved_utc  TEXT NOT NULL,
            started_utc   TEXT NOT NULL DEFAULT '',
            completed_utc TEXT NOT NULL DEFAULT '',
            -- The local round this locator owns, once it has one, and the answer it produced.
            round_ref     INTEGER REFERENCES rounds(id),
            result_json   TEXT NOT NULL DEFAULT '',
            PRIMARY KEY (provider_id, session_id, round_id)
        );

        -- One caller token, one locator: the reservation is idempotent per session and token.
        CREATE UNIQUE INDEX IF NOT EXISTS locators_by_token
            ON round_locators (session_id, client_token);

        -- At most ONE round of a session may be running at a time. The session file is a
        -- read-modify-write document and the round ordinal comes from it, so two rounds in flight
        -- on one session race over the trail, the worktree path and the stage state. Held by the
        -- database rather than by a lock in one process, because two SERVERS share this directory.
        CREATE UNIQUE INDEX IF NOT EXISTS locators_one_running
            ON round_locators (session_id) WHERE state = 'running';

        -- One local round belongs to exactly one locator. Two locators pointing at one round would
        -- both answer with its verdict, which is the duplicate this whole table exists to prevent.
        CREATE UNIQUE INDEX IF NOT EXISTS locators_by_round
            ON round_locators (round_ref) WHERE round_ref IS NOT NULL;

        -- A completed round is finished with it. Re-completing must not move the locator to another
        -- round, rewrite its identity, or change what it attested — the fields the answer was
        -- checked against cannot be rewritten BY that answer. Enforced here rather than trusted to
        -- the one writer, because a second writer is exactly what this table is for.
        CREATE TRIGGER IF NOT EXISTS locators_are_immutable
        BEFORE UPDATE ON round_locators
        FOR EACH ROW WHEN
            old.provider_id  <> new.provider_id
            OR old.session_id  <> new.session_id
            OR old.round_id    <> new.round_id
            OR old.client_token <> new.client_token
            OR old.subject_hash <> new.subject_hash
            OR old.head_sha     <> new.head_sha
            OR old.base_sha     <> new.base_sha
            OR old.tree_sha     <> new.tree_sha
            OR (old.state = 'completed' AND new.state <> 'completed')
        BEGIN
            SELECT RAISE(ABORT, 'a locator and what it attested are fixed at reservation');
        END;
        """;

    /// <summary>
    /// The outbox: the session update a finished addressable round still owes its session file.
    /// </summary>
    /// <remarks>
    /// <para><b>Why an outbox rather than an ordered pair of writes.</b> A round's answer lives in
    /// SQLite and the round MACHINE lives in a JSON file, and nothing can commit to both at once.
    /// Ordering the two only chooses which half survives a crash between them: writing the session
    /// first can leave an answer nobody can read, and writing SQLite first leaves a session that
    /// never advanced — `resolve` sees no pending findings and the next round is refused. Both are
    /// wrong, and the second is what this table fixes.</para>
    /// <para>So the session update is DECIDED inside the same transaction that stores the answer and
    /// written down here as an intention: the exact document to write, and the hash the file must
    /// still have for that document to be the right one. Applying it afterwards is a separate,
    /// repeatable step — the normal path does it immediately, and `open` catches up whatever a crash
    /// left behind.</para>
    /// <para><b>Why the whole document and not a patch.</b> A patch has to be replayed against
    /// whatever it finds; a document plus the hash it expects can only be applied to the state it
    /// was computed from. Re-applying it is then a no-op rather than a second round in the trail,
    /// and a session that changed underneath is a conflict this can SEE rather than overwrite.</para>
    /// </remarks>
    internal const string SessionCommits = """
        CREATE TABLE IF NOT EXISTS session_commits (
            provider_id    TEXT NOT NULL,
            session_id     TEXT NOT NULL,
            round_id       TEXT NOT NULL,
            repo_path      TEXT NOT NULL,
            branch         TEXT NOT NULL,
            -- The session file's hash BEFORE the round. Empty means there was no file.
            expected_sha256 TEXT NOT NULL,
            -- The exact text to write, and its own hash — so an update already applied is
            -- recognised as applied rather than attempted again.
            session_json   TEXT NOT NULL,
            result_sha256  TEXT NOT NULL,
            applied        INTEGER NOT NULL DEFAULT 0 CHECK (applied IN (0,1)),
            -- Why it could not be applied, when it could not. A conflict is kept, never cleared by
            -- a retry: the operator has to know the session moved under a finished round.
            conflict       TEXT NOT NULL DEFAULT '',
            created_utc    TEXT NOT NULL,
            applied_utc    TEXT NOT NULL DEFAULT '',
            PRIMARY KEY (provider_id, session_id, round_id),
            FOREIGN KEY (provider_id, session_id, round_id)
                REFERENCES round_locators (provider_id, session_id, round_id)
        );

        -- What recovery reads: the ones still owed, and nothing else.
        CREATE INDEX IF NOT EXISTS session_commits_unapplied
            ON session_commits (repo_path, branch) WHERE applied = 0;
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
