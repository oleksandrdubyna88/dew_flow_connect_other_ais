namespace CoaiMcp.Install;

/// <summary>The arguments of <c>--setup-clients</c> / <c>--install</c>, parsed — or the sentence that refuses them.</summary>
/// <param name="DryRun">Plan and report; change nothing, make no backup, start no vendor command.</param>
/// <param name="Only">The clients asked for; every client when <c>--only</c> was not given.</param>
/// <param name="Binary">The path to register, or empty for this binary's own (the stable path of epoch 1).</param>
/// <param name="Remove">Take our entries away.</param>
/// <param name="Restore">A backup id or <c>latest</c>, or empty.</param>
/// <param name="Error">Why the arguments are refused; empty when they are fine.</param>
internal sealed record SetupArgs(bool DryRun, IReadOnlyList<Client> Only, string Binary, bool Remove, string Restore, string Error)
{
    internal static string Usage(string mode) =>
        $"--{mode} [--dry-run] [--only <{Clients.Legal.Replace(", ", ",")}>] [--binary <path>] [--remove | --restore <id|latest>]";

    /// <summary>Parses everything after the mode word. A bad argument is an answer, never an exception: the mode is KNOWN, so it is 65 and never 64.</summary>
    internal static SetupArgs Parse(string mode, IReadOnlyList<string> args)
    {
        var parsed = new SetupArgs(false, Clients.All, string.Empty, false, string.Empty, string.Empty);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var at = 0; at < args.Count; at++)
        {
            var flag = args[at];
            if (!seen.Add(flag))
            {
                return Refused(mode, $"{flag} is given twice");
            }

            switch (flag)
            {
                case "--dry-run":
                    parsed = parsed with { DryRun = true };
                    break;
                case "--remove":
                    parsed = parsed with { Remove = true };
                    break;
                case "--shared":
                    return Refused(mode, "--shared is epoch 2: the shared VM instance waits for its own spike, and nothing is written for it now");
                case "--only" or "--binary" or "--restore":
                    if (at + 1 >= args.Count || args[at + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        return Refused(mode, $"{flag} needs a value");
                    }

                    var value = args[++at];
                    var next = flag switch
                    {
                        "--only" => OnlyOf(parsed, value),
                        "--binary" => parsed with { Binary = value },
                        _ => parsed with { Restore = value },
                    };
                    if (next.Error.Length > 0)
                    {
                        return next;
                    }

                    parsed = next;
                    break;
                default:
                    return Refused(mode, $"unknown argument '{flag}'");
            }
        }

        return parsed.Remove && parsed.Restore.Length > 0
            ? Refused(mode, "--remove and --restore cannot be given together")
            : (parsed.Remove || parsed.Restore.Length > 0) && parsed.Binary.Length > 0
                ? Refused(mode, "--binary names what to register; --remove and --restore register nothing")
                : parsed;
    }

    private static SetupArgs OnlyOf(SetupArgs parsed, string list)
    {
        var words = list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var clients = new List<Client>();
        foreach (var word in words)
        {
            if (Clients.Parse(word) is not { } client)
            {
                return parsed with { Error = $"--only names '{word}', which is not a client; the legal ones are {Clients.Legal}" };
            }

            if (!clients.Contains(client))
            {
                clients.Add(client);
            }
        }

        return clients.Count == 0
            ? parsed with { Error = $"--only names no client; the legal ones are {Clients.Legal}" }
            : parsed with { Only = [.. Clients.All.Where(clients.Contains)] };
    }

    private static SetupArgs Refused(string mode, string why) =>
        new(false, Clients.All, string.Empty, false, string.Empty, $"{why} — usage: {Usage(mode)}");
}
