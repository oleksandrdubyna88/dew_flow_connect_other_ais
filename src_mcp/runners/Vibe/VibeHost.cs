using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// The three facts about the machine a Vibe launch is judged against (D18 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>, DL1 and DL2 of
/// <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): which kind of host this is, which WSL it is when it is one, and where the person's Vibe
/// sign-in lives. Resolved once, where the adapter is made.
/// </summary>
/// <param name="Kind">The host (<see cref="HostKinds.Current"/> for this process).</param>
/// <param name="Wsl">Which WSL, when <paramref name="Kind"/> is <see cref="HostKind.Wsl"/> — a WSL1 kernel reads as that host too, and is refused.</param>
/// <param name="SignIn">The person's sign-in file, whose link an owned home may hold.</param>
public sealed record VibeHost(HostKind Kind, WslGeneration Wsl, VibeSignIn SignIn)
{
    /// <summary>A host of <paramref name="kind"/> with no sign-in link and no WSL — Windows, or a refusal that is decided before either matters.</summary>
    public static VibeHost Of(HostKind kind) => new(kind, WslGeneration.None, VibeSignIn.None);

    /// <summary>This process's host, as the product asks it: the operating system, the kernel's banner, coai's own environment.</summary>
    public static VibeHost ThisProcess()
    {
        var kind = HostKinds.Current;

        return new VibeHost(kind, kind == HostKind.Wsl ? WslGenerations.Current : WslGeneration.None, VibeSignIn.ForThisProcess(kind));
    }

    /// <summary>
    /// The platform whose manifest entry this host's Vibe is measured against, as node spells it (<c>process.platform</c>): WSL and native
    /// Linux are <c>linux</c>. Taken from the HOST the adapter was given — in the product, the process's own.
    /// </summary>
    public string Platform => Kind switch
    {
        HostKind.Windows => "win32",
        HostKind.Linux or HostKind.Wsl => "linux",
        HostKind.MacOs => "darwin",
        _ => "other",
    };

    /// <summary>The host a Vibe adapter made now is judged against: the flow's override when a test gave one, else this process's.</summary>
    public static VibeHost Current => Overridden.Value ?? ThisProcess();

    private static readonly AsyncLocal<VibeHost?> Overridden = new();

    /// <summary>
    /// A scenario's seam, per logical flow (the shape of <see cref="VibePathBudget.Using"/>): the real service builds its
    /// <see cref="VibeRuntime"/> itself (<c>new VibeRuntime(vendorId)</c>), which takes <see cref="Current"/>, so a flow that must run as a
    /// WSL host on a CI runner that is not one cannot hand the adapter a host. Dispose restores the process's.
    /// </summary>
    internal static IDisposable Using(VibeHost host)
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
