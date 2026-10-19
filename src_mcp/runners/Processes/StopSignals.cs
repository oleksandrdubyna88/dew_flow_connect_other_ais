using System.Runtime.InteropServices;

namespace CoaiMcp.Runners.Processes;

/// <summary>
/// <c>SIGTERM</c> and <c>SIGINT</c> turned into a cancelled token, for a process whose own <c>finally</c> must run (S-K16 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>, DL9): the
/// <c>--ask-acp</c> shim ends its agent's tree in a <c>finally</c>, and without a registration the runtime exits on the signal and skips it.
/// </summary>
/// <remarks>
/// <para>Extracted from the registration that <c>Program.RefusingServeAsync</c> and <c>ConsultantCheckMode</c> each carry a copy of. Those two copies are NOT replaced here
/// (the plan's Q7, ASKED, not done silently): this is the third use, and the other two stay until the owner answers. <c>ServeStop.Register</c> is a different shape — four signals and a grace — and is not
/// in the question.</para>
/// <para>It lives in the runners rather than beside <c>Program</c> so the fake CLI, which plays the shim in a real process, uses the very registration the shim does.</para>
/// </remarks>
public static class StopSignals
{
    /// <summary>Registers both signals: each cancels the default action (the runtime does not exit) and cancels <paramref name="stopping"/>. Disposing the result unregisters them.</summary>
    public static IDisposable Into(CancellationTokenSource stopping) =>
        new Registrations([.. new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT }.SelectMany(signal => Registered(signal, stopping))]);

    /// <summary>The registration of one signal, or none where the platform has no such signal — the other still stops the process cleanly.</summary>
    private static IEnumerable<PosixSignalRegistration> Registered(PosixSignal signal, CancellationTokenSource stopping)
    {
        try
        {
            return [PosixSignalRegistration.Create(signal, context =>
            {
                context.Cancel = true;
                stopping.Cancel();
            })];
        }
        catch (PlatformNotSupportedException)
        {
            return [];
        }
    }

    private sealed class Registrations(IReadOnlyList<PosixSignalRegistration> all) : IDisposable
    {
        public void Dispose()
        {
            foreach (var one in all)
            {
                one.Dispose();
            }
        }
    }
}
