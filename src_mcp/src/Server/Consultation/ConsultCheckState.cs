using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server;

/// <summary>The words a consultant check's state is written in — on disk, on stdout, and to the panel.</summary>
public static class ConsultCheckStates
{
    /// <summary>A check holds the lock and is running. Only ever on disk — stdout carries the END of a check.</summary>
    public const string Checking = "checking";

    /// <summary>The consultant answered; the record says whether it read the marker and what became of the canary.</summary>
    public const string Answered = "answered";

    /// <summary>The turn produced no advice — classified (<c>failureKind</c>, <c>failureWhat</c>, <c>failureCure</c>).</summary>
    public const string Failed = "failed";

    /// <summary>No turn could be had: the consultant is not configured or runnable, or the scratch repository could not be made.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>Another check of this caller kind holds the lock; nothing was launched.</summary>
    public const string AlreadyChecking = "already-checking";

    /// <summary>A <see cref="Checking"/> state whose lock nobody holds — the process that wrote it ended without finishing.</summary>
    public const string Abandoned = "abandoned";

    /// <summary>A state file exists and cannot be read — said as such, never confused with a kind nobody checked.</summary>
    public const string Unreadable = "unreadable";
}

/// <summary>What became of the canary — ONE of four, never a bare boolean (cadence consultation 435b1b25; the fourth since epic 4's code round).</summary>
public static class CanaryReadings
{
    /// <summary>Its word is in the answer: a read outside the repository LEAKED.</summary>
    public const string Read = "read";

    /// <summary>The CLI's own record shows a refusal — the only observed confinement.</summary>
    public const string DeniedByCli = "denied-by-cli";

    /// <summary>No word and no denial: the model did not try, or declined on its own — compliance, not confinement.</summary>
    public const string NotAttempted = "not-attempted";

    /// <summary>
    /// No word, but the CLI recorded refusing something it does not name (agy's <c>denied_actions</c> carry no input) — a
    /// refusal nobody can attribute to the canary, so NOT observed confinement.
    /// </summary>
    public const string DeniedByCliUnattributed = "denied-by-cli-unattributed";
}

/// <summary>
/// One consultant check of one caller kind: <c>&lt;dataDir&gt;/consultations/health/&lt;kind&gt;.check.json</c> while it
/// runs and after it ended, and the ONE document <c>--check-consultant</c> prints.
/// </summary>
/// <remarks>
/// Every string null-normalised in its accessor, as on <see cref="ConsultHealthAnswer"/>: the source-generated
/// deserializer skips initialisers, and a file another build wrote may lack a field this one knows (doctrine 4a).
/// </remarks>
public sealed record ConsultCheckRecord
{
    public string CallerKind { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>A word of <see cref="ConsultCheckStates"/>.</summary>
    public string State { get => field ?? string.Empty; init; } = string.Empty;

    public string StartedUtc { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// When the check will have ended whatever happens — its own deadline over the scratch, the probe and the turn.
    /// The panel derives its kill cap from this (E5.3), so it is never earlier than the truth.
    /// </summary>
    public string DeadlineUtc { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// Refreshed every 15 s by the holder: the liveness signal on the OTHER side of the Windows/WSL seam, and for a reader
    /// that cannot open the lock; on this side the lock decides (<see cref="ConsultCheckState.SettledAcross"/>).
    /// </summary>
    public string HeartbeatUtc { get => field ?? string.Empty; init; } = string.Empty;

    public string FinishedUtc { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// How long the OTHER side waits on a silent <see cref="HeartbeatUtc"/> before calling a <c>checking</c> abandoned —
    /// <see cref="ConsultCheckState.HeartbeatStaleAfter"/>, PUBLISHED with every record rather than kept by each reader.
    /// </summary>
    /// <remarks>
    /// Epic 5's plan round: the panel of a plain Windows window reads a WSL store's record through
    /// <c>coai.alsoWatchDataDirectories</c> and judges its heartbeat with this number, never with a constant of its own —
    /// a second copy of the margin would drift from <see cref="ConsultCheckState.SettledAcross"/>. Computed, so a record
    /// read from an older file and written again carries the margin of the build writing it; a reader meeting a file
    /// WITHOUT it (a server from before) says it cannot tell whether the check is still running.
    /// </remarks>
    public int HeartbeatStaleAfterSeconds => (int)ConsultCheckState.HeartbeatStaleAfter.TotalSeconds;

    /// <summary><c>windows</c>, <c>wsl</c>, <c>linux</c>, <c>macos</c> or <c>other</c> — which side ran it.</summary>
    public string Side { get => field ?? string.Empty; init; } = string.Empty;

    public string Vendor { get => field ?? string.Empty; init; } = string.Empty;

    public string Runtime { get => field ?? string.Empty; init; } = string.Empty;

    public string Model { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>What the turn was SENT (<c>restricted</c> / <c>no-restricted</c> for claude), empty for any other vendor.</summary>
    public string Confinement { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>The consultant answered something.</summary>
    public bool Answered { get; init; }

    /// <summary>The answer carries the marker word of <c>CHECK.md</c> — the file was READ, not guessed.</summary>
    public bool MarkerRead { get; init; }

    /// <summary>
    /// For an api row that asked for a stream (research/PLAN_api_streaming.md, Story C): <c>streamed</c> when the answer came as
    /// one, <c>not-streamed</c> when it did not — a gateway that ignored the request, or a coai-mcp too old to read the
    /// switch. Empty for every other check.
    /// </summary>
    public string Streamed { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>A word of <see cref="CanaryReadings"/>, or empty when the consultant answered nothing.</summary>
    public string Canary { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>The permission words the CLI recorded refusing (<see cref="Runners.Consultation.IConsultantRuntime.DeniedActions"/>).</summary>
    public IReadOnlyList<string> DeniedActions { get => field ?? []; init; } = [];

    public double Seconds { get; init; }

    public long TokensIn { get; init; }

    public long TokensOut { get; init; }

    /// <summary>A word of <c>shared/consult-failure-kinds.json</c> when the turn failed; empty otherwise.</summary>
    public string FailureKind { get => field ?? string.Empty; init; } = string.Empty;

    public string FailureWhat { get => field ?? string.Empty; init; } = string.Empty;

    public string FailureCure { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>Where a failed turn's transcript was kept — a path on THIS side's filesystem.</summary>
    public string Evidence { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>Why a check was unavailable, already checking or abandoned — a sentence for a person.</summary>
    public string Reason { get => field ?? string.Empty; init; } = string.Empty;
}

/// <summary>The rules a reader judges a check's liveness by — the lock on its own side, the heartbeat across the seam.</summary>
public static class ConsultCheckState
{
    /// <summary>How often the holder refreshes <see cref="ConsultCheckRecord.HeartbeatUtc"/>.</summary>
    public static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long the OTHER side waits on a silent heartbeat before calling the check abandoned — four missed beats, so a
    /// slow disk or a busy scheduler on the holder's side is not mistaken for a dead process.
    /// </summary>
    public static readonly TimeSpan HeartbeatStaleAfter = HeartbeatEvery * 4;

    /// <summary>
    /// The record as a reader ON ITS OWN SIDE must show it: a <see cref="ConsultCheckStates.Checking"/> whose lock is
    /// FREE was left by a process that ended without finishing — <see cref="ConsultCheckStates.Abandoned"/>.
    /// Everything else is what it says.
    /// </summary>
    /// <remarks>
    /// On its own side the lock is the whole question, asked by the reader itself (a non-blocking exclusive open): a
    /// holder that is alive holds it, however long it has been quiet, and a killed one released it with its handle.
    /// That holds ONLY on the side that took it: on Linux the lock is an advisory <c>flock</c>, and it does not cross
    /// the WSL/Windows 9P boundary — from the other side it always looks free. Across the seam the reader uses
    /// <see cref="SettledAcross"/> instead, and <see cref="ConsultCheckStore.Current"/> chooses (coai code round of
    /// epic 4, 2026-10-03).
    /// </remarks>
    public static ConsultCheckRecord Settled(ConsultCheckRecord record, bool lockHeld) =>
        record.State == ConsultCheckStates.Checking && !lockHeld
            ? Abandoned(record, "the check's process ended without finishing — its lock is free, so nothing is running it")
            : record;

    /// <summary>
    /// <see cref="Settled(ConsultCheckRecord, bool)"/> from a probe — and when the lock is FREE, the state read AGAIN
    /// first: a check that finished between the first read and the probe shows its result, never a flash of
    /// <see cref="ConsultCheckStates.Abandoned"/> (epic 4's code round).
    /// </summary>
    /// <remarks>
    /// <see cref="LockProbe.Unknown"/> — a reader that cannot open the lock — says nothing about liveness, so the record
    /// is returned as it is; <see cref="ConsultCheckStore.Current"/> judges that case by the heartbeat instead.
    /// </remarks>
    public static ConsultCheckRecord Settled(ConsultCheckRecord record, LockProbe probe, Func<ConsultCheckRecord?> reread) =>
        (record.State, probe) switch
        {
            (ConsultCheckStates.Checking, LockProbe.Free) => reread() is { State: not ConsultCheckStates.Checking } finished
                ? finished
                : Settled(record, lockHeld: false),
            _ => record,
        };

    /// <summary>
    /// The record as a reader on the OTHER side of the seam must show it: a <see cref="ConsultCheckStates.Checking"/>
    /// whose heartbeat has been silent longer than <see cref="HeartbeatStaleAfter"/> — or that carries none a reader can
    /// parse — is <see cref="ConsultCheckStates.Abandoned"/>.
    /// </summary>
    /// <remarks>
    /// <para>It is the second-best signal, used only where the lock cannot be seen — across the seam, or by a reader that
    /// cannot open the lock at all.</para>
    /// <para><b>It compares two clocks, and they are NOT one</b> (the whole-branch review, O; plan §2 item 11). A WSL
    /// distribution keeps its own clock, which drifts from the Windows host's — after a sleep or a suspended VM, by more
    /// than this four-beat margin — so a server on one side judging the other side's stamp with its own
    /// <see cref="DateTime.UtcNow"/> can call a live check abandoned, or a dead one alive. A one-shot server mode reads the
    /// file once and cannot see whether the beat MOVES, so it has nothing better than this comparison; that is the
    /// residual. The panel, which polls, does not use this rule across the seam: it judges a stamp by its OWN elapsed time
    /// since it last saw the heartbeat change (<c>consultantHealthWatcher.ts</c>), which no skew can move.</para>
    /// </remarks>
    public static ConsultCheckRecord SettledAcross(ConsultCheckRecord record, DateTime nowUtc) =>
        record.State == ConsultCheckStates.Checking && !BeatSince(record.HeartbeatUtc, nowUtc - HeartbeatStaleAfter)
            ? Abandoned(record, $"the check's side stopped refreshing its heartbeat (last {Last(record.HeartbeatUtc)}), so nothing is running it")
            : record;

    /// <summary>Whether the record was written on THIS side — a record from before sides were written counts as this side's.</summary>
    public static bool IsThisSide(ConsultCheckRecord record, string thisSide) =>
        record.Side.Length == 0 || string.Equals(record.Side, thisSide, StringComparison.Ordinal);

    /// <summary>A check another side is running RIGHT NOW, by its heartbeat — the one this side must not start beside.</summary>
    public static bool RunningOnTheOtherSide(ConsultCheckRecord record, string thisSide, DateTime nowUtc) =>
        !IsThisSide(record, thisSide) && SettledAcross(record, nowUtc).State == ConsultCheckStates.Checking;

    private static bool BeatSince(string heartbeatUtc, DateTime since) =>
        DateTime.TryParse(heartbeatUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var beat)
        && beat >= since;

    private static string Last(string heartbeatUtc) => heartbeatUtc.Length > 0 ? heartbeatUtc : "never";

    private static ConsultCheckRecord Abandoned(ConsultCheckRecord record, string reason) =>
        record with { State = ConsultCheckStates.Abandoned, Reason = reason };
}

/// <summary>What is on disk for one caller kind's check: nothing, a state, or a file that cannot be read.</summary>
public abstract record CheckOnDisk
{
    /// <summary>No check of this kind was ever recorded.</summary>
    public sealed record None : CheckOnDisk;

    public sealed record Found(ConsultCheckRecord Record) : CheckOnDisk;

    /// <summary>A state file exists and cannot be read or parsed — said, never mistaken for <see cref="None"/>.</summary>
    public sealed record Unreadable(string Why) : CheckOnDisk;

    private CheckOnDisk() { }
}

/// <summary>
/// Where a caller kind's check state and lock live — <c>&lt;dataDir&gt;/consultations/health/&lt;kind&gt;.check.{json,lock}</c>
/// — read by anyone, written only by the holder of the lock.
/// </summary>
/// <remarks>
/// Per caller kind, never one shared file (epic 4's plan round): two kinds may be checked at once. Beside the health
/// files, in the directory <c>ConsultationStore.All</c> and the panel's <c>consultations/*.json</c> watcher do not
/// list. Written through <see cref="AtomicFile"/>, so a reader never meets half a document; an orphaned temporary is
/// retired by <see cref="ConsultationRetention"/>'s hour rule like any other in this directory.
/// </remarks>
public sealed class ConsultCheckStore(string dataDir)
{
    /// <summary>How long a move over a file a reader holds is retried — the panel polls these files (<see cref="ConsultHealthPaths.SharingRetry"/>).</summary>
    public static readonly TimeSpan SharingRetry = ConsultHealthPaths.SharingRetry;

    public string Directory => ConsultHealthPaths.HealthDirectory(dataDir);

    public string StatePath(string callerKind) => Path.Combine(Directory, $"{Core.Rounds.FileName.Safe(callerKind)}.check.json");

    public string LockPath(string callerKind) => Path.Combine(Directory, $"{Core.Rounds.FileName.Safe(callerKind)}.check.lock");

    /// <summary>What is on disk for this caller kind — never a null that could mean either "never" or "unreadable".</summary>
    public CheckOnDisk Look(string callerKind)
    {
        var path = StatePath(callerKind);
        try
        {
            return File.Exists(path) ? Parsed(SharedRead.Text(path), path) : new CheckOnDisk.None();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new CheckOnDisk.Unreadable($"{path} could not be read: {e.Message}");
        }
    }

    /// <summary>The state as it was written — a convenience over <see cref="Look"/> for a caller to whom "none" and "unreadable" are one.</summary>
    public ConsultCheckRecord? Read(string callerKind) => Look(callerKind) is CheckOnDisk.Found found ? found.Record : null;

    /// <summary>
    /// The state as a reader must show it, or null when no check was ever recorded: judged by the lock when the record
    /// was written on this side and this reader can open the lock, by its heartbeat when it came from the other side of
    /// the Windows/WSL seam or this reader cannot open the lock (<see cref="ConsultCheckState.SettledAcross"/>), and as
    /// <see cref="ConsultCheckStates.Unreadable"/> when the file will not read.
    /// </summary>
    public ConsultCheckRecord? Current(string callerKind) => Judge(callerKind) switch
    {
        CheckOnDisk.Found found => found.Record,
        CheckOnDisk.Unreadable unreadable => new ConsultCheckRecord { CallerKind = callerKind, State = ConsultCheckStates.Unreadable, Reason = unreadable.Why },
        _ => null,
    };

    /// <summary><see cref="Look"/>, with a record that was found JUDGED as <see cref="Current"/> judges it — the union, for a caller that keeps the three cases apart.</summary>
    public CheckOnDisk Judge(string callerKind) => Look(callerKind) switch
    {
        CheckOnDisk.Found found => new CheckOnDisk.Found(Judged(found.Record, callerKind)),
        var other => other,
    };

    private ConsultCheckRecord Judged(ConsultCheckRecord record, string callerKind)
    {
        var probe = ConsultCheckState.IsThisSide(record, ConsultHealth.Side()) ? HeldFile.Probe(LockPath(callerKind)) : LockProbe.Unknown;

        return probe == LockProbe.Unknown
            ? ConsultCheckState.SettledAcross(record, DateTime.UtcNow)
            : ConsultCheckState.Settled(record, probe, () => Read(callerKind));
    }

    /// <summary>Writes the state. Called by the holder of the lock, and by nobody else.</summary>
    public void Write(ConsultCheckRecord record) => Write(record, SharingRetry);

    /// <summary>Writes the state, retrying a move over a held file for <paramref name="sharingRetry"/>.</summary>
    public void Write(ConsultCheckRecord record, TimeSpan sharingRetry)
    {
        System.IO.Directory.CreateDirectory(Directory);
        AtomicFile.Write(StatePath(record.CallerKind), Serialize(record), sharingRetry);
    }

    /// <summary>The document, as the file and stdout both carry it.</summary>
    public static string Serialize(ConsultCheckRecord record) =>
        JsonSerializer.Serialize(record, ConsultCheckJsonContext.Default.ConsultCheckRecord);

    /// <summary>
    /// Rewrites every <see cref="ConsultCheckStates.Checking"/> state of THIS side whose lock is free as
    /// <see cref="ConsultCheckStates.Abandoned"/> — TAKING the lock to do it, so the rule "only the holder writes"
    /// holds for the sweep too — and answers how many.
    /// </summary>
    /// <remarks>
    /// A file that cannot be read or written is logged and skipped: the consultation sweep this rides must never
    /// stop over a check (E4.4). Another side's check is never settled here: its lock cannot be seen from this side, so
    /// taking it would "succeed" while the check runs (coai code round of epic 4, 2026-10-03).
    /// </remarks>
    public int SettleAbandoned(DateTime nowUtc, Action<string> warn) =>
        CallerIdentity.Kinds.Count(kind => Abandoned(kind, nowUtc, warn));

    private bool Abandoned(string callerKind, DateTime nowUtc, Action<string> warn) =>
        Read(callerKind) is { State: ConsultCheckStates.Checking } record
        && ConsultCheckState.IsThisSide(record, ConsultHealth.Side())
        && RewrittenUnderTheLock(callerKind, nowUtc, warn);

    private bool RewrittenUnderTheLock(string callerKind, DateTime nowUtc, Action<string> warn)
    {
        try
        {
            using var held = HeldFile.TryHold(LockPath(callerKind));

            return held is not null && Rewritten(callerKind, nowUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"consultant check: the abandoned {callerKind} check could not be rewritten, and the next sweep will try again: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Re-read UNDER the lock — a check may have finished between the first read and the take — and written with NO
    /// sharing retry: the sweep holds the lock a check would take, and must let go well inside that check's half-second
    /// acquisition (epic 4's code round).
    /// </summary>
    private bool Rewritten(string callerKind, DateTime nowUtc)
    {
        if (Read(callerKind) is not { State: ConsultCheckStates.Checking } stranded)
        {
            return false;
        }

        Write(ConsultCheckState.Settled(stranded, lockHeld: false) with { FinishedUtc = ConsultationStore.Stamp(nowUtc) }, TimeSpan.Zero);

        return true;
    }

    private static CheckOnDisk Parsed(string text, string path)
    {
        try
        {
            return JsonSerializer.Deserialize(text, ConsultCheckJsonContext.Default.ConsultCheckRecord) is { } record
                ? new CheckOnDisk.Found(record)
                : new CheckOnDisk.Unreadable($"{path} holds no state");
        }
        catch (JsonException e)
        {
            return new CheckOnDisk.Unreadable($"{path} does not parse: {e.Message}");
        }
    }
}

/// <summary>
/// The check's lock: <c>&lt;kind&gt;.check.lock</c> HELD open with <see cref="FileShare.None"/> for the whole check.
/// </summary>
/// <remarks>
/// <para><b>The atomic operation is the exclusive open</b> (<see cref="HeldFile"/>) — the <c>SessionTurn</c> /
/// <c>EngineLease</c> shape, redesigned into this by the risk consultation (faa596bf, 2026-10-03): the
/// heartbeat-fenced lock first planned had a check-then-act gap — a process paused between verifying its id and
/// the paid launch would wake, launch a second time and overwrite the newer result, and a file cannot fence a
/// process's memory. Here nobody breaks a lock and nothing is renamed: the kernel releases it when the holder
/// dies, and a paused holder simply goes on holding it. A second check whose open fails answers
/// <c>already-checking</c>; there is no second paid launch.</para>
/// <para><b>The acquisition waits half a second</b>, because a reader judging liveness holds the file for the length
/// of one open (<see cref="HeldFile.Probe"/>) and must not be mistaken for a holder; a real holder holds for
/// minutes. A lock file that cannot be opened at all is NOT "already checking": it throws, and the mode answers 74.</para>
/// <para><b>The residual.</b> On Linux the lock is an ADVISORY <c>flock</c>: honoured between .NET processes, not
/// across the WSL/Windows 9P boundary. So on THIS side the lock decides liveness; on the OTHER side — a window reading
/// this side's store through <c>\\wsl$</c>, where the lock always looks free — the <c>heartbeatUtc</c> the holder
/// refreshes is the liveness signal (<see cref="ConsultCheckState.SettledAcross"/>). One data directory SHARED by both
/// sides (a WSL server pointed at a <c>/mnt/c</c> path) is not supported for checks: the two sides' locks cannot see
/// each other, and the one thing done about it is that a check refuses to start beside a fresh <c>checking</c> record
/// the other side wrote (<see cref="ConsultCheckState.RunningOnTheOtherSide"/>) — a narrow window remains between that
/// read and the other side's first write.</para>
/// </remarks>
public sealed class ConsultCheckLock : IDisposable
{
    private static readonly TimeSpan ReaderGrace = TimeSpan.FromMilliseconds(500);

    private readonly FileStream _held;

    private ConsultCheckLock(FileStream held) => _held = held;

    /// <summary>The lock, or null when another check of this caller kind holds it.</summary>
    /// <exception cref="IOException">The health directory or the lock file cannot be opened — the data directory is unusable.</exception>
    public static ConsultCheckLock? TryTake(ConsultCheckStore store, string callerKind)
    {
        System.IO.Directory.CreateDirectory(store.Directory);

        return HeldFile.TakeWithin(store.LockPath(callerKind), ReaderGrace) switch
        {
            Hold.Taken taken => new ConsultCheckLock(taken.Stream),
            Hold.Unusable unusable => throw new IOException(
                $"the check's lock {store.LockPath(callerKind)} cannot be opened: {unusable.Cause.Message}", unusable.Cause),
            _ => null,
        };
    }

    public void Dispose() => _held.Dispose();
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ConsultCheckRecord))]
internal sealed partial class ConsultCheckJsonContext : JsonSerializerContext;
