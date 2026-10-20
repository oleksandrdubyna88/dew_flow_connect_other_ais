using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The host a scenario-harness Kimi flow runs as (E6 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>, §8): the real service builds its own <see cref="KimiRuntime"/>, which takes <see cref="KimiHost.Current"/> and
/// <see cref="KimiSeams.Real"/>, so a flow that must run as a WSL2 host on a CI runner that is not one — with a FAKE person's <c>~/.kimi-code</c> and THIS repository's <c>coai-mcp</c> as the shim — gives both through the
/// per-flow seams (<see cref="KimiHost.Using"/>, <see cref="KimiSeams.Using"/>), and the sign-in lock root of its own.
/// </summary>
/// <remarks>
/// <para><b>Four arms.</b> <see cref="Wsl2"/> runs everywhere; <see cref="Windows"/> is the flow as Kimi always ran, on a Windows machine only; <see cref="Linux"/> and <see cref="Wsl1"/> are the refused hosts and start nothing. On a Linux
/// machine every step is REAL — the symlink, the <c>flock</c>, the pidfd teardown, the mount table, the shim as a child process; on Windows the WSL2 arm runs the host DECISIONS as a WSL2 host (admission, the link's mount judgement
/// over a stood-in ext4 table) on the Windows junction and lock, and the Linux CI job runs the real steps.</para>
/// <para><b>What this does not prove:</b> the real <c>kimi</c> (the agent is the fake ACP agent), a real WSL kernel's drvfs mount (CI is ubuntu, not WSL), the person's real <c>~/.kimi-code</c>, and Kimi's own
/// sub-agents, background shell and MCP servers.</para>
/// </remarks>
internal sealed class KimiFlowHost : IDisposable
{
    public const string Windows = "windows";

    public const string Wsl2 = "wsl2";

    public const string Linux = "linux";

    public const string Wsl1 = "wsl1";

    private const string Ext4 = "25 1 8:1 / / rw - ext4 /dev/sda1 rw\n";

    private readonly List<IDisposable> _scopes;

    private KimiFlowHost(List<IDisposable> scopes) => _scopes = scopes;

    /// <summary>Gives the flow its host, its seams and its lock root. <paramref name="scratch"/> is a folder of the flow's own.</summary>
    public static KimiFlowHost Enter(string mode, KimiFixture person, string scratch)
    {
        var host = HostOf(mode);
        if (mode == Windows)
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "the Windows-host arm of this flow needs a Windows machine (the WSL2 arm runs everywhere)");
        }

        if (mode is Windows or Wsl2)
        {
            TheOwnersKimi.SkipWhenARealKimiRuns();
        }

        List<IDisposable> scopes = [KimiHost.Using(host), KimiSeams.Using(SeamsFor(host.Kind, person)), KimiCredentialsLock.RootFor(Path.Combine(scratch, "locks"))];
        if (OperatingSystem.IsWindows())
        {
            // A Windows machine has no mount table; the WSL2 arm judges the link as a WSL2 host would on an ext4 home.
            Junctions.StandInMountInfo(() => Ext4, _ => new MountIdentity.NotAsked());
            scopes.Add(new Restore(() => Junctions.StandInMountInfo(null)));
        }

        return new KimiFlowHost(scopes);
    }

    private static KimiHost HostOf(string mode) => mode switch
    {
        Windows => KimiHost.Of(HostKind.Windows),
        Linux => new KimiHost(HostKind.Linux, WslGeneration.None),
        Wsl1 => new KimiHost(HostKind.Wsl, WslGeneration.Wsl1),
        _ => new KimiHost(HostKind.Wsl, WslGeneration.Wsl2),
    };

    /// <summary>The fake person found the way the product finds a real one — <see cref="KimiPlaces.Resolve"/> over the fixture's profile on Linux — and this binary as the shim.</summary>
    private static KimiSeams SeamsFor(HostKind host, KimiFixture person) => new(
        OperatingSystem.IsWindows()
            ? person.PlacesAt
            : root => KimiPlaces.Resolve(root, host, name => name == "PATH" ? "/usr/bin:/bin" : null, person.Profile, ExecutableResolver.IsInstalledFile),
        () => (ServerBinary.Path, []));

    public void Dispose()
    {
        for (var at = _scopes.Count - 1; at >= 0; at--)
        {
            _scopes[at].Dispose();
        }
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
