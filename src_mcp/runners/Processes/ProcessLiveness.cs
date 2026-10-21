namespace CoaiMcp.Runners.Processes;

/// <summary>Is the process that owned a running turn still there?</summary>
/// <remarks>
/// <para>Moved DOWN from <c>CoaiMcp.Server</c> to the runners (E3 S4: <c>AtomicFile</c> asks it whether an orphan temp file's writer is alive and cannot reference the server layer — the move, never a copy). Out of <c>PanelService</c> (S3 of the question consultant, D12: that file grows by zero net lines),
/// unchanged: it reads no state and every sweep asks it.</para>
/// <para><b>Every failure to ask is NOT ALIVE, and none of them may throw.</b> This is called from the sweeps,
/// which run on startup — so an exception here does not fail a sweep, it takes the whole binary down before it
/// has answered anything. Found by the <c>--close-consult</c> scenario on issue #309: a record left at
/// <c>asking</c> with pid 0, which is what a torn write or an older build leaves, made Windows answer
/// <c>Win32Exception (5): Access is denied</c> — pid 0 is the idle process and nobody may open it. A pid
/// belonging to another user does the same.</para>
/// <para>A pid this process cannot open is one it cannot vouch for, and the sweep's question is "may I reclaim
/// this record". Answering "not alive" reclaims it, which is the safe direction: the alternative is a
/// consultation stuck at <c>asking</c> for ever because nobody could ask.</para>
/// </remarks>
public static class ProcessLiveness
{
    /// <summary>The two-state answer the sweeps ask: true only for a process proven running. "Could not ask" is NOT alive here, by design (see above).</summary>
    public static bool IsAlive(int pid) => Check(pid) is Liveness.Alive;

    /// <summary>
    /// The three-answer check, for a caller that DELETES on the answer (E3 code gate G1/G2): <see cref="Liveness.Gone"/> only when the process is
    /// provably not there — no process has that id (<see cref="ArgumentException"/>), or it has exited. <see cref="Liveness.Unknown"/> is a process
    /// that could not be asked about (<c>Win32Exception</c>: not ours to open) and a pid nothing could own; a deletion must treat it as alive. Never throws.
    /// </summary>
    /// <remarks>
    /// <b>The narrow limit, decided and not changed:</b> a pid in ANOTHER pid namespace is not Unknown. On Linux that process is simply not visible, so
    /// <c>GetProcessById</c> throws <see cref="ArgumentException"/> and the answer is <c>Gone</c>. Two pid namespaces sharing one data folder (a container
    /// and its host) can therefore see a live writer's aged temp file removed by the other; that writer's move then fails loudly and the record it was
    /// replacing stays whole.
    /// </remarks>
    public static Liveness Check(int pid) =>
        pid <= 0
            // A pid nothing could ever own. Windows answers "access denied" for 0 rather than "no such process", so it is refused here
            // rather than through an exception handler.
            ? new Liveness.Unknown("not a pid")
            : Asked(pid);

    private static Liveness Asked(int pid)
    {
        try
        {
            return Running(pid);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return new Liveness.Gone("no such process");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return new Liveness.Unknown(e.Message);
        }
    }

    private static Liveness Running(int pid)
    {
        using var process = System.Diagnostics.Process.GetProcessById(pid);

        return process.HasExited ? new Liveness.Gone("exited") : new Liveness.Alive();
    }
}
