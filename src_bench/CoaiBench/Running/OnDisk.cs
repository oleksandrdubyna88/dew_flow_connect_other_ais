using System.Text.Json.Nodes;

namespace CoaiBench.Running;

/// <summary>What the session file says after a run — which is not the same as what the answer said.</summary>
/// <param name="Rounds">How many rounds the file carries.</param>
/// <param name="StillRunning">Rounds left at `running`, which a finished run must not have.</param>
/// <param name="Pending">How many findings `resolve` can actually index into.</param>
/// <param name="Note">What went wrong reading it, or empty.</param>
public sealed record SessionOnDisk(int Rounds, int StillRunning, int Pending, string Note = "")
{
    /// <summary>`state.config` as the server wrote it — what a settings check is measured against.</summary>
    public JsonObject? Config { get; init; }

    /// <summary>
    /// Whether the findings the answer carried can be acted on at all.
    /// </summary>
    /// <remarks>
    /// The check exists because the alternative was believed for an afternoon. A round returned
    /// findings, numbered, with an instruction to resolve them — and its record had never been
    /// written, so the session said `running` and `pending` was empty and every index pointed into
    /// nothing. The answer looked perfect. Only the disk knew.
    /// </remarks>
    public bool Resolvable => Note.Length == 0 && StillRunning == 0 && Pending > 0;
}

/// <summary>Reads what the server left behind, without asking the server.</summary>
public static class OnDisk
{
    public static SessionOnDisk Read(string dataDir)
    {
        var sessions = Path.Combine(dataDir, "sessions");
        if (!Directory.Exists(sessions))
        {
            return new SessionOnDisk(0, 0, 0, "no sessions directory — the server wrote nothing");
        }

        var files = Directory.EnumerateFiles(sessions, "session-*.json").ToList();
        if (files.Count == 0)
        {
            return new SessionOnDisk(0, 0, 0, "no session file");
        }

        var rounds = 0;
        var running = 0;
        var pending = 0;
        JsonObject? config = null;
        foreach (var file in files)
        {
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
