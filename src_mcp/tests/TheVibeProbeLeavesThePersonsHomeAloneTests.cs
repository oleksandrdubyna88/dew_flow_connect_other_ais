using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>providers</c> health probe asks Vibe its version WITHOUT touching the person's own <c>~/.vibe</c> (D17 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>: the person's home gains nothing from coai). A bare <c>vibe --version</c>
/// writes <c>vibehistory</c> into whatever <c>VIBE_HOME</c> it is given — the person's own when it is given none
/// (research/RESULTS_vibe_confinement.md §24) — so the probe runs in the launch control's BUILT environment on a
/// coai-owned home made for the probe and deleted after it.
/// </summary>
public sealed class TheVibeProbeLeavesThePersonsHomeAloneTests
{
    /// <summary>Records the probe's request, and checks while it runs that its home exists.</summary>
    private sealed class Recording : IProcessLauncher
    {
        public List<ProcessRequest> Asked { get; } = [];

        public bool HomeExistedWhileItRan { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Asked.Add(request);
            HomeExistedWhileItRan = request.Environment.TryGetValue(VibeLaunch.HomeVariable, out var home)
                && home is { Length: > 0 } && Directory.Exists(home);

            return Task.FromResult(new ProcessResult(0, "python.exe C:\\Users\\p\\.local\\bin\\vibe 2.26.1\r\n", string.Empty, TimedOut: false));
        }
    }

    private static async Task<(VendorHealth Health, Recording Launcher)> ProbeAsync()
    {
        var launcher = new Recording();
        var health = await VendorProbe.RunAsync(launcher, new VendorIdentity("vibe", "vibe", string.Empty), enabled: true,
            executablePath: "vibe", model: "glm-5-3", hasVaultKey: false, TestContext.Current.CancellationToken);

        return (health, launcher);
    }

    [Fact]
    public async Task TheProbe_RunsInTheBuiltEnvironment_OnAHomeOfCoaisOwn()
    {
        var (health, launcher) = await ProbeAsync();

        health.Version.Should().Contain("2.26.1");
        var asked = launcher.Asked.Should().ContainSingle().Subject;
        asked.Arguments.Should().Equal("--version");
        asked.InheritsEnvironment.Should().BeFalse("an inherited environment gives Vibe the person's own home");
        VibeLaunch.EnvironmentRefusal(asked).Should().BeEmpty("the probe is built exactly as a launch is (D17)");
        var home = asked.Environment[VibeLaunch.HomeVariable]!;
        home.Should().NotStartWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vibe"));
        launcher.HomeExistedWhileItRan.Should().BeTrue("Vibe is handed a home that is there");
    }

    [Fact]
    public async Task TheProbesHome_IsGoneAfterIt()
    {
        var (_, launcher) = await ProbeAsync();

        Directory.Exists(launcher.Asked.Single().Environment[VibeLaunch.HomeVariable]).Should().BeFalse("the probe's own home is deleted after it");
    }

    [Fact]
    public async Task EveryOtherRuntime_IsProbedAsBefore()
    {
        var launcher = new Recording();

        await VendorProbe.RunAsync(launcher, new VendorIdentity("codex", "codex", string.Empty), enabled: true,
            executablePath: "codex", model: string.Empty, hasVaultKey: false, TestContext.Current.CancellationToken);

        var asked = launcher.Asked.Should().ContainSingle().Subject;
        asked.InheritsEnvironment.Should().BeTrue();
        asked.Environment.Should().BeEmpty();
    }
}
