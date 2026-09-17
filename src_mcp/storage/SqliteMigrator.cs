using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Storage;

/// <summary>
/// Brings a SQLite file up to the schema a build expects: ordered steps, and <c>user_version</c>
/// recording how many of them the file has run.
/// </summary>
/// <remarks>
/// <para><b>One runner for every database this repository owns.</b> It was <c>RoundsDb.Migrate</c>,
/// private to <c>coai-mcp</c>; <c>coai-bugs</c> reached a host with its schema in one statement and a
/// note saying it would gain "the same discipline" when it shipped. The gate ruled against a second
/// copy — two runners drift, and the one that drifts is the one nobody re-reads — so it lives here,
/// in a project both binaries reference, and NOT in <c>CoaiMcp.Core</c>: that one is declared pure
/// (no Process, no HttpClient, no filesystem) and a database file is the filesystem.</para>
/// <para><b>What it guarantees.</b> <c>CREATE TABLE IF NOT EXISTS</c> alone is not a migration: it
/// creates nothing when the table already exists, so a column added later is missing on every file
/// an older build created. Each step runs ONCE, in order, and a step and the version bump it earns
/// are ONE transaction. SQLite makes DDL transactional, and an <c>ALTER TABLE ADD COLUMN</c> is not
/// idempotent the way <c>CREATE TABLE IF NOT EXISTS</c> is: a process killed between the alter and
/// the bump would otherwise re-run the step on the next open, answer <c>duplicate column name</c>,
/// and leave a file that never opens again. Raised by codex on the #174 plan round; proved by the
/// test that injects a step which fails after its first statement succeeded, which is the only way
/// the property can be observed, because every real step succeeds.</para>
/// <para><b>A step is additive, and a step that has shipped is never edited.</b> It is what every
/// file in the field already contains; rewriting it changes nothing on disk while changing what a
/// NEW file gets — two shapes under one version number, which is the failure the number exists to
/// prevent. Each owner freezes its first step against the release that shipped it.</para>
/// <para><b>Two pragmas, outside the transaction.</b> WAL, so a reader — a one-shot mode, a second
/// process opening the same file — sees a consistent snapshot while a writer is mid-transaction; and
/// <c>busy_timeout</c>, so a concurrent opener WAITS for a writer instead of failing at once with
/// <c>SQLITE_BUSY</c>. A journal mode cannot be set inside a transaction, which is why both sit
/// before the loop. The provider has a wait of its own, taken from the connection string's
/// <c>Default Timeout</c>; <see cref="DefaultTimeoutFragment"/> is how an owner makes the two numbers
/// one number rather than two that happen to agree.</para>
/// </remarks>
public static class SqliteMigrator
{
    /// <summary>How long a concurrent opener waits for a writer before giving up.</summary>
    /// <remarks>
    /// Five seconds covers every write either owner makes — a migration step, a batch of two hundred
    /// pairs, an administrative action — with room to spare, and is short enough that a genuinely
    /// wedged writer surfaces as an error a person can read rather than as a hang.
    /// </remarks>
    public const int BusyTimeoutMilliseconds = 5_000;

    /// <summary>
    /// The connection-string fragment that makes the provider's own wait agree with the pragma.
    /// </summary>
    /// <remarks>
    /// Microsoft.Data.Sqlite retries a busy statement until its command timeout, which defaults to
    /// the connection's <c>Default Timeout</c> in seconds — thirty, when the string says nothing.
    /// Rounded UP, because a fragment reading <c>Default Timeout=0</c> would mean "wait for ever" to
    /// the provider, and a test that shortens the budget to prove it is real must shorten both.
    /// </remarks>
    public static string DefaultTimeoutFragment =>
        $"Default Timeout={(BusyTimeoutMilliseconds + 999) / 1000}";

    /// <summary>Applies every step the file has not yet run, in order, one transaction each.</summary>
    /// <param name="db">An OPEN connection to the file.</param>
    /// <param name="steps">
    /// The schema as ordered steps — the owner's constant everywhere but the test that proves the
    /// transaction is real.
    /// </param>
    public static void Migrate(SqliteConnection db, IReadOnlyList<string> steps)
    {
        Run(db, "PRAGMA journal_mode=WAL"); // outside: a journal mode cannot be set in a transaction
        Run(db, $"PRAGMA busy_timeout={BusyTimeoutMilliseconds}");
        while (true)
        {
            // IMMEDIATE, and the version is read INSIDE it. Read outside, two openers — the service
            // and a one-shot starting together — both read 0, both ran step 1, and the second then
            // failed step 2's ALTER with `duplicate column name` after its own step-1 transaction had
            // set the version BACK to 1: a file at version 1 with the column present, which no later
            // open could repair. The write lock is taken before the read, so the second opener waits
            // (busy_timeout) and then reads what the first one left. Raised by codex on the story-1
            // code round; reproduced by the test that injects a step slow enough to hold the window
            // open, because the real steps close it in a millisecond.
            using var applying = db.BeginTransaction(deferred: false);
            var version = Version(db);
            if (version >= steps.Count)
            {
                applying.Commit();

                return;
            }

            Run(db, steps[version]);
            Run(db, $"PRAGMA user_version={version + 1}");
            applying.Commit();
        }
    }

    /// <summary>How many steps the file records having run. Zero for a file no build has migrated.</summary>
    public static int Version(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = "PRAGMA user_version";

        return Convert.ToInt32(read.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
