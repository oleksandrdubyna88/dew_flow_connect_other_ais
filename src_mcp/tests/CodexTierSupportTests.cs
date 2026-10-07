using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Whether the installed codex can be told the standard tier is read off its own <c>--version</c>, on every ask, against
/// the range in <c>shared/feature-availability.json</c> (todo/PLAN_codex_tier_floor.md, change 3).
/// </summary>
/// <remarks>
/// <para>Why it is asked at all (research/RESULTS_codex_service_tier_versions_2026-10-07.md): codex 0.110.0 through 0.130.0
/// refuse <c>-c service_tier=default</c> at config load and fail the whole launch; 0.107.0 and older ignore the key, 0.131.0
/// and newer accept it.</para>
/// <para>Driven through the REAL fake CLI and the real launcher, as <see cref="VendorProbeTests"/> is: what is being tested
/// is what a process's streams, exit and silence MEAN — and that a hung one is actually gone afterwards, which a mocked
/// launcher could only agree with. In the <c>fakecli-env</c> collection because it steers the stand-in by environment.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class CodexTierSupportTests : IDisposable
{
    private static readonly string[] Steering =
        ["FAKECLI_MODE", "FAKECLI_VERSION_STDOUT", "FAKECLI_VERSION_EXIT", "FAKECLI_VERSION_SLEEP_MS", "FAKECLI_VERSION_COUNT", "FAKECLI_VERSION_PID"];

    private readonly string _dir = Directory.CreateTempSubdirectory("coai-codextier-").FullName;

    public CodexTierSupportTests() => Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");

    public void Dispose()
    {
        foreach (var name in Steering)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Directory.Delete(_dir, recursive: true);
    }

    private static void Answers(string version) => Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", version);

    private Task<CodexTierSupport> Probed(IProcessLauncher launcher, string exe = "", TimeSpan? timeout = null) =>
        CodexTierSupport.ProbeAsync(
            launcher, exe.Length > 0 ? exe : FakeCliInvocations.Exe, _dir, timeout ?? CodexTierSupport.ProbeTimeout, TestContext.Current.CancellationToken);

    private static bool Running(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Theory]
    [InlineData("codex-cli 0.110.0\n", StandardTier.RefusesStandard)]
    [InlineData("codex-cli 0.120.0\n", StandardTier.RefusesStandard)]
    [InlineData("codex-cli 0.130.0\n", StandardTier.RefusesStandard)]
    [InlineData("codex-cli 0.131.0\n", StandardTier.Accepts)]
    [InlineData("codex-cli 0.107.0\n", StandardTier.Accepts)]
    [InlineData("codex-cli 0.160.0\n", StandardTier.Accepts)]
    [InlineData("codex-cli 0.12.0\n", StandardTier.Accepts)]
    public async Task TheReleaseItPrints_IsReadAgainstTheFilesRange(string printed, StandardTier answer)
    {
        Answers(printed);

        var tier = await Probed(new ProcessLauncher());

        tier.Answer.Should().Be(answer, $"`{printed.Trim()}` against 0.110.0–0.130.0, compared as numbers");
        tier.RefusesStandard.Should().Be(answer == StandardTier.RefusesStandard);
    }

    [Fact]
    public async Task ItIsAskedForItsVersionAlone_UnderAShortCeiling()
    {
        Answers("codex-cli 0.160.0\n");
        var launcher = new WatchedLauncher(new ProcessLauncher());

        await CodexTierSupport.ProbeAsync(launcher, FakeCliInvocations.Exe, _dir, TestContext.Current.CancellationToken);

        var asked = launcher.Requests.Should().ContainSingle().Subject;
        asked.Arguments.Should().Equal(["--version"], "nothing but the version is asked: no prompt, no model, no bill");
        asked.Timeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(10), "measured 442–601 ms; a CLI that needs longer is not answering");
    }

    [Theory]
    [InlineData("codex version: who knows\n", "0", "did not name a release")]
    [InlineData("", "0", "did not name a release")]
    [InlineData("codex-cli 0.120.0\n", "2", "exited 2")]
    public async Task AVersionThatDidNotComeBackWhole_IsUnknown(string printed, string exit, string reason)
    {
        Answers(printed);
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_EXIT", exit);

        var tier = await Probed(new ProcessLauncher());

        tier.Answer.Should().Be(StandardTier.Unknown, "an answer nobody can read says nothing about the tier");
        tier.Reason.Should().Contain(reason);
    }

    [Fact]
    public async Task ACodexThatCannotBeStarted_IsUnknown_NotACrash()
    {
        var tier = await Probed(new ProcessLauncher(), exe: Path.Combine(_dir, "no-such-codex"));

        tier.Answer.Should().Be(StandardTier.Unknown);
        tier.Reason.Should().Contain("could not be started");
    }

    [Fact]
    public async Task ACodexThatHangs_IsUnknown_AndTheHungChildIsGone()
    {
        Answers("codex-cli 0.120.0\n");
        var pidFile = Path.Combine(_dir, "version.pid");
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_SLEEP_MS", "60000");
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_PID", pidFile);
        var clock = Stopwatch.StartNew();

        var tier = await Probed(new ProcessLauncher(), timeout: TimeSpan.FromMilliseconds(1500));

        tier.Answer.Should().Be(StandardTier.Unknown, "a release that never said itself is not read as one that refuses");
        tier.Reason.Should().Contain("did not answer");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the probe gave up at its ceiling, not when the child woke");
        File.Exists(pidFile).Should().BeTrue("the child must have started for its kill to mean anything");
        Running(int.Parse(File.ReadAllText(pidFile), System.Globalization.CultureInfo.InvariantCulture))
            .Should().BeFalse("the launcher kills the whole tree at the ceiling — nothing is left behind the launch");
    }

    [Fact]
    public async Task TheCliIsAskedEveryTime_SoAnUpgradeIsSeenOnTheNextLaunch()
    {
        // NO cache (the plan round's findings 0, 1, 2 and 5): an in-place upgrade under a long-lived server is seen at once.
        var count = Path.Combine(_dir, "asked.txt");
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_COUNT", count);
        Answers("codex-cli 0.120.0\n");

        (await Probed(new ProcessLauncher())).Answer.Should().Be(StandardTier.RefusesStandard, "0.120.0 is installed");
        Answers("codex-cli 0.131.0\n");
        (await Probed(new ProcessLauncher())).Answer.Should().Be(StandardTier.Accepts, "and the next launch sees the upgrade");

        File.ReadAllText(count).Should().Be("V\nV\n", "one --version per launch, never a remembered answer");
    }
}
