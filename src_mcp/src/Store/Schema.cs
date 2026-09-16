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
    // Consultations is LAST because main's WhoCalled shipped first: a file migrated by that build
    // already records three steps, so inserting ahead of it would leave those databases without the
    // consultations table while believing they had run every step.
    internal static readonly string[] Steps =
    [
        Tables, Search, WhoCalled, Consultations, WhatItWasAgainst, TheCollectorsState,
        TheRunsThemselves, ThePairsThemselves, WhatWasSent,
    ];

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
    /// What the consultant was asked, and what it cost. Step 2, appended 2026-09-13.
    /// </summary>
    /// <remarks>
    /// <para><b>A table of its own rather than a kind of round.</b> A consultation is keyed by the
    /// CALLER and has no session, no stage and no findings — half the moments that trigger one happen
    /// before <c>open</c> exists. Filing it as a round would have meant a row of empty columns and a
    /// stage nobody could name.</para>
    /// <para>One row per CONSULTATION, not per turn, upserted as it advances: the log lists what was
    /// asked and how it ended, and the turn-by-turn detail lives in the record file the panel reads
    /// while it is running. <c>problem</c> is the FIRST turn's — what the consultation is about —
    /// and <c>advice</c> the LAST one's, which is the answer in force.</para>
    /// <para><c>rounds.consult_missed</c> is story 6's counter and is added here, with the table,
    /// because a schema step is append-only and splitting one column across two steps buys nothing.
    /// <c>-1</c> means the projection could not answer, the same convention
    /// <c>accepted</c>/<c>rejected</c> already use.</para>
    /// </remarks>
    internal const string Consultations = """
        CREATE TABLE IF NOT EXISTS consultations (
            id           TEXT PRIMARY KEY,
            caller       TEXT NOT NULL DEFAULT '',
            caller_kind  TEXT NOT NULL DEFAULT '',
            repo_path    TEXT NOT NULL DEFAULT '',
            branch       TEXT NOT NULL DEFAULT '',
            head_sha     TEXT NOT NULL DEFAULT '',
            vendor       TEXT NOT NULL DEFAULT '',
            model        TEXT NOT NULL DEFAULT '',
            turns        INTEGER NOT NULL DEFAULT 0,
            status       TEXT NOT NULL DEFAULT '',
            reason       TEXT NOT NULL DEFAULT '',
            started_utc  TEXT NOT NULL DEFAULT '',
            ended_utc    TEXT NOT NULL DEFAULT '',
            seconds      REAL NOT NULL DEFAULT 0,
            tokens_in    INTEGER NOT NULL DEFAULT 0,
            tokens_out   INTEGER NOT NULL DEFAULT 0,
            cost_usd     REAL,
            problem      TEXT NOT NULL DEFAULT '',
            advice       TEXT NOT NULL DEFAULT '',
            -- The filesystem invariant's sentence, when it fired. The one field a person must read.
            alert        TEXT NOT NULL DEFAULT ''
        );

        CREATE INDEX IF NOT EXISTS consultations_by_time ON consultations (started_utc DESC);

        ALTER TABLE rounds ADD COLUMN consult_missed INTEGER NOT NULL DEFAULT -1;
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

    /// <summary>The other end of the range a code round read.</summary>
    /// <remarks>
    /// <c>head_sha</c> shipped alone, and one end of a range does not describe a diff. The base is
    /// also the half that cannot be recovered afterwards: the commit stays in the repository whether
    /// or not anybody wrote it down, while the ref the diff was resolved against lives in a local
    /// for the length of one call and is then gone for good.
    /// </remarks>
    internal const string WhatItWasAgainst = """
        ALTER TABLE rounds ADD COLUMN base_ref TEXT NOT NULL DEFAULT '';
        """;

    /// <summary>What the corpus collector has done with a finding, if anything.</summary>
    /// <remarks>
    /// <para>An accepted finding is a defect a person confirmed, and the collector turns those into
    /// anonymous before/after pairs. These four columns are what it may not lose: WHICH run looked
    /// at a finding, what it concluded, why it concluded that, and the commit it found the fix in.
    /// See <c>research/PLAN_a_corpus_of_real_defects.md</c>.</para>
    /// <para><b><c>collect_state</c> is text and not a flag</b>, and that is the load-bearing choice.
    /// A boolean says only "looked at", so the first run consumes every finding it saw and a second
    /// run — with a better prompt, a wider language set, a repaired walk — can never see them again.
    /// Three states plus the empty one keep a skip distinguishable from a success, and
    /// <c>collect_reason</c> keeps WHY: a skip rate is a measurement, and the trigger for changing
    /// how the fix is resolved is a number nobody can read off a flag.</para>
    /// <para>Only <c>collect_state</c> is read by <see cref="BugsQuery"/> today; the other three are
    /// written by the collector and read back beside it, so a run's own record is one row.</para>
    /// </remarks>
    internal const string TheCollectorsState = """
        ALTER TABLE findings ADD COLUMN collect_state  TEXT NOT NULL DEFAULT '';
        ALTER TABLE findings ADD COLUMN collect_run_id TEXT NOT NULL DEFAULT '';
        ALTER TABLE findings ADD COLUMN collect_reason TEXT NOT NULL DEFAULT '';
        ALTER TABLE findings ADD COLUMN fix_sha        TEXT NOT NULL DEFAULT '';
        """;

    /// <summary>The runs themselves, so a button can say what is happening.</summary>
    /// <remarks>
    /// <para><b>A run id on a finding says which run decided it; it does not say what that run DID,</b>
    /// or whether one is happening now. Story 3 needed neither — its only surface was a CLI one-shot
    /// whose summary a person read as it scrolled past. A button needs both: the durable-status rule
    /// requires the state to survive a reload and to be read back from storage rather than from a
    /// flag in a webview that a reload destroys.</para>
    /// <para><b><c>heartbeat_utc</c>, and deliberately not a pid.</b> A row must be able to say
    /// whether the run that owns it is still alive, because the panel reaches this database only
    /// through one-shot invocations — a sweep that cleared every unfinished row would clear a run
    /// that is alive at that moment. A pid cannot answer it here: the data directory may be a network
    /// share, so a pid can come from another machine, where it is not merely useless but will
    /// eventually name a live and unrelated process. A timestamp means the same thing on every host.
    /// (Plan round, gemini, refined.)</para>
    /// <para><b>Kept forever.</b> ~400 bytes a run, about one run a day — 150 KB a year, beside a
    /// database already megabytes of rounds. Deleting a run would orphan every finding whose
    /// <c>collect_run_id</c> names it, and that correlation is the whole reason the id exists.</para>
    /// </remarks>
    internal const string TheRunsThemselves = """
        CREATE TABLE IF NOT EXISTS collect_runs (
            id            TEXT PRIMARY KEY,
            started_utc   TEXT NOT NULL,
            finished_utc  TEXT NOT NULL DEFAULT '',
            heartbeat_utc TEXT NOT NULL,
            state         TEXT NOT NULL DEFAULT 'running',
            model         TEXT NOT NULL DEFAULT '',
            candidates    INTEGER NOT NULL DEFAULT 0,
            picked        INTEGER NOT NULL DEFAULT 0,
            collected     INTEGER NOT NULL DEFAULT 0,
            skipped       INTEGER NOT NULL DEFAULT 0,
            failed        INTEGER NOT NULL DEFAULT 0,
            reasons       TEXT NOT NULL DEFAULT ''
        );

        CREATE INDEX IF NOT EXISTS ix_collect_runs_started ON collect_runs(started_utc DESC);
        """;

    /// <summary>The pairs themselves — the artefact this whole plan exists to produce.</summary>
    /// <remarks>
    /// <para><b>The collector computed both skeletons and threw them away.</b> It has to compute
    /// them: comparing them is how it decides the method changed at all. Only <c>fix_sha</c>
    /// survived, so what shipped was a corpus of POINTERS — and recomputing a pair costs a git read,
    /// a locate, a normalise, a second git read and a second normalise, per candidate, at review
    /// time and again at upload time, with the symbol name not stored either.</para>
    /// <para><b>What is anonymous here, said precisely.</b> The two SKELETONS carry the
    /// zero-knowledge guarantee. The row around them does NOT: <c>symbol_name</c> is a name and
    /// <c>finding_id</c> joins straight back to the repository path, the commit, the file and the
    /// line. That adds no new exposure where it sits — this database already holds all four, and the
    /// reviewers' un-anonymised prose besides — because the boundary that matters is what LEAVES the
    /// machine, and only the skeletons and the language ever do. (Plan round, codex.)</para>
    /// <para><b><c>keep</c> is a person's decision and survives everything.</b> A review of two
    /// hundred pairs is not finished in one sitting, and `--all` must rewrite the pair without
    /// touching it — an ordinary upsert would take every decision back to <c>-1</c>, silently. Two
    /// reviewers found that independently.</para>
    /// <para><b>Kept for ever</b>, measured: a skeleton is 381 bytes at the median over 53 real
    /// methods from this repository, so a pair is 762 and the 462 usable candidates are 344 KB. A
    /// retention window here would delete the product.</para>
    /// </remarks>
    internal const string ThePairsThemselves = """
        CREATE TABLE IF NOT EXISTS collect_pairs (
            finding_id      INTEGER PRIMARY KEY REFERENCES findings(id),
            symbol_name     TEXT NOT NULL,
            language        TEXT NOT NULL,
            skeleton_before TEXT NOT NULL,
            skeleton_after  TEXT NOT NULL,
            written_utc     TEXT NOT NULL,
            -- -1 undecided, 0 dropped, 1 kept. NOT a boolean, for the reason `collect_state` is not
            -- one: "nobody has looked" and "somebody said no" are different answers, and a flag
            -- would make the first indistinguishable from the second for ever.
            keep            INTEGER NOT NULL DEFAULT -1
        );

        CREATE INDEX IF NOT EXISTS ix_collect_pairs_keep ON collect_pairs(keep);
        """;

    /// <summary>What has already been sent, so a second run does not send it again.</summary>
    /// <remarks>
    /// <para><b>Marked on the ACKNOWLEDGEMENT, never on the batch leaving.</b> A pair recorded as
    /// sent before the server answered is a pair lost in silence — the client would skip it for ever
    /// and nobody would know. A kill between the request and the reply must leave it to be retried,
    /// which is what an empty <c>sent_utc</c> means. (Plan round, codex and the local reviewer.)</para>
    /// <para><b>And a REFUSAL is its own column.</b> A pair the server would not take is a defect in
    /// our normaliser, not a transport failure: retrying it changes nothing and hides it. Keeping the
    /// reason means a person can see what leaked, and a later run with a repaired normaliser can
    /// clear the column and try again.</para>
    /// </remarks>
    internal const string WhatWasSent = """
        ALTER TABLE collect_pairs ADD COLUMN sent_utc     TEXT NOT NULL DEFAULT '';
        ALTER TABLE collect_pairs ADD COLUMN send_refusal TEXT NOT NULL DEFAULT '';
        """;
}
