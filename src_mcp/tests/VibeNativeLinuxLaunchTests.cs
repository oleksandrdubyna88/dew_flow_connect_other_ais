using System.Text.RegularExpressions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E2.S2 of <c>todo/PLAN_kimi_and_vibe_on_native_linux_E2.md</c> (C1, §2.3 and §2.5): Vibe is admitted on a native Linux host and its launch there closes the keyring road
/// (<c>PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring</c>, DN10), measured closed on Vibe 2.26.1 with keyring 25.7.0 (research/RESULTS_vibe_confinement.md §32). The request-shaped half:
/// what <see cref="VibeLaunch.Built"/> makes and <see cref="VibeLaunch.EnvironmentRefusal"/> admits per host (S-N12), the probes' literal against the product's (S-N21), the clause an
/// unmeasured Vibe is warned with there (S-N22), and the native twin of the WSL2 launch (S-N5). The value reaching a REAL child on every road is <see cref="VibeNativeLinuxDeliveryTests"/>.
/// </summary>
/// <remarks>
/// In the <c>fakecli-env</c> collection only because two of its tests set a process environment variable for their duration (restored in <c>finally</c>) — the collection runs alone, so no
/// other class sees the conflicting value and no two classes race to restore it.
/// </remarks>
[Collection("fakecli-env")]
public sealed class VibeNativeLinuxLaunchTests : IDisposable
{
    private const string Persons = "/home/p/.vibe/.env";

    private const string Fail = "keyring.backends.fail.Keyring";

    private const string Null = "keyring.backends.null.Keyring";

    private readonly TempDir _scratch = TempDir.For("testsToDelete-vnl-");
    private readonly TempDir _work = TempDir.For("testsToDelete-vnl-work-");

    public void Dispose()
    {
        _scratch.Dispose();
        _work.Dispose();
    }

    private static ProcessRequest BuiltFor(HostKind host) =>
        VibeLaunch.Built(new ProcessRequest("vibe", ["-p"], "/w"), "/h", "glm-5-3", host);

    private static Dictionary<string, string?> Without(ProcessRequest request, string name) =>
        request.Environment.Where(pair => pair.Key != name).ToDictionary(pair => pair.Key, pair => pair.Value);

    private static Dictionary<string, string?> With(ProcessRequest request, string name, string value) =>
        new(request.Environment) { [name] = value };

    // ---------- S-N12 : the host is an input of the builder and of the control ----------

    /// <summary>S-N12 (teeth): a native Linux launch carries the fifth name with its value, and the control admits exactly that set there.</summary>
    [Fact]
    public void ANativeLinuxLaunch_CarriesTheClosedKeyringBackend_AndTheControlAdmitsExactlyThatSet()
    {
        var built = BuiltFor(HostKind.Linux);

        built.Environment.Should().ContainKey(VibeLaunch.KeyringBackendVariable,
            "a native Linux Vibe launch carries no PYTHON_KEYRING_BACKEND — the host's keyring could sign a fresh owned home in (DN4)")
            .WhoseValue.Should().Be(Fail);
        built.Environment.Should().HaveCount(5, "the four names of every host and the fifth of a native Linux one");
        VibeLaunch.EnvironmentRefusal(built, HostKind.Linux).Should().BeEmpty("the control admits what the builder makes for that host");
    }

    /// <summary>S-N12: on a native Linux host a launch without the fifth name, or with another value, or with a sixth name, is refused — the road would be open.</summary>
    [Theory]
    [InlineData("four names")]
    [InlineData("another backend")]
    [InlineData("an empty value")]
    [InlineData("a sixth name")]
    public void ANativeLinuxLaunch_WithoutExactlyTheFifthName_IsRefused(string row)
    {
        var because = row switch
        {
            "four names" => "a four-name Vibe launch on a native Linux host was admitted — the keyring road is open",
            "another backend" => "a Vibe launch forcing another keyring backend on a native Linux host was admitted — it is not the measured closure",
            "an empty value" => "a Vibe launch with an empty PYTHON_KEYRING_BACKEND on a native Linux host was admitted — Python reads an empty value as unset",
            _ => "a Vibe launch with a sixth name on a native Linux host was admitted",
        };
        var built = BuiltFor(HostKind.Linux);
        var changed = row switch
        {
            "four names" => Without(built, VibeLaunch.KeyringBackendVariable),
            "another backend" => With(built, VibeLaunch.KeyringBackendVariable, Null),
            "an empty value" => With(built, VibeLaunch.KeyringBackendVariable, string.Empty),
            _ => With(built, "PYTHONIOENCODING", "utf-8"),
        };

        VibeLaunch.EnvironmentRefusal(built with { Environment = changed }, HostKind.Linux).Should().Contain("D17", because);
    }

    /// <summary>S-N12: WSL2, Windows and every other host keep today's four names byte for byte, and the control REFUSES the fifth there — a measured launch is not changed.</summary>
    [Theory]
    [InlineData(HostKind.Wsl, "WSL2")]
    [InlineData(HostKind.Windows, "Windows")]
    [InlineData(HostKind.MacOs, "macOS")]
    [InlineData(HostKind.Other, "other")]
    public void EveryOtherHost_KeepsTheFourNames_AndRefusesTheFifth(HostKind host, string word)
    {
        var built = BuiltFor(host);

        built.Environment.Keys.Should().BeEquivalentTo([VibeLaunch.HomeVariable, VibeLaunch.CliVariable, VibeLaunch.ModelVariable, VibeLaunch.Utf8Variable],
            $"a {word} Vibe launch carried PYTHON_KEYRING_BACKEND — a measured launch was changed");
        VibeLaunch.EnvironmentRefusal(built, host).Should().BeEmpty();
        VibeLaunch.EnvironmentRefusal(built with { Environment = With(built, VibeLaunch.KeyringBackendVariable, Fail) }, host).Should().Contain("D17",
            $"a {word} launch carrying the fifth name was admitted — the road is closed only where it was measured closed");
    }

    /// <summary>S-N12: the refusal sentence names the five on Linux and the four elsewhere.</summary>
    [Fact]
    public void TheRefusalSentence_NamesTheFiveOnLinux_AndTheFourElsewhere()
    {
        var onLinux = VibeLaunch.EnvironmentRefusal(BuiltFor(HostKind.Linux) with { InheritsEnvironment = true }, HostKind.Linux);
        var onWsl = VibeLaunch.EnvironmentRefusal(BuiltFor(HostKind.Wsl) with { InheritsEnvironment = true }, HostKind.Wsl);

        onLinux.Should().Contain($"{VibeLaunch.KeyringBackendVariable}={Fail}", "the sentence did not name the fifth name of a native Linux launch");
        onWsl.Should().NotContain(VibeLaunch.KeyringBackendVariable, "the sentence named a name a WSL2 launch does not carry");
        onWsl.Should().Contain(VibeLaunch.Utf8Variable);
    }

    /// <summary>S-N12: the value is coai's own — never the server's. Set in THIS process for the duration (restored), a Linux launch still carries the closed backend and the passthrough does not name it.</summary>
    [Fact]
    public void TheServersOwnKeyringBackend_NeverReachesTheLaunch()
    {
        var before = Environment.GetEnvironmentVariable(VibeLaunch.KeyringBackendVariable);
        Environment.SetEnvironmentVariable(VibeLaunch.KeyringBackendVariable, Null);
        try
        {
            var linux = BuiltFor(HostKind.Linux);
            var wsl = BuiltFor(HostKind.Wsl);

            linux.Environment.Should().ContainKey(VibeLaunch.KeyringBackendVariable, "the launch carried the server's PYTHON_KEYRING_BACKEND, not coai's")
                .WhoseValue.Should().Be(Fail, "the launch carried the server's PYTHON_KEYRING_BACKEND, not coai's");
            wsl.Environment.Should().NotContainKey(VibeLaunch.KeyringBackendVariable, "the server's PYTHON_KEYRING_BACKEND crossed into a WSL2 launch");
            foreach (var built in (ProcessRequest[])[linux, wsl])
            {
                built.InheritsEnvironment.Should().BeFalse();
                built.Passthrough.Should().NotContain(VibeLaunch.KeyringBackendVariable, "the launch would copy the server's value into the child");
            }

            ProcessEnvironment.MinimalFor(false).Should().NotContain(VibeLaunch.KeyringBackendVariable);
            ProcessEnvironment.MinimalFor(true).Should().NotContain(VibeLaunch.KeyringBackendVariable);
        }
        finally
        {
            Environment.SetEnvironmentVariable(VibeLaunch.KeyringBackendVariable, before);
        }
    }

    /// <summary>S-N12: Kimi has no keyring road — its built set never holds the name, on either platform's rule.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KimisBuiltEnvironment_NeverHoldsTheKeyringBackend(bool windows)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "testsToDelete-vnl-kimi");
        var places = new KimiPlaces(Path.Combine(tmp, "owned", "kh-x"), Path.Combine(tmp, "ki"), Path.Combine(tmp, "ki", "bin"), [Path.Combine(tmp, "tools")], string.Empty, Path.Combine(tmp, "ki", "bin", "kimi"));
        var raw = new ProcessRequest(places.Executable, ["--ask-acp"], places.Home);

        var built = KimiEnvironment.Built(raw, places, Path.Combine(tmp, "tools"), windows);

        built.Environment.Should().NotContainKey(VibeLaunch.KeyringBackendVariable, "Kimi has no keyring road: a Vibe name was added to its launch");
    }

    // ---------- S-N21 : the probes measured this launch ----------

    /// <summary>
    /// S-N21: the probe scripts' <c>NATIVE_LINUX_BUILT</c> literal (<c>scripts/lib/vibe.mjs</c>), parsed the way <c>BUILT_FIXED</c> is, equals what a native Linux launch carries beyond the four of
    /// <c>BUILT_FIXED</c> + the home and the model (reads-another-program: a measurement harness, not one of the three programs). S-N20 pins that the probes carry it; THIS pins that it is the product's value.
    /// </summary>
    [Fact]
    public void TheProbeScripts_ForceTheSameKeyringBackendTheProductForces()
    {
        var script = File.ReadAllText(Path.Combine(RepoPaths.Root(), "scripts", "lib", "vibe.mjs"));
        var declared = Regex.Match(script, @"export const NATIVE_LINUX_BUILT = \{([^}]*)\};");
        declared.Success.Should().BeTrue("scripts/lib/vibe.mjs must declare `export const NATIVE_LINUX_BUILT = { … };` — the probes' native-Linux variable");
        var probes = Regex.Matches(declared.Groups[1].Value, @"(\w+):\s*'([^']*)'").ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);

        var fixedFour = Regex.Match(script, @"export const BUILT_FIXED = \{([^}]*)\};").Groups[1].Value;
        var beyondFixed = BuiltFor(HostKind.Linux).Environment
            .Where(pair => pair.Key is not VibeLaunch.HomeVariable and not VibeLaunch.ModelVariable && !fixedFour.Contains(pair.Key + ":", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty);

        probes.Should().BeEquivalentTo(beyondFixed,
            $"the probes forced {probes.GetValueOrDefault(VibeLaunch.KeyringBackendVariable)} and the product forces {beyondFixed.GetValueOrDefault(VibeLaunch.KeyringBackendVariable)} — the cells measured another launch");
        probes.Should().ContainKey(VibeLaunch.KeyringBackendVariable);
    }

    // ---------- S-N22 : the unmeasured-version clause ----------

    private const string SharedHead =
        "Vibe 2.27.0 is not a version coai has measured (it measured 2.26.1 (linux)); it is run anyway, but its confinement "
        + "(research/RESULTS_vibe_confinement.md) and its home's plugin hashes belong to 2.26.1 (linux); this version's plugin files are "
        + "admitted by name only";

    private const string SharedTail =
        " — re-run `node scripts/vibe-home-manifest.mjs --write` and `node scripts/probe-vibe-confinement.mjs` with this Vibe to measure it";

    private const string KeyringClause =
        ", and the keyring closure (PYTHON_KEYRING_BACKEND) is measured only on Vibe 2.26.1 with keyring 25.7.0 — on this native Linux host another Vibe could reach a keyring by a road this build does not close";

    private static VibeHomeManifest OnlyLinux2261() =>
        new("logs/session/plugins/", ["logs/**"], [new VibeHomeEntry("2.26.1", []) { Platform = "linux" }]);

    /// <summary>S-N22: on a native Linux host an unmeasured Vibe is told the closure is measured only on 2.26.1 with keyring 25.7.0; WSL2 gets today's sentence byte for byte.</summary>
    [Fact]
    public void AnUnmeasuredVibe_OnANativeLinuxHost_IsToldTheKeyringClosureIsMeasuredOnlyOn2261()
    {
        var manifest = OnlyLinux2261();

        var native = VibeLaunch.VersionWarning(manifest, "2.27.0", "linux", HostKind.Linux);
        var wsl = VibeLaunch.VersionWarning(manifest, "2.27.0", "linux", HostKind.Wsl);

        native.Should().Contain("keyring closure").And.Contain("measured only on Vibe 2.26.1 with keyring 25.7.0",
            "an unmeasured Vibe 2.27.0 on a native Linux host was not told the keyring closure is measured only on 2.26.1 — a later Vibe could reach a keyring by a road this build does not close");
        native.Should().Be(SharedHead + KeyringClause + SharedTail, "the clause sits after `admitted by name only`, before the re-run advice");
        wsl.Should().Be(SharedHead + SharedTail, "a WSL2 host's sentence is byte for byte what it was — the clause is for a native Linux host only");
    }

    /// <summary>S-N22: Windows and every host the clause is not for get today's sentence; 2.26.1 itself carries no warning anywhere.</summary>
    [Theory]
    [InlineData(HostKind.Windows, "win32")]
    [InlineData(HostKind.MacOs, "darwin")]
    public void AnUnmeasuredVibe_ElsewhereGetsNoClause_AndTheMeasuredVersionNoWarningAnywhere(HostKind host, string platform)
    {
        var manifest = new VibeHomeManifest("logs/session/plugins/", ["logs/**"], [new VibeHomeEntry("2.26.1", []) { Platform = platform }]);

        VibeLaunch.VersionWarning(manifest, "2.27.0", platform, host).Should().NotContain("keyring closure", $"the clause reached a {HostKinds.Word(host)} host");
        VibeLaunch.VersionWarning(manifest, "2.26.1", platform, host).Should().BeEmpty();
        VibeLaunch.VersionWarning(OnlyLinux2261(), "2.26.1", "linux", HostKind.Linux).Should().BeEmpty("2.26.1 is the measured version");
        VibeLaunch.VersionWarning(OnlyLinux2261(), "2.26.1", "linux", HostKind.Wsl).Should().BeEmpty();
    }

    /// <summary>S-N22: the LAUNCH's warning and the CARD's are one sentence on a native host — and the launch of a measured version carries none.</summary>
    [Fact]
    public async Task TheLaunchWarning_AndTheCardWarning_AreTheSameSentence_OnANativeLinuxHost()
    {
        var disk = new FakeSignInDisk();
        var host = NativeLinux(disk);
        var runtime = new VibeRuntime("vibe", host, VibePathBudget.None);

        string launchWarning;
        using (var unmeasured = await TakeAsync(host, runtime, new FakeVibe(version: "2.27.0")))
        {
            unmeasured.Refusal.Should().BeEmpty("an unmeasured Vibe is warned about and run, never refused (the owner's decision of 2026-10-13)");
            launchWarning = unmeasured.Warning;
        }

        // The home's turn is given back when the first launch is disposed: the second asks for it again.
        using var measured = await TakeAsync(host, runtime, new FakeVibe());

        launchWarning.Should().Contain("keyring closure").And.Contain("measured only on Vibe 2.26.1 with keyring 25.7.0");
        runtime.VersionWarning("python.exe /home/p/.local/bin/vibe 2.27.0").Should().Be(launchWarning, "the card and the launch say the same sentence");
        measured.Warning.Should().BeEmpty("2.26.1 is measured on a native Linux host: no warning");
    }

    // ---------- S-N5 : a native Linux host starts a Vibe row ----------

    /// <summary>S-N5: the native twin of <c>AVibeLaunchOnWslLinksTheSignInTests</c> — a read, non-WSL banner STARTS the row, the owned home holds <c>.env</c> as a link to the person's file, and `--version` is asked in the five-name environment.</summary>
    [Fact]
    public async Task ANativeLinuxHost_WithASignIn_Starts_AndTheOwnedHomeHoldsTheEnvLink()
    {
        var disk = new FakeSignInDisk();
        var host = NativeLinux(disk);
        var runtime = new VibeRuntime("vibe", host, VibePathBudget.None);
        var invocation = Invocation(runtime);
        var vibe = new FakeVibe();

        using var turn = await VibeLaunch.TakeAsync(host, invocation.Request, FakeVibe.ExportOf(invocation.Request), vibe, VibePathBudget.None, TestContext.Current.CancellationToken);

        turn.Refusal.Should().BeEmpty("a native Linux host should start a Vibe row now that it is measured (RESULTS_vibe §32), but it was refused");
        disk.Links.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>(Path.Combine(invocation.Request.Environment[VibeLaunch.HomeVariable]!, ".env"), Persons));
        vibe.Started.Should().ContainSingle().Which.Environment[VibeLaunch.KeyringBackendVariable].Should().Be(Fail, "the version is asked in the launch's own environment");
        turn.Warning.Should().BeEmpty("a linked sign-in and a measured version carry no warning");
    }

    private static VibeHost NativeLinux(FakeSignInDisk disk) => new(HostKind.Linux, WslGeneration.None, new VibeSignIn(Persons) { Steps = disk.Steps });

    private ReviewerInvocation Invocation(VibeRuntime runtime) =>
        runtime.Build(RoleCatalog.ArchitectureRole, "review this", _work, "/s.json", _scratch,
            new ReviewerSettings("vibe") { Model = "glm-5-3", OwnedScratch = _scratch, Timeout = TimeSpan.FromMinutes(1) });

    private Task<LaunchTurn> TakeAsync(VibeHost host, VibeRuntime runtime, FakeVibe vibe)
    {
        var invocation = Invocation(runtime);

        return VibeLaunch.TakeAsync(host, invocation.Request, FakeVibe.ExportOf(invocation.Request), vibe, VibePathBudget.None, TestContext.Current.CancellationToken);
    }
}
