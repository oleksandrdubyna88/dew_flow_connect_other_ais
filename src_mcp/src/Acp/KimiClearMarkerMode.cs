using System.Text.RegularExpressions;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Acp;

/// <summary>
/// <c>coai-mcp --kimi-clear-marker &lt;path&gt;</c> — the card's <i>Clear the stuck Kimi marker</i> (DL9 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>, S-K18): one survivor marker under the Kimi lock root,
/// deleted only when every process it names is gone. It signals NOTHING, ever — a person who wants a process ended ends it, then presses again.
/// </summary>
/// <remarks>
/// <para><b>Exit codes (fixed, and never 64 — 64 means "never heard of this mode"):</b> 0 cleared, or no marker; 65 a request fault — no path, or a path that is not a survivor marker directly under the
/// lock root (nothing else is ever deleted); 75 a named member still runs (<i>Alive</i> beats <i>Unknown</i>: a living survivor is the stronger fact); 74 a member could not be checked, the marker could
/// not be read, or the process list of a marker that names nothing could not be read, or the sign-in's gate could not be taken in time, or the marker changed while it was judged — a content fault is "cannot verify", not an argument fault.</para>
/// <para><b>Read, check and delete are one step under the sign-in's gate</b> (D of the review of 2026-10-18): the gate (<see cref="KimiCredentialsLock.TakeAsync"/>) is the lock a launch holds from before its version probe until the launch
/// has been read, so a turn cannot start between the mode's read and its delete; the marker is read AGAIN under the gate, and deleted only if the file still holds the marker that was judged
/// (<see cref="KimiSurvivorMarker.Holds"/>). A sign-in folder that has no key (it is gone) has no gate to take — nothing can be launching on it — and is judged without one.</para>
/// <para>stdout carries one JSON object for the panel (<see cref="ClearMarkerReport"/>); stderr the sentence. Not a writer mode: it never touches the data directory.</para>
/// </remarks>
internal static partial class KimiClearMarkerMode
{
    internal const int Ok = 0;
    internal const int BadRequest = 65;
    internal const int Unverifiable = 74;
    internal const int StillRunning = 75;

    private const string Mode = "--kimi-clear-marker";

    /// <summary>With this host's lock root, liveness and process list.</summary>
    internal static Task<int> RunAsync(string[] args, Action<string> note, TextWriter output) =>
        RunAsync(args, note, output, KimiCredentialsLock.Root, LivenessCheck.Of, HostProcessList.Current);

    /// <summary>With the process list as a listing — the form the plan names.</summary>
    internal static Task<int> RunAsync(string[] args, Action<string> note, TextWriter output, string lockRoot, Func<ProcessIdentity, Liveness> liveness, IProcessList listing) =>
        RunAsync(args, note, output, lockRoot, liveness, () => KimiPresence.Of(listing));

    /// <summary>How long the sign-in's gate is waited for: a launch holds it while it asks <c>kimi --version</c> (up to <see cref="KimiLaunch.VersionTimeout"/>), and the button's person should not wait that long to be told to press again.</summary>
    internal static readonly TimeSpan GateWait = TimeSpan.FromSeconds(20);

    /// <summary>With every seam handed in, and the product's wait for the gate.</summary>
    internal static Task<int> RunAsync(
        string[] args, Action<string> note, TextWriter output, string lockRoot, Func<ProcessIdentity, Liveness> liveness, Func<KimiPresence> presence) =>
        RunAsync(args, note, output, lockRoot, liveness, presence, GateWait);

    /// <summary>With every seam handed in.</summary>
    internal static async Task<int> RunAsync(
        string[] args, Action<string> note, TextWriter output, string lockRoot, Func<ProcessIdentity, Liveness> liveness, Func<KimiPresence> presence, TimeSpan gateWait)
    {
        if (Refusal(args, lockRoot) is { Length: > 0 } refused)
        {
            note(refused);

            return BadRequest;
        }

        var path = Path.GetFullPath(args[1]);
        using var rooted = KimiCredentialsLock.RootFor(lockRoot);
        var (exit, report) = await UnderTheGateAsync(path, liveness, presence, note, gateWait);
        await output.WriteLineAsync(KimiSurvivorMarker.Json(report));

        return exit;
    }

    /// <summary>The marker read, checked and deleted with the sign-in's gate held — or kept, with the gate's own sentence, when it cannot be taken in time.</summary>
    private static async Task<(int Exit, ClearMarkerReport Report)> UnderTheGateAsync(
        string path, Func<ProcessIdentity, Liveness> liveness, Func<KimiPresence> presence, Action<string> note, TimeSpan gateWait)
    {
        // The first read only learns WHICH sign-in's gate guards the file; the judging reads it again, under the gate.
        using var gate = await GateAsync(KimiSurvivorMarker.Read(path), gateWait);

        return gate is Gate.Refused refused
            ? Said(note, $"{path} is kept: {refused.Sentence}", Unverifiable, new ClearMarkerReport(path, "kept", Unverifiable, []))
            : Cleared(path, KimiSurvivorMarker.Read(path), liveness, presence, note);
    }

    /// <summary>What taking the sign-in's gate came to: held, not needed (no marker, or a folder with no key), or refused with the sentence.</summary>
    private abstract record Gate : IDisposable
    {
        public virtual void Dispose()
        {
        }

        public sealed record Held(FileStream Stream) : Gate
        {
            public override void Dispose() => Stream.Dispose();
        }

        public sealed record Unneeded : Gate;

        public sealed record Refused(string Sentence) : Gate;
    }

    private static async Task<Gate> GateAsync(MarkerRead first, TimeSpan wait)
    {
        if (first is not MarkerRead.Read { Marker.SignInFolder: var signIn } || KimiCredentialsLock.PathFor(signIn).Length == 0)
        {
            return new Gate.Unneeded();
        }

        using var timeout = new CancellationTokenSource(wait);
        try
        {
            return await KimiCredentialsLock.TakeAsync(signIn, timeout.Token) switch
            {
                Hold.Taken taken => new Gate.Held(taken.Stream),
                Hold.Unusable unusable => new Gate.Refused($"the sign-in's gate cannot be taken ({unusable.Cause.Message}) — fix the lock folder's permissions"),
                _ => new Gate.Refused("the sign-in's gate is busy"),
            };
        }
        catch (OperationCanceledException)
        {
            return new Gate.Refused($"a Kimi launch holds the sign-in's gate {KimiCredentialsLock.PathFor(signIn)} (waited {wait.TotalSeconds:0.#} s), and a marker cannot be judged beside a turn that is starting — press again when it is over");
        }
    }

    /// <summary>Why the request is a fault — or empty. Both are 65.</summary>
    private static string Refusal(string[] args, string lockRoot)
    {
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            return $"{Mode} needs <path>: the survivor marker's full path under {lockRoot} (a *.survivors.json file) — nothing else is ever deleted by this mode";
        }

        return IsMarkerUnder(Path.GetFullPath(args[1]), lockRoot)
            ? string.Empty
            : $"{Path.GetFullPath(args[1])} is not a Kimi survivor marker under {lockRoot} — this mode deletes nothing else";
    }

    /// <summary>A file directly under the lock root, named as a marker is (sixteen hex digits, then <c>.survivors.json</c>).</summary>
    private static bool IsMarkerUnder(string full, string lockRoot) =>
        MarkerName().IsMatch(Path.GetFileName(full))
        && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(full) ?? string.Empty),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(lockRoot)),
            StringComparison.Ordinal);

    [GeneratedRegex(@"^[0-9a-f]{16}\.survivors\.json$")]
    private static partial Regex MarkerName();

    private static (int Exit, ClearMarkerReport Report) Cleared(
        string path, MarkerRead read, Func<ProcessIdentity, Liveness> liveness, Func<KimiPresence> presence, Action<string> note) => read switch
        {
            MarkerRead.Absent => Said(note, $"no marker at {path} — nothing to clear", Ok, new ClearMarkerReport(path, "absent", Ok, [])),
            MarkerRead.Unreadable unreadable => Said(
                note, $"{path} could not be read ({unreadable.Reason}); a marker that cannot be read is not known to be safe to delete", Unverifiable, new ClearMarkerReport(path, "kept", Unverifiable, [])),
            MarkerRead.Read named => KimiSurvivorGate.OwnerLiveness(named.Marker, liveness) switch
            {
                Liveness.Alive => Said(
                    note, $"{path} is kept: the coai shim pid {named.Marker.Marker.OwnerPid} that wrote it is still running — its turn is not over; this mode never ends a process — wait for the turn to end, then press again",
                    StillRunning, new ClearMarkerReport(path, "kept", StillRunning, [])),
                Liveness.Unknown unknown => Said(
                    note, $"{path} is kept: the coai shim pid {named.Marker.Marker.OwnerPid} that wrote it could not be checked ({unknown.Reason}); a process that cannot be checked is not known to be gone",
                    Unverifiable, new ClearMarkerReport(path, "kept", Unverifiable, [])),
                _ => Judged(path, named.Marker, liveness, presence, note),
            },
            _ => Said(note, $"{path} could not be read", Unverifiable, new ClearMarkerReport(path, "kept", Unverifiable, [])),
        };

    /// <summary>The owner is gone (the marker is an orphan's): a nameless marker by the listing, a named one by its members.</summary>
    private static (int Exit, ClearMarkerReport Report) Judged(
        string path, KimiSurvivorMarkerDto marker, Func<ProcessIdentity, Liveness> liveness, Func<KimiPresence> presence, Action<string> note) =>
        marker.Marker.Unidentified ? Nameless(path, marker, presence(), note) : Named(path, marker, liveness, note);

    /// <summary>A marker that names no process: cleared only over a readable listing with no kimi in it.</summary>
    private static (int Exit, ClearMarkerReport Report) Nameless(string path, KimiSurvivorMarkerDto marker, KimiPresence presence, Action<string> note) => presence switch
    {
        KimiPresence.NoKimi => Deleted(path, marker, "it named no process, and no kimi runs on this machine", [], note),
        KimiPresence.Running running => Said(
            note, $"{path} names no process (the previous turn ended before Kimi started), and a kimi is running (pid {string.Join(", ", running.Pids)}) — kept", StillRunning, new ClearMarkerReport(path, "kept", StillRunning, [])),
        KimiPresence.Unreadable unreadable => Said(
            note, $"{path} names no process, and this machine's process list could not be read ({unreadable.Reason}) — kept", Unverifiable, new ClearMarkerReport(path, "kept", Unverifiable, [])),
        _ => Said(note, $"{path} could not be judged — kept", Unverifiable, new ClearMarkerReport(path, "kept", Unverifiable, [])),
    };

    /// <summary>Every member is checked FIRST, then the verdict: Alive wins over Unknown.</summary>
    private static (int Exit, ClearMarkerReport Report) Named(string path, KimiSurvivorMarkerDto marker, Func<ProcessIdentity, Liveness> liveness, Action<string> note)
    {
        var checkedMembers = marker.Marker.Tree.Select(member => new Checked(member, liveness(member))).ToList();
        var members = checkedMembers.Select(found => Reported(found.Member, found.Answer)).ToList();

        if (checkedMembers.Find(found => found.Answer is Liveness.Alive)?.Member is { } alive)
        {
            return Said(
                note, $"{path} is kept: pid {alive.Pid} (started {alive.StartedUtc:O}) is still running; this mode never ends a process — wait for it, or end it yourself, then press again",
                StillRunning, new ClearMarkerReport(path, "kept", StillRunning, members));
        }

        if (checkedMembers.Find(found => found.Answer is Liveness.Unknown) is { Answer: Liveness.Unknown unknown } unverified)
        {
            return Said(
                note, $"{path} is kept: pid {unverified.Member.Pid} could not be checked ({unknown.Reason}); a process that cannot be checked is not known to be gone",
                Unverifiable, new ClearMarkerReport(path, "kept", Unverifiable, members));
        }

        return Deleted(path, marker, $"every process it named is gone ({string.Join("; ", members.Select(member => $"{member.Pid}: {member.Detail}"))})", members, note);
    }

    private sealed record Checked(ProcessIdentity Member, Liveness Answer);

    private static (int Exit, ClearMarkerReport Report) Deleted(string path, KimiSurvivorMarkerDto judged, string why, IReadOnlyList<ClearMarkerMember> members, Action<string> note)
    {
        if (!KimiSurvivorMarker.Holds(path, judged))
        {
            return Said(note, $"{path} is kept: it changed while it was judged — a turn wrote a fresh marker, and a fresh marker is never deleted on the strength of the old one's check; press again", Unverifiable, new ClearMarkerReport(path, "kept", Unverifiable, members));
        }

        try
        {
            KimiSurvivorMarker.Clear(path);

            return Said(note, $"cleared {path}: {why}", Ok, new ClearMarkerReport(path, "cleared", Ok, members));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Said(note, $"{path} could not be deleted ({e.Message}) — kept", Unverifiable, new ClearMarkerReport(path, "kept", Unverifiable, members));
        }
    }

    private static ClearMarkerMember Reported(ProcessIdentity member, Liveness answer) => answer switch
    {
        Liveness.Alive => new ClearMarkerMember(member.Pid, member.StartedUtc, "alive", string.Empty),
        Liveness.Gone gone => new ClearMarkerMember(member.Pid, member.StartedUtc, "gone", gone.Why),
        Liveness.Unknown unknown => new ClearMarkerMember(member.Pid, member.StartedUtc, "unknown", unknown.Reason),
        _ => new ClearMarkerMember(member.Pid, member.StartedUtc, "unknown", "an unknown liveness"),
    };

    private static (int Exit, ClearMarkerReport Report) Said(Action<string> note, string sentence, int exit, ClearMarkerReport report)
    {
        note(sentence);

        return (exit, report);
    }
}
