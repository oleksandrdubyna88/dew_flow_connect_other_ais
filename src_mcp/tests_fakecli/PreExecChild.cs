using System.Reflection;
using System.Runtime.InteropServices;

namespace CoaiMcp.FakeCli;

/// <summary>
/// The ONE way the fake CLI makes a child that never execs (Linux): <c>fork</c>s, and the child sits in <c>pause()</c> until something kills it. Shared by <c>hold-file fork-fail</c>
/// (a copy of a descriptor that no exec has closed, C26) and <c>unreaped-child</c> (a child its parent never reaps, C21/C29).
/// </summary>
/// <remarks>
/// <c>pause</c> is bound BEFORE the fork (<see cref="Marshal.Prelink(MethodInfo)"/>), so the child — which has one thread and a runtime that was running others — does no symbol lookup
/// between the fork and the wait. What the child does if the runtime misbehaves there is "stay" or "die"; the test reads the child's pid and fails with a sentence if it is not alive.
/// The child is no <see cref="System.Diagnostics.Process"/> of this one: the runtime reaps only the children it started, so a killed one stays a zombie until THIS process ends.
/// </remarks>
internal static class PreExecChild
{
    [DllImport("libc", EntryPoint = "fork")]
    private static extern int Fork();

    [DllImport("libc", EntryPoint = "pause")]
    private static extern int Pause();

    /// <summary>Forks a child that stays in <c>pause()</c> for ever and returns its pid, in the parent.</summary>
    internal static int Start()
    {
        Marshal.Prelink(typeof(PreExecChild).GetMethod(nameof(Pause), BindingFlags.NonPublic | BindingFlags.Static)!);
        var child = Fork();
        while (child == 0)
        {
            _ = Pause();
        }

        return child;
    }
}
