using System.Security.Cryptography;

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
    internal static int Run(string[] args, string secret, string dataDir)
    {
        if (secret.Length == 0)
        {
            Say("COAI_BUGS_SECRET is not set; a key hashed with no secret is not hashed");

            return 78; // EX_CONFIG
        }

        using var corpus = Corpus.Open(Path.Combine(dataDir, "coai-bugs.db"));
        var now = DateTime.UtcNow.ToString("O");

        return args[0] switch
        {
            "--issue-key" => Issue(corpus, args, secret, now),
            "--revoke" => Revoke(corpus, args, now),
            "--promote" => Promote(corpus, args, now),
            _ => Waiting(corpus, args),
        };
    }

    /// <summary>
    /// Mints a key, prints it ONCE, and stores only its hash.
    /// </summary>
    /// <remarks>
    /// Printed to stdout so it can be piped into something that holds secrets, and never written to
    /// the database in a form anybody can read back: a stolen corpus must not become a set of working
    /// keys. If the holder loses it, they get a new one — there is nothing here to recover.
    /// </remarks>
    private static int Issue(Corpus corpus, string[] args, string secret, string now)
    {
        var note = Flag(args, "--note");
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

        corpus.Issue(id, Corpus.HashOf(key, secret), note, now);

        Say($"issued {id}" + (note.Length > 0 ? $" ({note})" : string.Empty));
        Say("the key is printed once and stored only as a hash — keep it or issue another");
        Console.Out.WriteLine(key);

        return 0;
    }

    private static int Revoke(Corpus corpus, string[] args, string now)
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

        if (!corpus.Revoke(id, now))
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
    private static int Promote(Corpus corpus, string[] args, string now)
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
        if (!corpus.Promote(id, now))
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
