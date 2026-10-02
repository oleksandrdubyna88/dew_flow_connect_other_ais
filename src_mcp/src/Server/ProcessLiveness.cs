namespace CoaiMcp.Server;

/// <summary>Is the process that owned a running turn still there?</summary>
/// <remarks>
/// <para>Out of <see cref="PanelService"/> (S3 of the question consultant, D12: that file grows by zero net lines),
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
internal static class ProcessLiveness
{
    public static bool IsAlive(int pid)
    {
        // A pid nothing could ever own. Windows answers "access denied" for 0 rather than "no such
        // process", so it is refused here rather than through an exception handler.
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Gone, never existed, or not ours to open. All three mean the same thing to a sweep.
            return false;
        }
    }
}
