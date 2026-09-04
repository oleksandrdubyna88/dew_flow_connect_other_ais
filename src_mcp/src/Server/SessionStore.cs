using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>Where one reviewer has got to. The panel shows these while the round is still open.</summary>
/// <param name="Seconds">
/// How long this reviewer actually ran, once it has finished — zero while it is queued or running.
/// </param>
/// <remarks>
/// The round already recorded its own elapsed time, and a round is as slow as its slowest reviewer:
/// "11m 2s" for nine of them says nothing about which one cost the eleven minutes. The scheduler
/// measures each reviewer anyway (<c>ReviewerProgress.Elapsed</c>) and was throwing the number away
/// at this boundary.
/// </remarks>
public sealed record ReviewerState(
    string Provider,
    string Role,
    string Status,
    int Findings = 0,
    string Note = "",
    double Seconds = 0)
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
}

/// <summary>
/// One round — written when it STARTS, updated as reviewers move, finished with its verdict.
/// </summary>
/// <remarks>
/// <para>It used to be written only at the end, which meant a ten-minute round showed nothing at
/// all while it ran and the panel could not distinguish "nobody has ever reviewed this" from "six
/// reviewers are working right now". The durable-status rule applied to ourselves: the state is
/// persisted before the work starts, advanced while it runs, and swept if the process dies.</para>
/// <para><paramref name="RunnerPid"/> is what makes the sweep safe: a second server sharing this
/// data directory must not declare another server's live round dead.</para>
/// </remarks>
public sealed record RoundRecord(
    string Stage,
    int Number,
    string Verdict,
    int GatingCount,
    string Reviewers,
    DateTime CompletedUtc)
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Interrupted = "interrupted";

    public string Status { get; init; } = Done;

    public DateTime StartedUtc { get; init; }

    public int RunnerPid { get; init; }

    /// <summary>Per-reviewer progress — the live part of a running round.</summary>
    public List<ReviewerState> ReviewerStates { get; init; } = [];

    /// <summary>
    /// What this round was ABOUT — the plan's file name or its title.
    /// </summary>
    /// <remarks>
    /// A round identified only by branch and number tells a reader which gate ran and nothing
    /// about what went through it, so a week of rounds reads as a column of numbers.
    /// </remarks>
    public string Subject { get; init; } = string.Empty;

    public long TokensIn { get; init; }

    public long TokensOut { get; init; }

    /// <summary>Only when a vendor priced its own run; null is "nobody told us", never "free".</summary>
    public double? CostUsd { get; init; }
}

/// <summary>What the store persists: the state machine's state plus the human-readable trail.</summary>
public sealed record PersistedSession(SessionState State, List<RoundRecord> Rounds)
{
    /// <summary>The last round's merged findings — what `resolve`'s indices point into.</summary>
    public List<Finding> Pending { get; init; } = [];

    /// <summary>
    /// The scope: what this change was supposed to achieve, as the plan stage stated it.
    /// </summary>
    /// <remarks>
    /// Kept because it is the SAME scope the code stage needs, and it was already agreed by both
    /// halves. Throwing it away between the stages and then asking the caller to send it again is
    /// how a caller ends up sending nothing — and a reviewer handed a bare diff can only judge
    /// whether the code is defensible, never whether it is what was asked for.
    /// </remarks>
    public string PlanText { get; init; } = string.Empty;

    /// <summary>
    /// Prompt ids this session has already asked, so the plan stage spends every lens once.
    /// </summary>
    /// <remarks>
    /// The plan role has three prompts and a round deals one to each vendor, so two vendors cover
    /// the pool in two rounds rather than asking the universal question twice. Without this the deal
    /// would be random with replacement, which asks the same lens again while another goes unasked.
    /// </remarks>
    public List<string> UsedPrompts { get; init; } = [];
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
        // A scratch name per WRITE, and that is a crash fix rather than a nicety. It used to be
        // `<session>.json.tmp` — one fixed path — and this machine runs several MCP clients at once,
        // each with a server of its own, sharing a data directory; a nine-reviewer round saves on
        // every reviewer transition. Two writers therefore wrote the SAME scratch file and both
        // tried to move it, and the loser died with `UnauthorizedAccessException: Access to the path
        // is denied` out of `LiveRound.Persist`. Twice in one morning's log before it was understood.
        var temp = $"{file}.{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(session, ServerJsonContext.Default.PersistedSession));
        try
        {
            MoveOverExisting(temp, file);
        }
        finally
        {
            // Ours alone, so cleaning up can never take another writer's scratch file with it.
            TryDelete(temp);
        }
    }

    /// <summary>
    /// The move, retried briefly — for what a unique scratch name cannot fix.
    /// </summary>
    /// <remarks>
    /// A reader, an indexer or a virus scanner holding the DESTINATION for a moment is also reported
    /// as access denied on Windows. Bounded and short: the last attempt is allowed to throw, because
    /// a save that never lands is a real problem and must surface as one rather than spin.
    /// </remarks>
    private static void MoveOverExisting(string temp, string file)
    {
        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                File.Move(temp, file, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < attempts)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Marks rounds abandoned by a dead process, once at startup. Without this a crashed round
    /// reads as "running" forever, which is the one thing the durable-status rule forbids.
    /// </summary>
    /// <returns>How many rounds were swept — logged, so a crash leaves a trace.</returns>
    public int SweepOrphanedRounds(Func<int, bool> processIsAlive)
    {
        if (!Directory.Exists(SessionsDir))
        {
            return 0;
        }

        var swept = 0;
        foreach (var file in Directory.EnumerateFiles(SessionsDir, "session-*.json"))
        {
            PersistedSession? session;
            try
            {
                session = JsonSerializer.Deserialize(File.ReadAllText(file), ServerJsonContext.Default.PersistedSession);
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                continue;
            }

            if (session is null || !session.Rounds.Any(r => IsOrphaned(r, processIsAlive)))
            {
                continue;
            }

            var rounds = session.Rounds
                .Select(r => IsOrphaned(r, processIsAlive)
                    ? r with { Status = RoundRecord.Interrupted, Verdict = "interrupted", CompletedUtc = DateTime.UtcNow }
                    : r)
                .ToList();
            swept += rounds.Count(r => r.Status == RoundRecord.Interrupted);
            Save(session with { Rounds = rounds });
        }

        return swept;
    }

    private static bool IsOrphaned(RoundRecord round, Func<int, bool> processIsAlive) =>
        round.Status == RoundRecord.Running && (round.RunnerPid == 0 || !processIsAlive(round.RunnerPid));

    private string FileFor(string repoPath, string branch)
    {
        // The session key is not a valid file name; hash it and keep a readable prefix.
        var key = SessionKey.For(repoPath, branch);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(SessionsDir, $"session-{hash}.json");
    }
}
