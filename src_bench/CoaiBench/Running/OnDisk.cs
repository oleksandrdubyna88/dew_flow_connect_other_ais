using System.Text.Json.Nodes;

namespace CoaiBench.Running;

/// <summary>What the session file says after a run — which is not the same as what the answer said.</summary>
/// <param name="Rounds">How many rounds the file carries.</param>
/// <param name="StillRunning">Rounds left at `running`, which a finished run must not have.</param>
/// <param name="Pending">What the run left unresolved — informational; a run that did its job leaves none.</param>
/// <param name="Note">What went wrong reading it, or empty.</param>
public sealed record SessionOnDisk(int Rounds, int StillRunning, int Pending, string Note = "")
{
    /// <summary>`state.config` as the server wrote it — what a settings check is measured against.</summary>
    public JsonObject? Config { get; init; }

    /// <summary>
    /// Whether the session was left as a finished run leaves it: every round written to a terminal
    /// state, and the file readable.
    /// </summary>
    /// <remarks>
    /// <para>The check exists because the alternative was believed for an afternoon. A round returned
    /// findings, numbered, with an instruction to resolve them — and its record had never been
    /// written, so the session said `running` and every index pointed into nothing. The answer looked
    /// perfect. Only the disk knew.</para>
    /// <para>It used to demand <c>Pending &gt; 0</c> as well, and that was a definition that could only
    /// be true for somebody else's session: the bench resolves every finding straight after each
    /// stage, so a run that did its job leaves NOTHING pending. The first campaign on 0.17.1 came back
    /// with every run tagged not resolvable at zero pending, and the day before, "resolvable" had
    /// meant a neighbour's forty findings read out of the shared directory. Whether the findings could
    /// be resolved is answered where the server says so — the resolve call's own reply, kept on the
    /// stage as <see cref="Model.StageResult.ResolveRefused"/>.</para>
    /// </remarks>
    public bool Clean => Note.Length == 0 && StillRunning == 0;
}

/// <summary>Reads what the server left behind for ONE run, without asking the server.</summary>
public static class OnDisk
{
    /// <param name="dataDir">The server's data directory — often the operator's own.</param>
    /// <param name="repoPath">The repository this run reviewed.</param>
    /// <param name="branch">The branch or commit it reviewed, which together with the repo IS the session.</param>
    public static SessionOnDisk Read(string dataDir, string repoPath, string branch)
    {
        var sessions = Path.Combine(dataDir, "sessions");
        if (!Directory.Exists(sessions))
        {
            return new SessionOnDisk(0, 0, 0, "no sessions directory — the server wrote nothing");
        }

        // THIS run's session, never every session in the directory. The bench writes into the real
        // data directory on purpose — the operator watches the rounds appear in the panel while it
        // works — and that directory belongs to every window on this machine. Reading all of it
        // reported `1 still running, 40 pending` against a run that had finished cleanly: the forty
        // were a neighbour's, and so was the one.
        var byOwner = Directory.EnumerateFiles(sessions, "session-*.json")
            .ToLookup(f => Sessions.Owner(f, repoPath, branch));
        var files = byOwner[Sessions.Whose.Mine].ToList();
        if (files.Count == 0)
        {
            // A file nobody can read might have been this run's. Saying "no session file" about it
            // would report a broken write as no write at all.
            var torn = byOwner[Sessions.Whose.Unreadable].FirstOrDefault();

            return new SessionOnDisk(0, 0, 0, torn is null
                ? $"no session file for {repoPath} @ {branch}"
                : $"{Path.GetFileName(torn)} does not parse");
        }

        var rounds = 0;
        var running = 0;
        var pending = 0;
        JsonObject? config = null;
        foreach (var file in files)
        {
            // It parsed a moment ago, when it was matched. A server replacing it in between is the
            // one way this fails, and it is worth saying so rather than counting it as empty.
            var session = Parse(file);
            if (session is null)
            {
                return new SessionOnDisk(0, 0, 0, $"{Path.GetFileName(file)} does not parse");
            }

            var theseRounds = session["rounds"] as JsonArray ?? [];
            rounds += theseRounds.Count;
            running += theseRounds.OfType<JsonObject>()
                .Count(r => r["status"]?.GetValue<string>() == "running");
            pending += (session["pending"] as JsonArray)?.Count ?? 0;
            config ??= session["state"]?["config"] as JsonObject;
        }

        return new SessionOnDisk(rounds, running, pending) { Config = config };
    }

    private static JsonObject? Parse(string file)
    {
        try
        {
            // Shared, and permitting deletion: a server may still be replacing this very file, and a
            // reader that forbids that is how six rounds died here in one afternoon.
            using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            return JsonNode.Parse(reader.ReadToEnd()) as JsonObject;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
