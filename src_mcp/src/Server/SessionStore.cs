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
/// <summary>
/// A session could not be written, and the caller decides what that means.
/// </summary>
/// <remarks>
/// Named on purpose. The failure used to arrive as <see cref="UnauthorizedAccessException"/> from
/// <c>File.Move</c> — which is NOT an <see cref="IOException"/>, so the one <c>catch (IOException)</c>
/// written for exactly this case walked past it and took the round down with it. Six code rounds
/// died that way, one of them on the FINAL save with every reviewer already answered: the findings
/// were in memory and were thrown away for the sake of a file name nobody was told.
/// </remarks>
public sealed class SessionStoreException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Readers and writers of one session file take turns, across every process on this machine.
/// </summary>
/// <remarks>
/// <para>Sharing alone was not enough, and the measurement is what said so: with the reader opening
/// the file <c>ReadWrite | Delete</c> and the rename retried ten times over half a second, four
/// readers in a hot loop still starved the writer in two runs of three. Retrying harder is not a
/// mechanism — it is a hope with a bigger budget. A turn is a mechanism.</para>
/// <para>The same shape as <c>EngineLease</c>, which serialises one GPU across every process here:
/// an OS lock file, released by the kernel even when a process is killed. It is NOT named
/// <c>session-*.json</c>, so the orphan sweep's own enumeration cannot pick it up.</para>
/// <para>A turn that cannot be taken is not fatal on its own: the reader answers "no session" as it
/// always did for an unreadable file, and the writer goes on to fail loudly at the move. Blocking a
/// round on a lock file would be a worse failure than the one being fixed.</para>
/// </remarks>
internal sealed class SessionTurn : IDisposable
{
    private readonly FileStream? _held;

    private SessionTurn(FileStream? held) => _held = held;

    /// <summary>Whether the turn is actually ours — a writer decides how to write on this.</summary>
    public bool Held => _held is not null;

    public static SessionTurn Take(string sessionFile)
    {
        var lockFile = Path.ChangeExtension(sessionFile, null) + ".turn";
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                return new SessionTurn(new FileStream(
                    lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(Random.Shared.Next(2, 8));
            }
        }

        return new SessionTurn(null);
    }

    public void Dispose() => _held?.Dispose();
}

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
            return JsonSerializer.Deserialize(ReadShared(file), ServerJsonContext.Default.PersistedSession);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // a torn or momentarily busy file is a fresh session, not a locked repo
        }
    }

    /// <summary>
    /// Reads a session file WITHOUT forbidding anyone else to write it.
    /// </summary>
    /// <remarks>
    /// <para>The cause nobody looks for, and the one that actually killed six rounds:
    /// <c>File.ReadAllText</c> opens with <see cref="FileShare.Read"/>, so a READER forbids writing.
    /// Five <c>coai-mcp</c> processes were alive on this machine — one per VS Code window — each
    /// polling this directory, so a writer's <c>File.Move</c> landed on a file somebody was merely
    /// looking at and failed with <c>Access to the path is denied</c>.</para>
    /// <para><see cref="FileShare.Delete"/> is in the set as well as <c>ReadWrite</c>: on Windows a
    /// rename over an open file is a delete of that file, and a reader that permits writing but not
    /// deleting still blocks the move.</para>
    /// </remarks>
    private static string ReadShared(string file)
    {
        using var turn = SessionTurn.Take(file);
        using var stream = new FileStream(
            file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    public void Save(PersistedSession session)
    {
        try
        {
            Write(session);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // As ITSELF, so a caller can decide. A repaint may lose one; the answer to a finished
            // round may not be thrown away over it.
            throw new SessionStoreException(
                $"the session for '{session.State.Branch}' could not be written: {e.Message}", e);
        }
    }

    private void Write(PersistedSession session)
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
        var json = JsonSerializer.Serialize(session, ServerJsonContext.Default.PersistedSession);
        using (var turn = SessionTurn.Take(file))
        {
            if (turn.Held)
            {
                // IN PLACE, and no rename at all. Temp-and-move exists to keep a reader from seeing
                // half a file; the turn does that better, because readers take the same turn. Once
                // the lock is held there is nothing left for the rename to buy — and the rename was
                // the only operation that could be denied. Serialising happens BEFORE the turn is
                // taken, so a writer never holds it over work readers do not need.
                File.WriteAllText(file, json);
                return;
            }
        }

        // Only when the turn could not be taken at all: fall back to the old shape, which at least
        // cannot show a reader a half-written file.
        File.WriteAllText(temp, json);
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
        // Ten attempts with a jittered back-off — about half a second of patience in the worst case.
        // The first version allowed five and a fixed 20 ms step, and a test with four readers in a
        // hot loop spent the whole budget and failed: on Windows a rename over an open file needs
        // that file's handle to permit deletion, and while a reader now permits it, the two still
        // have to miss each other. Real readers poll every few seconds rather than continuously, so
        // this budget is generous there and merely sufficient under the test's deliberate hostility.
        // The jitter matters because several writers backing off in lock-step retry in lock-step.
        const int attempts = 10;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                File.Move(temp, file, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < attempts)
            {
                Thread.Sleep(Random.Shared.Next(5, 15) * attempt);
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
                session = JsonSerializer.Deserialize(ReadShared(file), ServerJsonContext.Default.PersistedSession);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
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
