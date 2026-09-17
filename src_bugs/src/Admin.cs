using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using CoaiMcp.Storage;
using Microsoft.Data.Sqlite;

namespace CoaiBugs;

/// <summary>
/// The admin one-shots: issuing a key, ending one, and letting something out of quarantine.
/// </summary>
/// <remarks>
/// <para><b>Without these a fresh deployment is an empty key table and no documented way to fill
/// it</b>, so every upload is refused and the only remedy is an undocumented INSERT somebody works
/// out. A plan reviewer said so.</para>
/// <para><b>And quarantine needs a door.</b> The plan first said "a separate reviewed step" and named
/// no mechanism, which makes it a room nothing leaves. `--waiting` shows a person what arrived and
/// `--promote` moves one pair into the corpus; nothing is automatic and there is no timer, because
/// the whole reason quarantine exists is that a machine cannot tell a real skeleton from a crafted
/// one.</para>
/// </remarks>
internal static class Admin
{
    /// <summary>Every one-shot mode this binary answers, in one place.</summary>
    /// <remarks>
    /// <b>It was two lists.</b> `Program.Main` decided whether the arguments named an admin mode
    /// and <see cref="Run"/> decided which one, and the two were maintained by hand — so a new mode
    /// added to one and not the other either starts Kestrel instead of running the one-shot, or
    /// falls through this switch's default and silently runs `--waiting`. Both failures are silent,
    /// which is what makes a duplicated list worse than a long one. (Code round, codex.)
    /// </remarks>
    internal static readonly string[] Modes =
        ["--issue-key", "--revoke", "--promote", "--waiting"];

    /// <summary>A run that took this long waited on something, and a person is owed a sentence about it.</summary>
    private static readonly TimeSpan WorthMentioning = TimeSpan.FromSeconds(1);

    /// <summary>Whether these arguments name a mode this binary runs instead of listening.</summary>
    internal static bool Knows(string[] args) =>
        args.Length > 0 && Array.IndexOf(Modes, args[0]) >= 0;

    /// <param name="clock">
    /// Injected, per the UTC rule: every stamp a one-shot writes — a key's creation, an audit row,
    /// a promotion — comes from here, so a test can pin it. It does not take the serve lock: a
    /// one-shot is not a server, and running one while the service serves is what the database's
    /// busy timeout exists for.
    /// </param>
    /// <remarks>
    /// <para><b>The two faults a one-shot meets at the database are said in one line each</b>, at
    /// this edge, with an exit code a script can read — instead of a stack trace, which is what an
    /// unhandled <see cref="SqliteException"/> was. A file that is not a database (74) and a file that
    /// stayed locked past the busy timeout (75) are different situations for the person holding the
    /// terminal, and only one of them is worth trying again.</para>
    /// </remarks>
    internal static int Run(string[] args, string secret, string dataDir, TimeProvider clock)
    {
        if (secret.Length == 0)
        {
            Say("COAI_BUGS_SECRET is not set; a key hashed with no secret is not hashed");

            return 78; // EX_CONFIG
        }

        var path = Path.Combine(dataDir, Program.DatabaseFileName);
        var started = Stopwatch.StartNew();
        try
        {
            return Running(args, secret, path, clock, started);
        }
        catch (SqliteException e)
        {
            return e.SqliteErrorCode == 5 ? Busy(path, e) : Unusable(path, e);
        }
    }

    private static int Running(string[] args, string secret, string path, TimeProvider clock, Stopwatch started)
    {
        using var corpus = Corpus.Open(path);

        // Whoever runs a one-shot has a shell on the host: the audit names them `cli`, and the
        // exact time — this is a record about an administrator, which the promise allows. One
        // stamp for the whole run, so a key's creation time and its audit row agree to the tick.
        var by = Audit.By(AdminId.Cli, clock);

        // Every arm is a mode in `Modes`, and `Knows` is what let this be reached — so the default
        // is unreachable rather than a silent fallback to `--waiting`.
        var code = args[0] switch
        {
            "--issue-key" => Issue(corpus, args, secret, by),
            "--revoke" => Revoke(corpus, args, by),
            "--promote" => Promote(corpus, args, by.At),
            "--waiting" => Waiting(corpus, args),
            _ => throw new ArgumentOutOfRangeException(
                nameof(args), args[0], $"is in {nameof(Modes)} but has no arm here"),
        };
        Waited(started.Elapsed);

        return code;
    }

    /// <summary>Says so when the run waited on the database — a silent wait reads as a hang.</summary>
    /// <remarks>
    /// A one-shot beside the running server waits for the server's write on the busy timeout, and a
    /// terminal that stops for seconds with nothing said is a person reaching for Ctrl+C. Said after
    /// the fact, because nothing can know in advance; one second is where a person starts to wonder.
    /// </remarks>
    private static void Waited(TimeSpan elapsed)
    {
        if (elapsed >= WorthMentioning)
        {
            Say(
                $"that took {elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s: the "
                + "database was busy — the server was mid-write — and a one-shot waits up to "
                + $"{SqliteMigrator.BusyTimeoutMilliseconds / 1000} s for it");
        }
    }

    private static int Busy(string path, SqliteException e)
    {
        Say(
            $"{path} stayed locked for longer than the {SqliteMigrator.BusyTimeoutMilliseconds / 1000} s "
            + $"a one-shot waits ({e.Message}); the server is mid-write, or something else holds the "
            + "file — try again");

        return 75; // EX_TEMPFAIL
    }

    private static int Unusable(string path, SqliteException e)
    {
        Say(
            $"{path} cannot be used as this server's database: {e.Message} (SQLite error "
            + $"{e.SqliteErrorCode}); nothing here can repair it — restore it from the backup or move "
            + "it aside");

        return 74; // EX_IOERR
    }

    /// <summary>
    /// Mints a key, prints it ONCE, and stores only its hash.
    /// </summary>
    /// <remarks>
    /// Printed to stdout so it can be piped into something that holds secrets, and never written to
    /// the database in a form anybody can read back: a stolen corpus must not become a set of working
    /// keys. If the holder loses it, they get a new one — there is nothing here to recover.
    /// </remarks>
    private static int Issue(Corpus corpus, string[] args, string secret, Audit by)
    {
        var note = Flag(args, "--note");
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var id = new KeyId(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)));

        corpus.Issue(id, Corpus.HashOf(key, secret), note, by);

        Say($"issued {id.Value}" + (note.Length > 0 ? $" ({note})" : string.Empty));
        Say("the key is printed once and stored only as a hash — keep it or issue another");
        Console.Out.WriteLine(key);

        return 0;
    }

    private static int Revoke(Corpus corpus, string[] args, Audit by)
    {
        var id = Flag(args, "--id");
        if (id.Length == 0)
        {
            Say("--revoke needs --id <id>");

            // NOT 64. `.agents/PROJECT.md`: a binary that KNOWS a one-shot mode must never exit
            // 64, whatever is wrong with the request — 64 is reserved for "this binary has never
            // heard of that mode", which is how a caller detects an old binary and falls back.
            // This binary knows `--revoke` perfectly well; the ARGUMENT is what is wrong.
            // (Code round, codex/local.)
            return 65; // EX_DATAERR
        }

        if (!corpus.Revoke(new KeyId(id), by))
        {
            Say($"no key {id} is in force");

            return 69; // EX_UNAVAILABLE
        }

        Say($"revoked {id}");

        return 0;
    }

    /// <summary>Moves one pair out of quarantine, because a person read it.</summary>
    /// <remarks>
    /// <b>It prints what it moved, not only that it moved something.</b> The id is typed or pasted
    /// from `--waiting` and there is no undo, so the one defence against promoting the wrong row is
    /// seeing the row. A confirmation prompt was asked for instead; this is the same protection
    /// without making a one-shot mode unusable from a script. (Code round, local.)
    /// </remarks>
    private static int Promote(Corpus corpus, string[] args, UtcInstant at)
    {
        // Lowercased because the id is hex from `IdOf` and SQLite compares text byte for byte:
        // an id pasted from a terminal that upper-cased it would answer "nothing in quarantine"
        // about a row that is right there. (Code round, local.)
        var id = Flag(args, "--entry").ToLowerInvariant();
        if (id.Length == 0)
        {
            Say("--promote needs --entry <id>; run --waiting to see what is there");

            return 65; // EX_DATAERR — this binary knows the mode; the argument is what is wrong
        }

        // Read BEFORE the move, because after it the row is gone from quarantine and there is
        // nothing left to describe.
        var moving = corpus.Find(id);
        if (!corpus.Promote(id, at))
        {
            Say($"nothing in quarantine with id {id}");

            return 69; // EX_UNAVAILABLE
        }

        Say($"promoted [{moving.Language}] {First(moving.Before)}");
        Say($"the corpus now holds {corpus.Held()}");

        return 0;
    }

    /// <summary>A skeleton's first line, which is what identifies it to a person.</summary>
    private static string First(string skeleton)
    {
        var line = skeleton.Split('\n')[0].Trim();

        return line.Length > 72 ? line[..72] + "…" : line;
    }

    /// <summary>What is waiting for somebody to read it.</summary>
    /// <remarks>
    /// The skeletons themselves, because the decision a person makes is about what the code SAYS —
    /// a list of ids would be a list nobody can judge.
    /// </remarks>
    private static int Waiting(Corpus corpus, string[] args)
    {
        var total = corpus.WaitingCount();
        var limit = Number(args, "--limit", 50);
        var skip = Number(args, "--skip", 0);
        var waiting = corpus.Waiting(limit, skip);
        if (total == 0)
        {
            Say("nothing is waiting");

            return 0;
        }

        // The TOTAL first, and before the skeletons: it said "50 waiting" after printing fifty,
        // which reads as "that was all of them" whether there are fifty or five hundred, and the
        // number arrived after the scrollback had already gone past. (Code round, codex/local.)
        Say($"{total} waiting; showing {waiting.Count} from {skip}");

        foreach (var (id, language, before, after) in waiting)
        {
            Console.Out.WriteLine($"--- {id}  [{language}]");
            Console.Out.WriteLine(before);
            Console.Out.WriteLine("  ->");
            Console.Out.WriteLine(after);
        }

        Say(skip + waiting.Count < total
            ? $"{total - skip - waiting.Count} more; --waiting --skip {skip + waiting.Count}"
            : "promote one with --promote --entry <id>");

        return 0;
    }

    private static int Number(string[] args, string name, int fallback) =>
        int.TryParse(Flag(args, name), out var value) && value > 0 ? value : fallback;

    private static string Flag(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : string.Empty;
    }

    /// <summary>To stderr, because stdout is this mode's answer.</summary>
    private static void Say(string line) => Console.Error.WriteLine($"[coai-bugs] {line}");
}
