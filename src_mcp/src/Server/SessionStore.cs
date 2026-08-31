using System.Text.Json;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>One completed round, kept so a resumed conversation can replay the story.</summary>
public sealed record RoundRecord(
    string Stage,
    int Number,
    string Verdict,
    int GatingCount,
    string Reviewers,
    DateTime CompletedUtc);

/// <summary>What the store persists: the state machine's state plus the human-readable trail.</summary>
public sealed record PersistedSession(SessionState State, List<RoundRecord> Rounds)
{
    /// <summary>The last round's merged findings — what `resolve`'s indices point into.</summary>
    public List<CoaiMcp.Core.Findings.Finding> Pending { get; init; } = [];
}

/// <summary>
/// Sessions survive the server: an MCP client restarting <c>coai-mcp</c> re-orients from disk
/// instead of forgetting the rounds — the durable-status rule, applied to ourselves.
/// </summary>
/// <remarks>
/// One JSON file per session key under the data dir. Writes are whole-file and atomic-ish
/// (temp + move): a torn session file would refuse every later call on that repo+branch.
/// </remarks>
public sealed class SessionStore(string dataDir)
{
    private string SessionsDir => Path.Combine(dataDir, "sessions");

    public PersistedSession? Load(string repoPath, string branch)
    {
        var file = FileFor(repoPath, branch);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(file), ServerJsonContext.Default.PersistedSession);
        }
        catch (JsonException)
        {
            return null; // a torn file is a fresh session, not a locked repo
        }
    }

    public void Save(PersistedSession session)
    {
        Directory.CreateDirectory(SessionsDir);
        var file = FileFor(session.State.RepoPath, session.State.Branch);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(session, ServerJsonContext.Default.PersistedSession));
        File.Move(temp, file, overwrite: true);
    }

    private string FileFor(string repoPath, string branch)
    {
        // The session key is not a valid file name; hash it and keep a readable prefix.
        var key = SessionKey.For(repoPath, branch);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(SessionsDir, $"session-{hash}.json");
    }
}
