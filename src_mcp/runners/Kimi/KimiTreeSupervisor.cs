using System.Diagnostics;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Kimi;

/// <summary>What starting a supervised turn came to: the supervisor, ready — or the sentence the shim refuses with (exit 65) because the first marker could not be written.</summary>
public abstract record SupervisorBegin
{
    private SupervisorBegin()
    {
    }

    /// <summary>The <c>spawning</c> marker is on disk; start the agent.</summary>
    public sealed record Ready(KimiTreeSupervisor Supervisor) : SupervisorBegin;

    /// <summary>Nothing may be started: the marker that makes a crash safe could not be written.</summary>
    public sealed record Refused(string Sentence) : SupervisorBegin;
}

/// <summary>
/// The Kimi <see cref="ITreeSupervisor"/> (DL9 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): the survivor marker's phases around <see cref="SupervisedTeardown"/>. A SIGKILLed shim leaves a
/// <c>running</c> marker naming the agent; a clean teardown deletes it; a survivor keeps it.
/// </summary>
/// <remarks>
/// <para>Phases: <c>spawning</c> (<see cref="Begin"/>, before the agent starts — a failed write starts nothing), <c>running [agent]</c> (<see cref="Started"/>, right after the start), <c>running [members]</c>
/// (before any signal), deleted (only on a confirmed-empty re-listing). A write that fails during the teardown never skips the kill: the previous marker stands (fail closed) and the failure goes to stderr.</para>
/// <para>Linux only in the product (the survivor problem is a Linux one); the teardown is a seam so the phases are tested on any host.</para>
/// </remarks>
public sealed class KimiTreeSupervisor : ITreeSupervisor
{
    /// <summary>The teardown behind the supervisor: <see cref="SupervisedTeardown.EndAsync"/> in the product.</summary>
    internal delegate Task<TreeEnd> Teardown(Process agent, int shimPid, IReadOnlySet<int> startedByUs, Action<IReadOnlyList<ProcessIdentity>> onListed);

    private readonly string _markerPath;
    private readonly string _ownedRoot;
    private readonly string _signInFolder;
    private readonly int _shimPid;
    private readonly DateTime _shimStartedUtc;
    private readonly Teardown _teardown;
    private readonly Action<string> _note;
    private readonly HashSet<int> _started = [];
    private IReadOnlyList<ProcessIdentity> _listed = [];

    internal KimiTreeSupervisor(string markerPath, string ownedRoot, string signInFolder, int shimPid, DateTime shimStartedUtc, Teardown teardown, Action<string> note)
    {
        _markerPath = markerPath;
        _ownedRoot = ownedRoot;
        _signInFolder = signInFolder;
        _shimPid = shimPid;
        _shimStartedUtc = shimStartedUtc;
        _teardown = teardown;
        _note = note;
    }

    /// <summary>
    /// Writes the <c>spawning</c> marker and answers with the supervisor — or with the refusal. This host's teardown: under a subreaper, by identity through <see cref="PidfdLayer.Calls"/>.
    /// Called by <c>KimiShimHold.Taken</c> after both tree files are held and the previous marker was reconciled.
    /// </summary>
    public static SupervisorBegin Begin(string markerPath, string ownedRoot, string signInFolder) =>
        Begin(markerPath, ownedRoot, signInFolder, Environment.ProcessId, ProcessTracking.StartedAt, ThisHost, Console.Error.WriteLine);

    /// <summary>
    /// With the shim's start time read through a seam. <b>A start time that cannot be read refuses the turn</b> (nothing starts): the marker names its owner by pid AND start, and an invented start disagrees with the real
    /// one on every later read — a live owner would read as Gone, and the next launch would end an active turn's agent.
    /// </summary>
    internal static SupervisorBegin Begin(
        string markerPath, string ownedRoot, string signInFolder, int shimPid, Func<int, DateTime?> startedAt, Teardown teardown, Action<string> note) =>
        startedAt(shimPid) is { } started
            ? Begin(markerPath, ownedRoot, signInFolder, shimPid, started, teardown, note)
            : new SupervisorBegin.Refused(
                $"coai cannot read the start time of its own shim (pid {shimPid}), so the survivor marker could not name its owner; coai does not start Kimi without it (DL9 of todo/PLAN_kimi_and_vibe_on_linux.md)");

    internal static SupervisorBegin Begin(
        string markerPath, string ownedRoot, string signInFolder, int shimPid, DateTime shimStartedUtc, Teardown teardown, Action<string> note)
    {
        var supervisor = new KimiTreeSupervisor(markerPath, ownedRoot, signInFolder, shimPid, shimStartedUtc, teardown, note);
        try
        {
            supervisor.Write(ReaderPhases.Spawning, []);

            return new SupervisorBegin.Ready(supervisor);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new SupervisorBegin.Refused(
                $"the survivor marker {markerPath} could not be written ({e.Message}); coai does not start Kimi without it (DL9 of todo/PLAN_kimi_and_vibe_on_linux.md)");
        }
    }

    public void Started(ProcessIdentity agent)
    {
        _ = _started.Add(agent.Pid);
        TryWrite(ReaderPhases.Running, [agent]);
    }

    public void StartedUnidentified(int pid) => _ = _started.Add(pid);

    public async Task<TreeEnd> EndAsync(Process agent)
    {
        var end = await _teardown(agent, _shimPid, _started, members =>
        {
            _listed = members;
            TryWrite(ReaderPhases.Running, members);
        });
        if (end.Survivors.Count == 0)
        {
            TryClear();
        }
        else
        {
            KeepNaming(end.Survivors);
        }

        return end;
    }

    /// <summary>A survivor born after the first listing is not in the marker yet: it is written, so the next launch finds it by identity.</summary>
    private void KeepNaming(IReadOnlyList<ProcessIdentity> survivors)
    {
        TryWrite(ReaderPhases.Running, [.. _listed.Concat(survivors).DistinctBy(member => member.Pid)]);
        _note(KimiSurvivorGate.Sentence(new SurvivorVerdict.Blocked(MarkerOf(ReaderPhases.Running, survivors), _markerPath, BlockedReasons.ReaderTerminationFailed, string.Empty)));
    }

    private static Teardown ThisHost => static (agent, shimPid, started, onListed) =>
        OperatingSystem.IsLinux() ? SupervisedTeardown.EndAsync(agent, shimPid, started, PidfdLayer.Calls, onListed) : UnsupervisedTree.Instance.EndAsync(agent);

    private KimiSurvivorMarkerDto MarkerOf(string phase, IReadOnlyList<ProcessIdentity> tree) =>
        new(new ReaderMarkerDto(phase, _shimPid, _shimStartedUtc, DateTimeOffset.UtcNow, tree), _ownedRoot, _signInFolder);

    private void Write(string phase, IReadOnlyList<ProcessIdentity> tree) => KimiSurvivorMarker.Write(_markerPath, MarkerOf(phase, tree));

    /// <summary>A write during the turn that fails never stops it: the previous marker stands, and the failure is said on stderr.</summary>
    private void TryWrite(string phase, IReadOnlyList<ProcessIdentity> tree)
    {
        try
        {
            Write(phase, tree);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _note($"the survivor marker {_markerPath} could not be updated ({e.Message}); the previous one stands");
        }
    }

    private void TryClear()
    {
        try
        {
            KimiSurvivorMarker.Clear(_markerPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _note($"the survivor marker {_markerPath} could not be deleted after a clean teardown ({e.Message}); it stands, and the next launch reconciles it");
        }
    }
}
