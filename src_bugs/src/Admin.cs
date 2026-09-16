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
            _ => Waiting(corpus),
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

            return 64; // EX_USAGE — this binary's modes are its whole interface, and this IS one
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
    private static int Promote(Corpus corpus, string[] args, string now)
    {
        var id = Flag(args, "--entry");
        if (id.Length == 0)
        {
            Say("--promote needs --entry <id>; run --waiting to see what is there");

            return 64; // EX_USAGE
        }

        if (!corpus.Promote(id, now))
        {
            Say($"nothing in quarantine with id {id}");

            return 69; // EX_UNAVAILABLE
        }

        Say($"promoted {id}; the corpus now holds {corpus.Held()}");

        return 0;
    }

    /// <summary>What is waiting for somebody to read it.</summary>
    /// <remarks>
    /// The skeletons themselves, because the decision a person makes is about what the code SAYS —
    /// a list of ids would be a list nobody can judge.
    /// </remarks>
    private static int Waiting(Corpus corpus)
    {
        var waiting = corpus.Waiting(50);
        if (waiting.Count == 0)
        {
            Say("nothing is waiting");

            return 0;
        }

        foreach (var (id, language, before, after) in waiting)
        {
            Console.Out.WriteLine($"--- {id}  [{language}]");
            Console.Out.WriteLine(before);
            Console.Out.WriteLine("  ->");
            Console.Out.WriteLine(after);
        }

        Say($"{waiting.Count} waiting; promote one with --promote --entry <id>");

        return 0;
    }

    private static string Flag(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : string.Empty;
    }

    /// <summary>To stderr, because stdout is this mode's answer.</summary>
    private static void Say(string line) => Console.Error.WriteLine($"[coai-bugs] {line}");
}
