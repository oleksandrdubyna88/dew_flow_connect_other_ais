using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Kimi;

/// <summary>What reconciling a standing survivor marker came to — a closed union.</summary>
public abstract record SurvivorVerdict
{
    private SurvivorVerdict()
    {
    }

    /// <summary>There is no marker: nothing survived the previous turn.</summary>
    public sealed record None : SurvivorVerdict;

    /// <summary>A marker stood, and every process it named is confirmed gone: the file is deleted and the launch may proceed.</summary>
    public sealed record Cleared(KimiSurvivorMarkerDto Was) : SurvivorVerdict;

    /// <summary>
    /// The sign-in stays unavailable: <paramref name="Code"/> is a <see cref="BlockedReasons"/> code, <paramref name="Detail"/> what could not be ended or checked. <paramref name="ByOwner"/> is true when the answer
    /// is about the shim that WROTE the marker (alive, or not checkable) rather than about a process the marker names.
    /// </summary>
    public sealed record Blocked(KimiSurvivorMarkerDto Marker, string MarkerPath, string Code, string Detail, bool ByOwner = false) : SurvivorVerdict;
}

/// <summary>The process control a reconcile uses, as seams: the product's (by identity through a pidfd on Linux) or a test's.</summary>
internal sealed record ReconcileSeams(
    Func<ProcessIdentity, bool> Terminate,
    Func<IReadOnlyList<ProcessIdentity>, Task<IReadOnlyList<ProcessIdentity>>> ConfirmGone,
    Func<ProcessIdentity, IReadOnlyList<ProcessIdentity>> Descendants,
    Func<ProcessIdentity, Liveness> Liveness)
{
    /// <summary>
    /// This host's: on Linux every signal goes through a pidfd (<see cref="TreeSignals"/>), never by number; elsewhere <see cref="ProcessTree"/>'s own kill by handle (markers are written on Linux only, so
    /// this is reached on Windows only by a marker somebody carried there).
    /// </summary>
    public static ReconcileSeams Real { get; } = new(
        OperatingSystem.IsLinux() ? EndedByIdentity : ProcessTree.Terminate,
        members => ProcessTree.ConfirmGoneAsync(members, OperatingSystem.IsLinux() ? EndedByIdentity : ProcessTree.Terminate),
        ProcessTree.DescendantsOf,
        LivenessCheck.Of);

    private static bool EndedByIdentity(ProcessIdentity member) =>
        OperatingSystem.IsLinux() && TreeSignals.Kill([member], PidfdLayer.Calls).Any(signalled => signalled is Signalled.Sent);
}

/// <summary>
/// What a standing survivor marker does to the next launch (DL9 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): it is reconciled FIRST — terminate by identity, confirm, then ask every member the
/// tri-state question — and only a confirmed-empty tree clears it. Anything else keeps the marker and makes the launch Busy, with the way out in the sentence.
/// </summary>
/// <remarks>
/// <para>Called by the server's gate (<c>KimiLaunch.TakeAsync</c>), by the shim's tree hold (<c>KimiShimHold.Taken</c>) — the third place, because the gate dies with the server — and, as a standing
/// marker, by <c>KimiHome.Alive</c>.</para>
/// <para><b>A marker that names nothing</b> (phase <c>spawning</c>: the shim died between writing it and starting the agent) is cleared only when the machine's process list is READABLE and holds no
/// <c>kimi</c> at all — the orphaned <c>kimi acp</c> such a marker stands for has no <c>coai-mcp</c> in its chain and exits within about 0.1 s of its stdin's EOF (L-K4). A recorded deviation from the
/// <see cref="ReaderReconcile"/> precedent, which blocks a nameless marker for ever.</para>
/// </remarks>
public static class KimiSurvivorGate
{
    /// <summary>None when no file; else the reconcile of the marker — Cleared deletes the file, Blocked keeps it.</summary>
    public static Task<SurvivorVerdict> ReconcileAsync(string markerPath, IProcessList listing) =>
        ReconcileAsync(markerPath, () => KimiPresence.Of(listing), ReconcileSeams.Real);

    /// <summary>The same with every seam handed in — what the tests drive.</summary>
    internal static async Task<SurvivorVerdict> ReconcileAsync(string markerPath, Func<KimiPresence> presence, ReconcileSeams seams) =>
        KimiSurvivorMarker.Read(markerPath) switch
        {
            MarkerRead.Absent => new SurvivorVerdict.None(),
            MarkerRead.Unreadable unreadable => new SurvivorVerdict.Blocked(
                KimiSurvivorMarkerDto.Unreadable, markerPath, BlockedReasons.ReaderMemberUnverifiable, $"the marker file could not be read ({unreadable.Reason})"),
            MarkerRead.Read read => await AfterTheOwnerAsync(read.Marker, markerPath, presence, seams),
            _ => new SurvivorVerdict.None(),
        };

    /// <summary>
    /// The shim that wrote the marker is asked FIRST (the E5 security judge, 2026-10-17): alive → its turn is not over and nothing it names is signalled — Blocked, to WAIT; not checkable → Blocked, unverifiable;
    /// gone → the orphan fail-safe, today's reconcile. The owner is a pid AND a start time, so a reused number reads as gone.
    /// </summary>
    private static async Task<SurvivorVerdict> AfterTheOwnerAsync(KimiSurvivorMarkerDto marker, string path, Func<KimiPresence> presence, ReconcileSeams seams) =>
        OwnerLiveness(marker, seams.Liveness) switch
        {
            Liveness.Alive => new SurvivorVerdict.Blocked(marker, path, BlockedReasons.ReaderOwnerAlive, marker.Marker.OwnerPid.ToString(System.Globalization.CultureInfo.InvariantCulture), ByOwner: true),
            Liveness.Unknown unknown => new SurvivorVerdict.Blocked(
                marker, path, BlockedReasons.ReaderMemberUnverifiable, $"the coai shim pid {marker.Marker.OwnerPid} that wrote it could not be checked ({unknown.Reason})", ByOwner: true),
            _ => marker.Marker.Unidentified
                ? Judged(marker, path, presence())
                : Judged(marker, path, await ReaderReconcile.RunAsync(marker.Marker, seams.Terminate, seams.ConfirmGone, seams.Descendants, seams.Liveness)),
        };

    /// <summary>The liveness of the shim that wrote <paramref name="marker"/>; Gone for a marker that records no owner (nothing to wait for).</summary>
    public static Liveness OwnerLiveness(KimiSurvivorMarkerDto marker, Func<ProcessIdentity, Liveness> liveness) =>
        marker.Marker.OwnerPid > 0 ? liveness(new ProcessIdentity(marker.Marker.OwnerPid, marker.Marker.OwnerStartedUtc)) : new Liveness.Gone("the marker records no owner");

    private static SurvivorVerdict Judged(KimiSurvivorMarkerDto marker, string path, Reconciled reconciled) => reconciled switch
    {
        Reconciled.Cleared => Deleted(marker, path),
        Reconciled.Blocked blocked => new SurvivorVerdict.Blocked(marker, path, blocked.Code, blocked.Detail),
        _ => new SurvivorVerdict.Blocked(marker, path, BlockedReasons.ReaderMemberUnverifiable, "an answer this gate has no word for"),
    };

    /// <summary>The nameless marker: cleared only over a readable listing with no kimi in it.</summary>
    private static SurvivorVerdict Judged(KimiSurvivorMarkerDto marker, string path, KimiPresence presence) => presence switch
    {
        KimiPresence.NoKimi => Deleted(marker, path),
        KimiPresence.Running running => new SurvivorVerdict.Blocked(marker, path, BlockedReasons.ReaderIdentityMissing, string.Join(", ", running.Pids)),
        KimiPresence.Unreadable unreadable => new SurvivorVerdict.Blocked(marker, path, BlockedReasons.ReaderMemberUnverifiable, unreadable.Reason),
        _ => new SurvivorVerdict.Blocked(marker, path, BlockedReasons.ReaderMemberUnverifiable, "an answer this gate has no word for"),
    };

    /// <summary>The file deleted — a failure to delete keeps the marker (fail closed), saying why.</summary>
    private static SurvivorVerdict Deleted(KimiSurvivorMarkerDto marker, string path)
    {
        try
        {
            KimiSurvivorMarker.Clear(path);

            return new SurvivorVerdict.Cleared(marker);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SurvivorVerdict.Blocked(marker, path, BlockedReasons.ReaderMemberUnverifiable, $"the marker could not be deleted ({e.Message})");
        }
    }

    /// <summary>
    /// The Busy sentence for a blocked verdict (A3 §6.5): the marker's FULL path, the pid(s) and "safe to delete once …" come FIRST — asserted with no button in play, as an older extension would show it —
    /// and the card's button is named as an addition.
    /// </summary>
    public static string Sentence(SurvivorVerdict.Blocked blocked) => blocked switch
    {
        { Code: BlockedReasons.ReaderOwnerAlive } => OwnerAlive(blocked),
        { ByOwner: true } => Unverifiable(blocked),
        _ when IsNameless(blocked) => Unidentified(blocked),
        { Code: BlockedReasons.ReaderMemberUnverifiable } => Unverifiable(blocked),
        _ => Alive(blocked),
    };

    private static string OwnerAlive(SurvivorVerdict.Blocked blocked) =>
        $"a Kimi turn is still running under coai shim pid {blocked.Detail} — it ends when its agent does; the turn is given up. coai does not start another Kimi on this sign-in while that turn runs, and it never ends another turn's agent "
        + $"(the marker {blocked.MarkerPath} is that turn's own and is removed when it ends cleanly)";

    /// <summary>A marker that names no process — but not the one a file that could not be read stands for, whose sentence is the unverifiable one.</summary>
    private static bool IsNameless(SurvivorVerdict.Blocked blocked) =>
        blocked.Marker.Marker.Unidentified && blocked.Marker != KimiSurvivorMarkerDto.Unreadable && blocked.Code != BlockedReasons.ReaderTerminationFailed;

    private static string Pids(KimiSurvivorMarkerDto marker) => string.Join(", ", marker.Marker.Tree.Select(member => member.Pid));

    private static string Alive(SurvivorVerdict.Blocked blocked)
    {
        var first = blocked.Marker.Marker.Tree.Count > 0 ? blocked.Marker.Marker.Tree[0] : new ProcessIdentity(0, default);

        return $"a Kimi started by coai on this sign-in may still be running: the previous turn could not confirm that pid {first.Pid} (started {first.StartedUtc:yyyy-MM-dd HH:mm:ss} UTC) ended, "
            + $"and it is recorded in {blocked.MarkerPath}. coai does not start another Kimi on this sign-in while that stands — two refreshers of one rotating token would sign you out. "
            + $"The file is safe to delete once those processes are gone (check with `ps -p {Pids(blocked.Marker)}`). The Kimi card's *Clear the stuck Kimi marker* button does the same check "
            + "and deletes it only when every process named is gone (DL9 of todo/PLAN_kimi_and_vibe_on_linux.md)";
    }

    private static string Unverifiable(SurvivorVerdict.Blocked blocked) =>
        $"a Kimi started by coai on this sign-in may still be running: {blocked.Detail}, so coai cannot tell whether it ended; it is recorded in {blocked.MarkerPath}. "
        + "coai does not start another Kimi on this sign-in while that stands. "
        + $"The file is safe to delete once you have checked that those processes are gone{PsHint(blocked.Marker)}. "
        + "The Kimi card's *Clear the stuck Kimi marker* button refuses while a process cannot be checked (DL9 of todo/PLAN_kimi_and_vibe_on_linux.md)";

    private static string PsHint(KimiSurvivorMarkerDto marker) => marker.Marker.Tree.Count == 0 ? string.Empty : $" (`ps -p {Pids(marker)}`)";

    private static string Unidentified(SurvivorVerdict.Blocked blocked)
    {
        var why = blocked.Code == BlockedReasons.ReaderIdentityMissing
            ? $", and this machine runs a kimi (pid {blocked.Detail})"
            : $", and this machine's process list could not be read ({blocked.Detail})";

        return $"a Kimi started by coai on this sign-in may still be running: the previous turn ended between writing {blocked.MarkerPath} and starting Kimi, so the marker names no process{why}. "
            + "coai does not start another Kimi on this sign-in while that stands. It is cleared automatically once no kimi runs here; the file is safe to delete once you are sure no Kimi started by coai "
            + "is running. The Kimi card's *Clear the stuck Kimi marker* button does the same check (DL9 of todo/PLAN_kimi_and_vibe_on_linux.md)";
    }
}
