using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The two facts about the machine a Kimi launch is judged against (D18 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>, DL1 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): which kind of host this is, and
/// which WSL it is when it is one. The shape of <see cref="Vibe.VibeHost"/> without its sign-in, which Kimi finds another way (<see cref="KimiPlaces"/>). Resolved once, where the adapter is made.
/// </summary>
/// <param name="Kind">The host (<see cref="HostKinds.Current"/> for this process).</param>
/// <param name="Wsl">Which WSL, when <paramref name="Kind"/> is <see cref="HostKind.Wsl"/> — a WSL1 kernel reads as that host too, and is refused.</param>
public sealed record KimiHost(HostKind Kind, WslGeneration Wsl)
{
    /// <summary>
    /// A host of <paramref name="kind"/>; the kernel's generation is THIS machine's when <paramref name="wsl"/> is unset and the kind is WSL — so a test that asks for a bare WSL host on a runner that is
    /// not one gets a refusal, never a quiet admission.
    /// </summary>
    public static KimiHost Of(HostKind kind, WslGeneration? wsl = null) =>
        new(kind, wsl ?? (kind == HostKind.Wsl ? WslGenerations.Current : WslGeneration.None));

    /// <summary>This process's host, as the product asks it: the operating system and the kernel's banner.</summary>
    public static KimiHost ThisProcess() => Of(HostKinds.Current);

    /// <summary>The host a Kimi adapter made now is judged against: the flow's override when a test gave one, else this process's.</summary>
    public static KimiHost Current => Overridden.Value ?? ThisProcess();

    private static readonly AsyncLocal<KimiHost?> Overridden = new();

    /// <summary>
    /// A scenario's seam, per logical flow (the shape of <see cref="Vibe.VibeHost.Using"/>): the real service builds its <see cref="KimiRuntime"/> itself (<c>new KimiRuntime(vendorId)</c>), which takes
    /// <see cref="Current"/>, so a flow that must run as a WSL2 host on a CI runner that is not one cannot hand the adapter a host. Dispose restores the process's.
    /// </summary>
    internal static IDisposable Using(KimiHost host)
    {
        var before = Overridden.Value;
        Overridden.Value = host;

        return new Restore(() => Overridden.Value = before);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
