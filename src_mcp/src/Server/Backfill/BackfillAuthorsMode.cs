using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>
/// What a test may watch or do inside the mode: a trace of every store it opens and every write it makes, and a hand between the two halves of the
/// backup — a concurrent writer. Never reached from the product's own start: <c>Program</c> passes none.
/// </summary>
/// <param name="Trace">Called with <c>open:ReadOnly</c>, <c>backup:start</c>, <c>backup:verified</c>, <c>open:ReadWrite</c> and <c>write</c>, in the order they happen.</param>
/// <param name="DuringBackup">Called after the backup is made and before it is verified — where a live server's commit would land.</param>
internal sealed record BackfillSeams(Action<string>? Trace = null, Action? DuringBackup = null)
{
    public void Saw(string what) => Trace?.Invoke(what);
}

/// <summary>A decorator that traces every write before it reaches the real writer — the order seam the backup test reads.</summary>
internal sealed class TracedWrites(IBackfillWrites inner, BackfillSeams seams) : IBackfillWrites
{
    public WriteResult Write(RoundWrite write)
    {
        seams.Saw("write");

        return inner.Write(write);
    }

    public WriteResult Heal(StoredGate gate, StoredRound round, string expected)
    {
        seams.Saw("write");

        return inner.Heal(gate, round, expected);
    }
}

/// <summary>
/// <c>--backfill-authors [--since &lt;ISO-8601 UTC instant&gt;] [--dry-run]</c>: gives every settled plan or code round the authors and the size the live path
/// would have written, from what is on disk — over THIS side's own store and transcripts, once per side (E7.S4 of
/// <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E7.md</c>).
/// </summary>
/// <remarks>
/// <para><b>The order is the safety.</b> The store is opened READ-ONLY for the selection, the counts and the whole of <c>--dry-run</c>. A writing run makes a
/// VERIFIED backup first (<see cref="BackfillBackup"/>) and opens the store for writing only after it verified — so even the migration of a store an older
/// server left behind happens after the backup. A backup that does not verify writes nothing: exit 74, the folder kept.</para>
/// <para><b>Exit codes</b>, as every one-shot mode here: <b>0</b> done (a summary on stdout), including nothing to do and a missing store; <b>65</b> a bad,
/// repeated or unknown argument; <b>74</b> a store or session file that will not read or write, a side that has not run the release, or a backup that did
/// not verify; <b>75</b> the run completed but something was deferred — the summary says so, and to run again. <b>Never 64</b>: that means "this binary does
/// not know the mode".</para>
/// <para>It is a writer mode: a NAMED data folder that is missing is refused before the mode runs (<c>Program.WriterModes</c>). It deletes nothing, ever — no
/// backup, no file, no row.</para>
/// </remarks>
internal static class BackfillAuthorsMode
{
    internal const string Usage =
        "--backfill-authors takes `--since <ISO-8601 UTC instant>` (such as --since 2026-09-30T00:00:00Z) and `--dry-run`, each at most once, and nothing else";

    /// <summary>Authors came with schema step 24; a side below it has not run the release that carries E5.</summary>
    internal static int RequiredStep => Array.IndexOf(Schema.Steps, Schema.TheRoundsAuthors) + 1;

    private sealed record Options(DateTime? Since, bool DryRun);

    private sealed class Parsing
    {
        public DateTime? Since { get; set; }

        public bool SinceSeen { get; set; }

        public bool DryRun { get; set; }

        public string Why { get; set; } = string.Empty;
    }

    internal static async Task<int> RunAsync(
        string[] args, Func<string, string?> env, TextWriter output, Action<string> note, TimeProvider clock, BackfillSeams? seams = null, CancellationToken ct = default)
    {
        var parsing = Parse(args);
        if (parsing.Why.Length > 0)
        {
            note(parsing.Why);

            return 65; // EX_DATAERR: this binary HAS the mode and is refusing the request — never 64
        }

        var settings = PanelSettings.FromEnvironment(env);
        var options = new Options(parsing.Since, parsing.DryRun);
        var presence = RoundsDb.PresenceOf(settings.DataDir, File.GetAttributes);

        return presence switch
        {
            RootPresence.Absent => Nothing(settings.DataDir, options, output, note),
            RootPresence.Unknown => Refuse(note, "the rounds database could not be told apart from a missing one: the disk would not describe it"),
            _ => await Guarded(settings, options, output, note, clock, seams ?? new BackfillSeams(), ct),
        };
    }

    /// <summary>No database: nothing to backfill, and nothing is created — unless the name is taken by something that is not a database, which is the disk's fault, not an empty store.</summary>
    private static int Nothing(string dataDir, Options options, TextWriter output, Action<string> note) =>
        Directory.Exists(Path.Combine(dataDir, RoundsDb.FileName))
            ? Refuse(note, "the rounds database could not be read: its name is taken by a directory")
            : Print(output, BackfillSummary.Of(new BackfillCounts(), options.Since, options.DryRun, null, 0));

    // ---------- the arguments ----------

    private static Parsing Parse(string[] args)
    {
        var parsing = new Parsing();
        for (var at = 1; at >= 0 && at < args.Length;)
        {
            at = Take(args, at, parsing);
        }

        return parsing;
    }

    private static int Take(string[] args, int at, Parsing parsing) => args[at] switch
    {
        "--since" => TakeSince(args, at, parsing),
        "--dry-run" => TakeDryRun(at, parsing),
        _ => Fail(parsing),
    };

    private static int TakeSince(string[] args, int at, Parsing parsing)
    {
        if (parsing.SinceSeen || at + 1 >= args.Length || !Program.TryParseInstant(args[at + 1], out var instant))
        {
            return Fail(parsing);
        }

        parsing.Since = instant.UtcDateTime;
        parsing.SinceSeen = true;

        return at + 2;
    }

    private static int TakeDryRun(int at, Parsing parsing)
    {
        if (parsing.DryRun)
        {
            return Fail(parsing);
        }

        parsing.DryRun = true;

        return at + 1;
    }

    private static int Fail(Parsing parsing)
    {
        parsing.Why = Usage;

        return -1;
    }

    // ---------- one run ----------

    private static async Task<int> Guarded(
        PanelSettings settings, Options options, TextWriter output, Action<string> note, TimeProvider clock, BackfillSeams seams, CancellationToken ct)
    {
        try
        {
            return await Execute(settings, options, output, note, clock, seams, ct);
        }
        catch (Exception e) when (Program.Unreadable(e) || e is SessionStoreException)
        {
            // A store or a session file that will not read or write is the disk, not the request: 74, as every mode here.
            note(Program.WhyUnreadable(e));

            return 74; // EX_IOERR
        }
    }

    private static async Task<int> Execute(
        PanelSettings settings, Options options, TextWriter output, Action<string> note, TimeProvider clock, BackfillSeams seams, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        seams.Saw("open:ReadOnly");
        using var read = OpenReadOnly(settings.DataDir);
        if (UserVersion(read) < RequiredStep)
        {
            return Refuse(note, $"this side has not run the release that carries the authors: the rounds database is at schema step {UserVersion(read)}, and this build needs step {RequiredStep}");
        }

        var backup = options.DryRun ? null : MakeBackup(settings.DataDir, read, clock, seams);
        if (backup is { Check.Verified: false })
        {
            Print(output, BackfillSummary.Of(new BackfillCounts(), options.Since, options.DryRun, backup, watch.ElapsedMilliseconds));

            return Refuse(note, "the backup did not verify, so nothing was written: " + FailedChecks(backup.Check));
        }

        var counts = new BackfillCounts();
        await Run(settings, options, read, counts, seams, ct);

        return Finish(output, BackfillSummary.Of(counts, options.Since, options.DryRun, backup, watch.ElapsedMilliseconds));
    }

    private static BackupOutcome MakeBackup(string dataDir, SqliteConnection read, TimeProvider clock, BackfillSeams seams)
    {
        seams.Saw("backup:start");
        var backup = BackfillBackup.Make(dataDir, read, clock, seams);
        if (backup.Check.Verified)
        {
            seams.Saw("backup:verified");
        }

        return backup;
    }

    /// <summary>The run over the stored history: dry, over the read-only connection alone; or writing, through a read-write store opened only now.</summary>
    private static async Task Run(PanelSettings settings, Options options, SqliteConnection read, BackfillCounts counts, BackfillSeams seams, CancellationToken ct)
    {
        var launcher = new ProcessLauncher();
        var log = Serilog.Core.Logger.None;
        var store = new SessionStore(settings.DataDir);
        var sizer = new StoredSizer(new ContextAssembler(launcher), launcher, log);
        if (options.DryRun)
        {
            await new BackfillRun(settings, store, read, new TracedWrites(new DryWrites(), seams), counts, sizer, log, options.Since).RunAsync(ct);

            return;
        }

        seams.Saw("open:ReadWrite");
        using var db = RoundsDb.OpenExisting(settings.DataDir, log) ?? throw new IOException("the rounds database could not be opened for writing after its backup verified");
        var writes = new TracedWrites(new BackfillWriter(settings.DataDir, store, db), seams);
        await new BackfillRun(settings, store, read, writes, counts, sizer, log, options.Since).RunAsync(ct);
    }

    // ---------- the store ----------

    /// <summary>A read-only connection: it cannot migrate and cannot write; a reader that held a snapshot open would pin the write-ahead log, so none is left open.</summary>
    private static SqliteConnection OpenReadOnly(string dataDir)
    {
        var connection = new SqliteConnection($"Data Source={Path.Combine(dataDir, RoundsDb.FileName)};Mode=ReadOnly;Pooling=False;Default Timeout=5");
        connection.Open();

        return connection;
    }

    private static long UserVersion(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = "PRAGMA user_version";

        return Convert.ToInt64(read.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------- the answer ----------

    private static int Finish(TextWriter output, BackfillSummary summary)
    {
        Print(output, summary);

        return summary.Deferred.Busy + summary.Deferred.Unreadable + summary.Deferred.ChangedUnderUs + summary.Deferred.PeersUnknown > 0 ? 75 : 0; // EX_TEMPFAIL
    }

    private static int Print(TextWriter output, BackfillSummary summary)
    {
        output.WriteLine(JsonSerializer.Serialize(summary, ServerJsonContext.Default.BackfillSummary));

        return 0;
    }

    private static int Refuse(Action<string> note, string why)
    {
        note(why);

        return 74; // EX_IOERR
    }

    private static string FailedChecks(BackfillBackupCheck check) =>
        string.Join(", ", new (string Name, bool Held)[]
        {
            ("opened", check.Opened), ("integrity", check.Integrity), ("userVersion", check.UserVersion), ("rowsMatch", check.RowsMatch),
            ("dataVersionUnchanged", check.DataVersionUnchanged), ("filesMatch", check.FilesMatch),
        }.Where(item => !item.Held).Select(item => item.Name));
}
