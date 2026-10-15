using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One Kimi launch of a round at a time (S4.9 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>, D19 until M-K14): the refresh token rotates, so two
/// reviewers of one round must not refresh one sign-in together. The scheduler asks the adapter (<see cref="IReviewerRuntime.LaunchesOneAtATime"/>),
/// never an <c>if</c> on a vendor name; every other provider keeps its per-provider cap.
/// </summary>
public sealed class OneKimiLaunchAtATimeTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-oneatatime-");

    public void Dispose() => _dir.Dispose();

    private static IReviewerRuntime Kimi() => new KimiReadSide(new KimiRuntime("kimi", HostKind.Windows, new KimiSeams(
        root => new KimiPlaces(root, "persons", "persons-bin", [], string.Empty, "kimi.exe"), () => ("coai-mcp.exe", []))));

    private ReviewerWork Work(string provider, string role, IReviewerRuntime adapter) =>
        new(new ReviewerInvocation(provider, role, new ProcessRequest(provider, [], _dir) { Timeout = TimeSpan.FromSeconds(30) }, string.Empty, adapter));

    [Fact]
    public async Task TwoKimiReviewersOfOneRound_NeverOverlap_WhileTwoOfAnyOtherVendorDo()
    {
        var kimi = Kimi();
        var plain = new PlainAdapter("vibe");
        var launcher = new ScriptedKimi(request => request.Executable == "vibe"
            ? new ProcessResult(0, KimiShimScripts.CleanReview, string.Empty, TimedOut: false)
            : KimiShimScripts.Answered(), delay: TimeSpan.FromMilliseconds(300));
        var work = new[]
        {
            Work("kimi", "architecture", kimi), Work("kimi", "security", kimi), Work("kimi", "tests", kimi),
            Work("vibe", "architecture", plain), Work("vibe", "security", plain),
        };
        var scheduler = new BoundedScheduler(globalCap: 5, perProviderCap: 2);

        var results = await scheduler.RunAllAsync(work, new ReviewerExecutor(launcher), TestContext.Current.CancellationToken);

        scheduler.PeakPerProvider["kimi"].Should().Be(1, "one Kimi launch at a time, whatever the per-provider cap says");
        scheduler.PeakPerProvider["vibe"].Should().Be(2, "every other vendor keeps its per-provider cap");
        results.Should().OnlyContain(result => result.Outcome is ReviewerOutcome.Ok, "all five answered");
    }

    /// <summary>E4.4 review M1: the lane is keyed by the ADAPTER, not by the provider id — two Kimi rows on one sign-in (or a settings reload that builds a new adapter) share it.</summary>
    [Fact]
    public async Task TwoKimiRowsOfDifferentIds_ShareOneLane()
    {
        var launcher = new ScriptedKimi(delay: TimeSpan.FromMilliseconds(300));
        var work = new[] { Work("kimi", "architecture", Kimi()), Work("my-kimi", "architecture", Kimi()), Work("other-kimi", "security", Kimi()) };
        var scheduler = new BoundedScheduler(globalCap: 5, perProviderCap: 2);

        await scheduler.RunAllAsync(work, new ReviewerExecutor(launcher), TestContext.Current.CancellationToken);

        launcher.PeakShims.Should().Be(1, "three rows, three ids, ONE sign-in: never two Kimi launches at once");
    }

    [Fact]
    public void OnlyKimiSaysSo()
    {
        ((IReviewerRuntime)new CodexRuntime()).LaunchesOneAtATime.Should().BeFalse();
        ((IReviewerRuntime)new Runners.Vibe.VibeRuntime()).LaunchesOneAtATime.Should().BeFalse();
        Kimi().LaunchesOneAtATime.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public void TheCapForAProvider_IsOneForAnAdapterThatLaunchesOneAtATime_ElseThePerProviderCap(bool oneAtATime, int expected)
    {
        IReviewerRuntime adapter = oneAtATime ? Kimi() : new PlainAdapter("x");

        BoundedScheduler.CapFor(Work("p", "r", adapter), perProviderCap: 2).Should().Be(expected);
    }
}
