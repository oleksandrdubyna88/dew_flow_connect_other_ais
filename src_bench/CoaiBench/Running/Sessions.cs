using System.Text.Json.Nodes;

namespace CoaiBench.Running;

/// <summary>
/// A run starts from a clean session, or it is measuring the last one.
/// </summary>
/// <remarks>
/// <para>A session is keyed by repo+branch and REMEMBERS the configuration it was opened with, along
/// with how far its stages have got. So a second campaign over the same commit does not re-measure
/// anything: `open` hands back the old session, the plan stage may already be `Done`, and the round
/// budget and thresholds are the ones from whenever it was first created.</para>
/// <para>Observed, and it cost a whole campaign: a run wrote into a directory a previous one had
/// used, and every round came out on `maxRounds 3, threshold 2, onExhausted Human` — the defaults —
/// while the operator's settings said 1, 6 and good_enough. The numbers looked like a product
/// problem and were a bench problem.</para>
/// <para>The protocol says this itself, in the refusal a stale session produces: <i>"the plan stage
/// is over for this session; open a new session for a new plan"</i>. This is how the bench opens one.</para>
/// </remarks>
public static class Sessions
{
    /// <summary>Removes the session for this repo+branch, if there is one. Returns whether there was.</summary>
    public static bool Reset(string dataDir, string repoPath, string branch)
    {
        var sessions = Path.Combine(dataDir, "sessions");
        if (!Directory.Exists(sessions))
        {
            return false;
        }

        var removed = false;
        foreach (var file in Directory.EnumerateFiles(sessions, "session-*.json"))
        {
            if (!Describes(file, repoPath, branch))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                removed = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Somebody is holding it — another window mid-round on the same branch. Leaving it
                // is better than fighting for it; the run will say what it found instead.
            }
        }

        return removed;
    }

    /// <summary>
    /// Whether this file is the session for that repo and branch.
    /// </summary>
    /// <remarks>
    /// By reading it rather than by recomputing the server's file name. The name is a hash of a
    /// canonicalised key, and a bench that reimplements that hash is one release away from deleting
    /// the wrong file — or, worse, none.
    /// </remarks>
    private static bool Describes(string file, string repoPath, string branch)
    {
        try
        {
            using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var state = (JsonNode.Parse(reader.ReadToEnd()) as JsonObject)?["state"] as JsonObject;

            return Same(state?["repoPath"]?.GetValue<string>(), repoPath)
                && (state?["branch"]?.GetValue<string>() ?? string.Empty)
                    .Equals(branch, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>Paths differ by separator and case on Windows; the session key does not.</summary>
    private static bool Same(string? one, string other) =>
        (one ?? string.Empty).Replace('\\', '/').TrimEnd('/')
            .Equals(other.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
