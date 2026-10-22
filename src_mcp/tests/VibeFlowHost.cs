using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The host a scenario-harness Vibe flow runs as (E2 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>, §8): the real service builds its own
/// <see cref="VibeRuntime"/>, which takes <see cref="VibeHost.Current"/>, so a flow that must run as a WSL2 host on a CI runner that is not
/// one gives it through the per-flow seam (<see cref="VibeHost.Using"/>) — with a fake person's <c>~/.vibe/.env</c> in the flow's own scratch
/// folder, never the machine's.
/// </summary>
/// <remarks>
/// <para><b>Two arms.</b> <see cref="Windows"/> is the flow as it always ran, on a Windows machine only; <see cref="Wsl2"/> runs everywhere. On
/// Linux the link is REAL (a symlink to a 0600 file this process owns, judged through a descriptor); on Windows, which cannot judge a Linux
/// descriptor and often cannot make a symbolic link, the disk steps are stood in (<see cref="FakeSignInDisk"/>) — every DECISION of the launch
/// control is the product's either way, and the Linux CI job runs the real steps.</para>
/// <para><b>What this does not prove:</b> a real Vibe (the CLI is played or faked), the real keyring or D-Bus behaviour, a real WSL kernel's drvfs
/// mount (CI is ubuntu, not WSL), and the person's real <c>~/.vibe/.env</c>.</para>
/// </remarks>
internal sealed class VibeFlowHost : IDisposable
{
    public const string Windows = "windows";

    public const string Wsl2 = "wsl2";

    /// <summary>A NATIVE Linux host with a read, non-WSL banner (E2.S2 of <c>todo/PLAN_kimi_and_vibe_on_native_linux_E2.md</c>): the same fake person, the same links as <see cref="Wsl2"/>.</summary>
    public const string NativeLinux = "linux";

    private readonly IDisposable _seam;

    private VibeFlowHost(IDisposable seam, VibeHost host, string personsEnv, List<(string Link, string Target)> made)
    {
        _seam = seam;
        Host = host;
        PersonsEnv = personsEnv;
        Made = made;
    }

    /// <summary>The host the flow runs as (a Windows flow's is <see cref="VibeHost.Current"/>).</summary>
    public VibeHost Host { get; }

    /// <summary>The fake person's sign-in file.</summary>
    public string PersonsEnv { get; }

    /// <summary>Every link the launch control made, as (link, target) — recorded at the disk step, so a home deleted with its round is still accounted for.</summary>
    public List<(string Link, string Target)> Made { get; }

    /// <summary>Gives the flow its host. <paramref name="scratch"/> is a folder of the flow's own, which holds the fake person's file.</summary>
    public static VibeFlowHost Enter(string mode, string scratch)
    {
        if (mode == Windows)
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "the Windows-host arm of this flow needs a Windows machine (the WSL2 arm runs everywhere)");

            return new VibeFlowHost(new Nothing(), VibeHost.Current, string.Empty, []);
        }

        var persons = Path.Combine(Directory.CreateDirectory(Path.Combine(scratch, "persons-vibe")).FullName, ".env");
        File.WriteAllText(persons, "MISTRAL_API_KEY=sk-test-harness-flow\n");
        OwnerOnly(persons);
        var disk = new FakeSignInDisk();
        // A fresh buffer per judgement: the launch control zeroes the one it is handed after the scan, and a flow launches more than once.
        var real = OperatingSystem.IsLinux() ? SignInSteps.Real : disk.Steps with { Judge = _ => new FileReadResult.Content(File.ReadAllBytes(persons)) };
        List<(string Link, string Target)> made = [];
        var steps = real with { Link = (link, target) => { real.Link(link, target); made.Add((link, target)); } };

        var host = mode == NativeLinux
            ? new VibeHost(HostKind.Linux, WslGeneration.None, new VibeSignIn(persons) { Steps = steps })
            : new VibeHost(HostKind.Wsl, WslGeneration.Wsl2, new VibeSignIn(persons) { Steps = steps });

        return new VibeFlowHost(VibeHost.Using(host), host, persons, made);
    }

    /// <summary>The WSL2 and native Linux arms made exactly the one link a Vibe home may hold — to the fake person's file, by its exact spelling — and the Windows arm none.</summary>
    public void AssertLinked(string mode)
    {
        if (mode == Windows)
        {
            Made.Should().BeEmpty("a Windows host links nothing");

            return;
        }

        Made.Should().NotBeEmpty("a Vibe launch on a wsl host should have linked the sign-in");
        Made.Should().OnlyContain(made => made.Target == PersonsEnv && Path.GetFileName(made.Link) == ".env", "the one admitted link: .env -> the person's file");
    }

    private static void OwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public void Dispose() => _seam.Dispose();

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
