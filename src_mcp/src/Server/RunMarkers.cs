using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Core.Notices;
using CoaiMcp.ServiceDefaults;

namespace CoaiMcp.Server;

/// <summary>
/// One run's claim to be alive: who it is, where it runs, since when, and when it last said so.
/// </summary>
/// <remarks>
/// The plan's own field list (section 5 of PLAN_the_server_says_what_it_did.md), written as
/// <c>runs/{run}.json</c> beside <c>server-notices.jsonl</c>. It carries a run IDENTITY rather than a
/// pid: a pid is reused, and on a shared data directory it may name a process on another machine.
/// </remarks>
/// <param name="Run">Minted once per host start — the id every notice this run writes carries.</param>
/// <param name="Pid">Meaningful only on <paramref name="Host"/>, and only beside the start time.</param>
/// <param name="StartedUtc">When the PROCESS started, so a reused pid cannot pass for it.</param>
/// <param name="HeartbeatUtc">When the run last said it was alive.</param>
internal sealed record RunMarker(string Run, int Pid, string Host, DateTime StartedUtc, DateTime HeartbeatUtc);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RunMarker))]
internal sealed partial class RunMarkerContext : JsonSerializerContext;

/// <summary>What a file in the markers directory is.</summary>
internal enum MarkerFileKind
{
    /// <summary><c>{run}.json</c> — a run's own marker.</summary>
    Marker,

    /// <summary><c>{run}.claim</c> — a sweeper recording that run's death, right now or before it died.</summary>
    Claim,

    /// <summary><c>{run}.json.tmp</c> — a marker being written, or one whose write died.</summary>
    Temporary,
}

/// <summary>One file the sweep found.</summary>
/// <param name="Run">The run it is about, from its NAME — so it is known even when it cannot be read.</param>
/// <param name="Marker">The marker, for a <see cref="MarkerFileKind.Marker"/> that could be read.</param>
/// <param name="WrittenUtc">Its last write — what a claim's or an unreadable file's age is judged by.</param>
internal sealed record FoundMarker(string Path, MarkerFileKind Kind, string Run, RunMarker? Marker, DateTime WrittenUtc);

/// <summary>What a sweep decided.</summary>
/// <param name="Claim">Markers of runs that died, to claim and record.</param>
/// <param name="Break">Claims abandoned by a sweeper that died holding them, to delete before re-claiming.</param>
/// <param name="Retire">Files nothing will ever read again: a leftover claim, an old torn or unreadable file.</param>
internal sealed record MarkerSweep(
    IReadOnlyList<FoundMarker> Claim, IReadOnlyList<FoundMarker> Break, IReadOnlyList<FoundMarker> Retire);

/// <summary>
/// The run marker: written at start, beaten while the run lives, cleared by its owner's clean end —
/// and swept by the NEXT start, which records every run that never finished.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A server killed by SIGKILL, OOM, a power cut or a background-thread crash runs no
/// <c>catch</c> and leaves no record: the page shows a server that simply stopped talking. The only
/// party that can say it died is the next one to start, and only if the dead one left something
/// behind that says it was alive. (Story 3.1.)</para>
///
/// <para><b>The shape is <see cref="Runners.Processes.ProcessTracking"/>'s</b>, which the parent plan
/// names: one small file per entry, a pure decision kept apart from the I/O, every failure swallowed
/// with a note. Its LIVENESS is not: that is pid-based, and a pid cannot answer "is this run alive"
/// when the data directory is a network share whose owner is on another machine. Liveness here is a
/// HEARTBEAT, with one refinement from the plan round (gemini and local): a stale marker written on
/// THIS machine is checked against the process table too, pid AND start time, so a server paused under
/// a debugger or stalled for half an hour is not recorded as a death it is not.</para>
///
/// <para><b>A claim is an EXCLUSIVE CREATE, never a rename — measured.</b> The first version claimed
/// a death by renaming its marker, on the reasoning that a rename is atomic. It is not, through
/// .NET on Windows: with two threads renaming one file to two targets, BOTH renames succeeded in 975
/// of 1000 attempts, while <see cref="FileMode.CreateNew"/> let both succeed in 0 of 1000. The race
/// test here found it — 72 records for 50 deaths — before anything shipped. So a claim is a file
/// created with <see cref="FileMode.CreateNew"/>, which exactly one contender can win, and its AGE is
/// what says whether the sweeper that made it is still working: a live sweeper holds a claim for
/// milliseconds, so one older than the window was abandoned by a sweeper that died holding it.</para>
///
/// <para><b>An unreadable file is retired only when it is OLD.</b> A file being written can be read
/// half-written, and "could not parse it" must not become "delete a live run's marker". Temporary
/// files and unparseable markers are retired once they are older than the window, which is also what
/// keeps the directory bounded: every start removes every stale file it finds.</para>
///
/// <para><b>Residuals, stated.</b> A laptop asleep past the window, seen from ANOTHER machine on the
/// same share, is a false death — accepted in the plan's costs. A crash between the confirmed append
/// and the marker's deletion records one death twice: across separate files there is no crash-proof
/// exactly-once without a journal, so the guarantee is at least once, and the duplicate lands in the
/// SAME row because its subject is the dead run's id. The same is true of two starts that break one
/// abandoned claim at the same moment — a sweeper died mid-claim AND two starts raced to its leftovers.</para>
/// </remarks>
internal sealed class RunMarkers(
    ResolvedDataDir dataDir,
    RunMarker me,
    Func<DateTime> clock,
    Func<int, DateTime, bool> sameProcessAlive,
    Serilog.ILogger log)
{
    /// <summary>How often a live run says so.</summary>
    internal static readonly TimeSpan Beat = TimeSpan.FromSeconds(60);

    /// <summary>How long a run may be silent before the next start calls it dead: thirty beats.</summary>
    internal static readonly TimeSpan Stale = TimeSpan.FromMinutes(30);

    /// <summary>The directory, beside the notices file on the side-resolved data directory.</summary>
    internal const string Folder = "runs";

    private const string MarkerSuffix = ".json";

    private const string ClaimSuffix = ".claim";

    private const string TemporarySuffix = ".json.tmp";

    /// <summary>
    /// The production composition: this process, the machine's name, the real clock, the process table.
    /// </summary>
    /// <remarks>
    /// This run's start time is read by the SAME function a later sweep reads a live process's with —
    /// <see cref="Runners.Processes.ProcessTracking.StartedAt"/> — so the two are compared like with like
    /// and <see cref="Runners.Processes.OrphanSweep.Same"/>'s slack covers only clock rounding, not two
    /// different ways of asking.
    /// </remarks>
    internal static RunMarkers Of(ResolvedDataDir dataDir, string run, Serilog.ILogger log)
    {
        var pid = Environment.ProcessId;
        var now = DateTime.UtcNow;
        var me = new RunMarker(
            run, pid, Environment.MachineName, Runners.Processes.ProcessTracking.StartedAt(pid) ?? now, now);

        return new(dataDir, me, () => DateTime.UtcNow, SameProcessAlive, log);
    }

    /// <summary>Whether the process with that pid is the one that started then.</summary>
    private static bool SameProcessAlive(int pid, DateTime startedUtc) =>
        Runners.Processes.ProcessTracking.StartedAt(pid) is { } started
        && Runners.Processes.OrphanSweep.Same(started, startedUtc);

    private string Dir => Path.Combine(dataDir.Path, Folder);

    private string MarkerOf(string run) => Path.Combine(Dir, run + MarkerSuffix);

    private string ClaimOf(string run) => Path.Combine(Dir, run + ClaimSuffix);

    /// <summary>
    /// Write this run's marker, fresh — and unconditionally.
    /// </summary>
    /// <remarks>
    /// Unconditionally because a beat is EVIDENCE and a sweep is a guess: another server may have
    /// swept this one's marker while this machine slept, and a beat that respected that sweep would
    /// strand a live run for ever. (The lesson <c>collect_runs</c> learned.) A temporary file then a
    /// replace, so a reader almost never meets a half-written marker — and the sweep does not delete
    /// one it cannot read unless it is old, so "almost" is enough.
    /// </remarks>
    /// <returns>Whether the marker was written.</returns>
    internal bool Write()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var temporary = Path.Combine(Dir, me.Run + TemporarySuffix);
            File.WriteAllText(temporary, JsonSerializer.Serialize(
                me with { HeartbeatUtc = clock() }, RunMarkerContext.Default.RunMarker));
            File.Move(temporary, MarkerOf(me.Run), overwrite: true);

            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            log.Warning("run marker: could not write {Run}'s: {Why}", me.Run, failure.Message);

            return false;
        }
    }

    /// <summary>Remove this run's marker — by its owner, on a clean end, and never by anyone else.</summary>
    internal void Clear() => Remove(MarkerOf(me.Run));

    /// <summary>
    /// Record every run that never finished, once, and retire what nothing will read again.
    /// </summary>
    /// <param name="append">
    /// The confirmed road onto disk — <c>ServerNotices.Append</c> in production, which answers whether
    /// the record LANDED. A marker is removed only after that answer is yes.
    /// </param>
    /// <returns>How many deaths were recorded.</returns>
    /// <param name="stop">
    /// Honoured BETWEEN deaths: the one being written finishes, because a synchronous append cannot be
    /// recalled, and every other one stays on disk for the next start. A stop that waited for the whole
    /// sweep held a quick exit for the full stop budget on a slow share. (gemini, the code round.)
    /// </param>
    internal int Sweep(Func<ServerNotice, bool> append, CancellationToken stop)
    {
        var decided = Plan(Read(), clock(), me, Stale, sameProcessAlive);
        foreach (var gone in decided.Retire.Concat(decided.Break))
        {
            Remove(gone.Path);
        }

        return decided.Claim
            .TakeWhile(_ => !stop.IsCancellationRequested)
            .Count(found => Recorded(found.Marker!, append));
    }

    /// <summary>
    /// What to record, break and retire, decided without touching a disk or a process.
    /// </summary>
    /// <param name="sameProcessAlive">
    /// Whether a process with that pid, started at that time, is alive on THIS machine. Injected so
    /// the decision is a unit test rather than an experiment with real processes.
    /// </param>
    internal static MarkerSweep Plan(
        IReadOnlyList<FoundMarker> found,
        DateTime nowUtc,
        RunMarker me,
        TimeSpan window,
        Func<int, DateTime, bool> sameProcessAlive)
    {
        var markers = found.Where(one => one.Kind == MarkerFileKind.Marker).Select(one => one.Run).ToHashSet(StringComparer.Ordinal);
        var claims = found.Where(one => one.Kind == MarkerFileKind.Claim).ToList();
        var abandoned = claims.Where(claim => Old(claim, nowUtc, window)).Select(claim => claim.Run).ToHashSet(StringComparer.Ordinal);
        var held = claims.Select(claim => claim.Run).Where(run => !abandoned.Contains(run)).ToHashSet(StringComparer.Ordinal);

        return new(
            [.. found.Where(one => one is { Kind: MarkerFileKind.Marker, Marker: not null }
                && !held.Contains(one.Run)
                && ADeath(one.Marker, nowUtc, me, window, sameProcessAlive))],
            [.. claims.Where(claim => abandoned.Contains(claim.Run) && markers.Contains(claim.Run))],
            [.. found.Where(one => NothingWillReadIt(one, nowUtc, window, markers))]);
    }

    /// <summary>Another run's marker, silent past the window, and not provably alive on this machine.</summary>
    private static bool ADeath(
        RunMarker marker, DateTime nowUtc, RunMarker me, TimeSpan window, Func<int, DateTime, bool> sameProcessAlive) =>
        marker.Run != me.Run
        && nowUtc - marker.HeartbeatUtc > window
        && !AliveHere(marker, me.Host, sameProcessAlive);

    /// <summary>
    /// A stale marker from THIS machine whose process is provably still the one that wrote it.
    /// </summary>
    private static bool AliveHere(RunMarker marker, string host, Func<int, DateTime, bool> sameProcessAlive) =>
        string.Equals(marker.Host, host, StringComparison.OrdinalIgnoreCase)
        && sameProcessAlive(marker.Pid, marker.StartedUtc);

    /// <summary>
    /// An old file that is not a live marker: a claim whose death is recorded, a torn write, garbage.
    /// </summary>
    private static bool NothingWillReadIt(FoundMarker one, DateTime nowUtc, TimeSpan window, IReadOnlySet<string> markers) =>
        Old(one, nowUtc, window) && one.Kind switch
        {
            // A claim whose marker is gone: the death was recorded and the cleanup died.
            MarkerFileKind.Claim => !markers.Contains(one.Run),
            MarkerFileKind.Temporary => true,
            _ => one.Marker is null,
        };

    private static bool Old(FoundMarker one, DateTime nowUtc, TimeSpan window) => nowUtc - one.WrittenUtc > window;

    /// <summary>
    /// Claim a death, confirm it is still unrecorded, record it, and remove the marker only once the
    /// record landed.
    /// </summary>
    /// <remarks>
    /// <para><b>The invariant: holding the claim while the marker still exists means the death is
    /// unrecorded.</b> A recorder removes the marker BEFORE it releases the claim, so a claim won after
    /// somebody else finished finds the marker gone. The claim alone is not enough, and the race test
    /// is what said so: a start whose listing was taken before a sibling finished could win the
    /// released claim and record the same death again — 68 to 75 records for 50 deaths — because the
    /// claim protects only the moment it exists. So the marker is read again UNDER the claim.</para>
    /// <para>Read again, it must also still be stale: a laptop that slept past the window and woke
    /// between the listing and the claim has beaten since, and is not dead.</para>
    /// <para>An append that answers no RELEASES the claim and keeps the marker, so a later start retries
    /// rather than a death disappearing.</para>
    /// </remarks>
    private bool Recorded(RunMarker dead, Func<ServerNotice, bool> append)
    {
        if (!Created(ClaimOf(dead.Run)))
        {
            return false;
        }

        var landed = StillDead(dead.Run) is { } unrecorded && Written(UncleanExit.Of(unrecorded, clock()), append);
        if (landed)
        {
            Remove(MarkerOf(dead.Run));
        }

        Remove(ClaimOf(dead.Run));

        return landed;
    }

    /// <summary>
    /// The record, onto the page and into the log — an operator reading a terminal and a person
    /// reading the panel are different people (story 2.3.3's rule), so a death both of them can see.
    /// </summary>
    private bool Written(ServerNotice death, Func<ServerNotice, bool> append)
    {
        if (!append(death))
        {
            return false;
        }

        log.Warning("recorded a run that never finished: {Title}", death.Title);

        return true;
    }

    /// <summary>The marker as it is NOW, if it is still there and still silent past the window.</summary>
    private RunMarker? StillDead(string run) =>
        Parsed(MarkerOf(run)) is { } now && clock() - now.HeartbeatUtc > Stale ? now : null;

    /// <summary>The exclusive create: exactly one contender gets <c>true</c>.</summary>
    private bool Created(string claim)
    {
        try
        {
            using var claiming = new FileStream(claim, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            claiming.Write(System.Text.Encoding.UTF8.GetBytes(me.Run));

            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Already there is the ordinary case: another start claimed it first.
            log.Debug("run marker: {Claim} is held elsewhere: {Why}", Path.GetFileName(claim), failure.Message);

            return false;
        }
    }

    private void Remove(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            log.Warning("run marker: could not remove {Path}: {Why}", Path.GetFileName(path), failure.Message);
        }
    }

    /// <summary>Every marker, claim and temporary file in the directory.</summary>
    internal IReadOnlyList<FoundMarker> Read()
    {
        if (!Directory.Exists(Dir))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(Dir).Select(Found).OfType<FoundMarker>()];
    }

    private static FoundMarker? Found(string path)
    {
        var name = Path.GetFileName(path);
        var written = File.GetLastWriteTimeUtc(path);

        return KindOf(name) switch
        {
            MarkerFileKind.Temporary => new(path, MarkerFileKind.Temporary, name[..^TemporarySuffix.Length], null, written),
            MarkerFileKind.Claim => new(path, MarkerFileKind.Claim, name[..^ClaimSuffix.Length], null, written),
            MarkerFileKind.Marker => new(path, MarkerFileKind.Marker, name[..^MarkerSuffix.Length], Parsed(path), written),
            _ => null,
        };
    }

    /// <summary>What a file name says it is; anything else in the directory is not ours.</summary>
    private static MarkerFileKind? KindOf(string name) =>
        name.EndsWith(TemporarySuffix, StringComparison.Ordinal) ? MarkerFileKind.Temporary
        : name.EndsWith(ClaimSuffix, StringComparison.Ordinal) ? MarkerFileKind.Claim
        : name.EndsWith(MarkerSuffix, StringComparison.Ordinal) ? MarkerFileKind.Marker
        : null;

    private static RunMarker? Parsed(string path)
    {
        try
        {
            using var reading = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return JsonSerializer.Deserialize(reading, RunMarkerContext.Default.RunMarker);
        }
        catch (Exception failure) when (failure is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>A host start's identity.</summary>
internal static class RunIds
{
    /// <summary>
    /// Twelve hex characters from a cryptographic source — the extension's own shape
    /// (<c>randomBytes(6).toString('hex')</c> in <c>notify.ts</c>), so a run id reads the same whichever
    /// half wrote the record. Not the pid: pids are reused, and on a shared data directory two machines
    /// hand out the same numbers.
    /// </summary>
    internal static string New() =>
        Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6));
}

/// <summary>The record a start writes about a run that never finished.</summary>
internal static class UncleanExit
{
    /// <remarks>
    /// Its SUBJECT is the dead run's id, so each death is its own row on the page and a duplicate — the
    /// crash windows the class docstring owns — lands in the same row rather than a second one. Its
    /// <c>run</c> and <c>pid</c> are the DEAD run's: the record is about that run, and the marker is
    /// named for the same id every notice it wrote carries.
    /// </remarks>
    internal static ServerNotice Of(RunMarker dead, DateTime nowUtc) => new()
    {
        Utc = ServerNotice.Iso(nowUtc),
        Class = "failure",
        Source = "coai-mcp",
        Code = ServerNoticeCodes.UncleanExit,
        Subject = dead.Run,
        Run = dead.Run,
        Pid = dead.Pid,
        Title = ServerNotice.Shortened(
            $"a coai-mcp run on {dead.Host} (pid {dead.Pid}) that started {ServerNotice.Iso(dead.StartedUtc)} "
            + $"never finished — its last heartbeat was {ServerNotice.Iso(dead.HeartbeatUtc)}"),
    };
}
