using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The person's OWN Kimi beside a coai launch (decision 74 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>, the epic 4 code gate): coai's sign-in locks
/// serialise coai's launches only, so a <c>kimi</c> the person runs near the token's expiry could refresh the rotating token at the same time as coai's
/// agent. Before the hold is taken and the agent started the shim looks — through a fake process list here — and waits within the deadline.
/// </summary>
public sealed class ForeignKimiTests
{
    private const int Shim = 100;
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);

    private static ProcessEntry P(int pid, int parent, string name) => new(pid, parent, name);

    /// <summary>A process list that answers the scripted listings in order, the last one for ever.</summary>
    private sealed class ScriptedList(params IReadOnlyList<ProcessEntry>[] listings) : IProcessList
    {
        public int Asked { get; private set; }

        public IReadOnlyList<ProcessEntry> Snapshot() => listings[Math.Min(Asked++, listings.Length - 1)];
    }

    private static KimiPlaces Places() => KimiPlaces.ForShim(Path.Combine(Path.GetTempPath(), "testsToDelete-foreign-kimi-not-made"), Path.GetTempPath());

    private static ForeignKimiWatch Watch(IProcessList list) => Watch(list, []);

    private static ForeignKimiWatch Watch(IProcessList list, List<TimeSpan> slept) => new(list, Shim, Poll, slept.Add);

    // ---------- who is coai's own ----------

    [Fact]
    public void AKimiStartedByThePersonsTerminal_IsForeign()
    {
        var all = new[] { P(1, 0, "explorer.exe"), P(2, 1, "pwsh.exe"), P(4242, 2, "kimi.exe"), P(Shim, 50, "coai-mcp.exe") };

        ForeignKimiWatch.Foreign(all, Shim).Select(p => p.Pid).Should().Equal(4242);
    }

    [Fact]
    public void TheShimsOwnAgentTree_IsNotCounted_AtAnyDepth()
    {
        var all = new[] { P(Shim, 50, "coai-mcp.exe"), P(200, Shim, "kimi.exe"), P(300, 200, "cmd.exe"), P(301, 300, "kimi.exe") };

        ForeignKimiWatch.Foreign(all, Shim).Should().BeEmpty("a kimi whose parent chain reaches this shim is coai's own, a grandchild included");
    }

    [Fact]
    public void AnEarlierTreeUnderAnotherCoaiMcp_IsNotCounted()
    {
        // The sign-in's tree hold already serialises another shim's tree; counting it as the person's would only change the sentence.
        var all = new[] { P(900, 1, "coai-mcp.exe"), P(901, 900, "COAI-MCP.EXE"), P(902, 901, "kimi.exe") };

        ForeignKimiWatch.Foreign(all, Shim).Should().BeEmpty();
    }

    [Fact]
    public void AKimiWhoseParentHasEnded_IsCountedAsThePersons_BecauseItCanStillRefreshTheToken()
    {
        ForeignKimiWatch.Foreign([P(777, 31337, "kimi.exe")], Shim).Select(p => p.Pid).Should().Equal(777);
    }

    [Fact]
    public void OnlyAProcessNamedKimiCounts_InAnyCase_AndACycleInTheListingEnds()
    {
        var all = new[] { P(5, 6, "kimi-helper.exe"), P(6, 5, "node.exe"), P(7, 8, "KIMI.EXE"), P(8, 7, "pwsh.exe") };

        ForeignKimiWatch.Foreign(all, Shim).Select(p => p.Pid).Should().Equal(7);
    }

    // ---------- the wait ----------

    [Fact]
    public void WithNoKimiRunning_NothingIsWaitedFor()
    {
        var slept = new List<TimeSpan>();
        var waited = Watch(new ScriptedList([P(1, 0, "explorer.exe")]), slept).Wait(TimeSpan.FromSeconds(30));

        waited.Still.Should().BeEmpty();
        waited.Waited.Should().Be(TimeSpan.Zero);
        slept.Should().BeEmpty();
    }

    [Fact]
    public void AForeignKimiThatEndsWithinTheDeadline_IsWaitedOut_AndTheWayIsClear()
    {
        var running = new[] { P(4242, 2, "kimi.exe") };
        var gone = new[] { P(2, 1, "pwsh.exe") };
        var slept = new List<TimeSpan>();

        var waited = Watch(new ScriptedList(running, running, gone), slept).Wait(TimeSpan.FromSeconds(30));

        waited.Still.Should().BeEmpty();
        waited.Waited.Should().Be(TimeSpan.FromSeconds(2));
        slept.Should().Equal(Poll, Poll);
    }

    [Fact]
    public void AForeignKimiStillRunningAtTheDeadline_IsNamed_AndWaitedForNoLongerThanTheDeadline()
    {
        var slept = new List<TimeSpan>();

        var waited = Watch(new ScriptedList([P(4242, 2, "kimi.exe")]), slept).Wait(TimeSpan.FromSeconds(3));

        waited.Still.Select(p => p.Pid).Should().Equal(4242);
        waited.Waited.Should().Be(TimeSpan.FromSeconds(3));
        slept.Should().HaveCount(3);
    }

    [Fact]
    public void TheSentence_IsThePersonsOwn_NamingThePid()
    {
        ForeignKimiWatch.Sentence(P(4242, 2, "kimi.exe")).Should()
            .Be("your own Kimi Code is running (pid 4242); coai does not start a second Kimi on the same sign-in while it runs, so two refreshes never race — close it or wait");
    }

    // ---------- the shim's hold ----------

    [Fact]
    public void AForeignKimi_MakesTheTurnBusy_BeforeAnyHoldIsTaken()
    {
        // The owned root here is not even made: reaching the junction check would answer Unusable, so Busy proves the look came first.
        var holding = KimiShimHold.Taken(Places(), TimeSpan.FromSeconds(2), Watch(new ScriptedList([P(4242, 2, "kimi.exe")])));

        holding.Should().BeOfType<AcpHolding.Busy>().Which.Sentence.Should().Contain("your own Kimi Code is running (pid 4242)");
    }

    [Fact]
    public void AForeignKimiThatEndsInTime_LetsTheTurnGoOn_ToTheNextCheck()
    {
        var running = new[] { P(4242, 2, "kimi.exe") };
        var holding = KimiShimHold.Taken(Places(), TimeSpan.FromSeconds(5), Watch(new ScriptedList(running, [P(2, 1, "pwsh.exe")])));

        holding.Should().BeOfType<AcpHolding.Unusable>("the wait is over, so the hold's own first check ran — and the not-made root has no junction")
            .Which.Sentence.Should().Contain("has no credentials junction");
    }

    [Fact]
    public void OnlyCoaisOwnKimiChildren_DoNotStopTheTurn()
    {
        var own = new[] { P(Shim, 50, "coai-mcp.exe"), P(200, Shim, "kimi.exe") };
        var holding = KimiShimHold.Taken(Places(), TimeSpan.FromSeconds(5), Watch(new ScriptedList(own)));

        holding.Should().BeOfType<AcpHolding.Unusable>().Which.Sentence.Should().NotContain("your own Kimi Code");
    }

    [Fact]
    public void TheKimiProfileAsksItsWatch_NotTheHost()
    {
        var profile = new KimiAcpProfile(Watch(new ScriptedList([P(4242, 2, "kimi.exe")])));
        var ask = new AcpAsk("kimi.exe", Path.GetTempPath(), "kimi-code/k3-256k", "x", TimeSpan.FromSeconds(2), Path.Combine(Path.GetTempPath(), "testsToDelete-foreign-kimi-not-made"), Path.GetTempPath());

        profile.Hold(ask, TimeSpan.FromSeconds(2)).Should().BeOfType<AcpHolding.Busy>();
    }

    [Fact]
    public void TheHostsOwnListing_NamesThisProcess_WithItsParent()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the host listing is Windows' toolhelp snapshot; other hosts never run Kimi (D18)");
        var me = HostProcessList.Current.Snapshot().FirstOrDefault(p => p.Pid == Environment.ProcessId);

        me.Should().NotBeNull();
        me!.ParentPid.Should().BePositive();
        me.Name.Should().NotBeEmpty();
    }
}
