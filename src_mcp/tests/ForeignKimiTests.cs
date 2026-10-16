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
    private sealed class ScriptedList(params ProcessListing[] listings) : IProcessList
    {
        public int Asked { get; private set; }

        public ScriptedList(params IReadOnlyList<ProcessEntry>[] listed)
            : this([.. listed.Select(processes => (ProcessListing)new ProcessListing.Listed(processes))])
        {
        }

        public ProcessListing Snapshot() => listings[Math.Min(Asked++, listings.Length - 1)];
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

        waited.Should().Be(new ForeignKimiAnswer.Clear(TimeSpan.Zero));
        slept.Should().BeEmpty();
    }

    [Fact]
    public void AForeignKimiThatEndsWithinTheDeadline_IsWaitedOut_AndTheWayIsClear()
    {
        var running = new[] { P(4242, 2, "kimi.exe") };
        var gone = new[] { P(2, 1, "pwsh.exe") };
        var slept = new List<TimeSpan>();

        var waited = Watch(new ScriptedList(running, running, gone), slept).Wait(TimeSpan.FromSeconds(30));

        waited.Should().Be(new ForeignKimiAnswer.Clear(TimeSpan.FromSeconds(2)));
        slept.Should().Equal(Poll, Poll);
    }

    [Fact]
    public void AForeignKimiStillRunningAtTheDeadline_IsNamed_AndWaitedForNoLongerThanTheDeadline()
    {
        var slept = new List<TimeSpan>();

        var waited = Watch(new ScriptedList([P(4242, 2, "kimi.exe")]), slept).Wait(TimeSpan.FromSeconds(3));

        var running = waited.Should().BeOfType<ForeignKimiAnswer.Running>().Subject;
        running.Still.Select(p => p.Pid).Should().Equal(4242);
        running.Waited.Should().Be(TimeSpan.FromSeconds(3));
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
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the host listing is Windows' toolhelp snapshot here; Linux's is HostProcessListTests'");
        var me = HostProcessList.Current.Snapshot().Should().BeOfType<ProcessListing.Listed>().Subject.Processes.FirstOrDefault(p => p.Pid == Environment.ProcessId);

        me.Should().NotBeNull();
        me!.ParentPid.Should().BePositive();
        me.Name.Should().NotBeEmpty();
    }

    // ---------- Linux: the listing over /proc (DL10; S-K7, S-K13, S-K13b) ----------

    private const string KimiExe = "/home/me/.kimi-code/bin/kimi";

    private static ForeignKimiWatch OnLinux(FakeProcReads proc, List<TimeSpan> slept) => new(new LinuxProcessListing(proc, Shim), Shim, Poll, slept.Add);

    /// <summary>The shim, its ancestors, and one stranger's <c>kimi acp</c> (comm <c>MainThread</c>, as measured) under a shell under node.</summary>
    private static FakeProcReads ATerminalsKimi() => new FakeProcReads()
        .Running(1, 0, "/sbin/init", "init")
        .Running(Shim, 1, "/usr/local/bin/coai-mcp", "coai-mcp")
        .Running(301, 1, "/usr/bin/node", "MainThread")
        .Running(300, 301, "/usr/bin/bash", "bash")
        .Running(4242, 300, KimiExe);

    [Fact]
    public void ALinuxKimiWithNoCoaiMcpInItsChain_MakesTheWaitWait_ThenBusy()
    {
        var slept = new List<TimeSpan>();

        var answer = OnLinux(ATerminalsKimi(), slept).Wait(TimeSpan.FromSeconds(3));

        answer.Should().BeOfType<ForeignKimiAnswer.Running>("the Linux listing found no process — the foreign-Kimi wait never waits").Which.Still.Select(p => p.Pid).Should().Equal(4242);
        slept.Should().HaveCount(3, "a kimi that never ends is waited for the whole deadline");
    }

    [Fact]
    public void TheShimsOwnKimi_OnLinux_IsNotForeign()
    {
        var proc = new FakeProcReads().Running(1, 0, "/sbin/init", "init").Running(Shim, 1, "/usr/local/bin/coai-mcp", "coai-mcp").Running(200, Shim, KimiExe).Running(201, 200, KimiExe);

        OnLinux(proc, []).Wait(TimeSpan.FromSeconds(3)).Should().BeOfType<ForeignKimiAnswer.Clear>("a kimi whose parent chain reaches this shim is coai's, a grandchild kimi included");
    }

    [Fact]
    public void ATuiWhoseCommIsKimi_IsKimiByItsSecondWitness()
    {
        var proc = new FakeProcReads().Running(1, 0, "/sbin/init", "init").Running(Shim, 1, "/usr/local/bin/coai-mcp", "coai-mcp").Running(500, 1, "/usr/bin/wrapped-binary", "kimi");

        OnLinux(proc, []).Wait(TimeSpan.Zero).Should().BeOfType<ForeignKimiAnswer.Running>("a process whose comm is kimi is the TUI as the first scout saw it");
    }

    [Fact]
    public void AnUnreadableListing_IsBusyWithTheReason_NeverClear()
    {
        var proc = ATerminalsKimi();
        proc.Stats[777] = () => throw new UnauthorizedAccessException("denied");
        var slept = new List<TimeSpan>();

        var answer = OnLinux(proc, slept).Wait(TimeSpan.FromSeconds(30));

        answer.Should().BeOfType<ForeignKimiAnswer.Unreadable>("an unreadable process listing was read as 'nobody is running'").Which.Reason.Should().Contain("/proc/777/stat");
        slept.Should().BeEmpty("an unreadable /proc does not become readable by waiting: the wait ends at once");
    }

    [Fact]
    public void ASameUserProcessWhoseExeCannotBeRead_MakesTheListingUnreadable()
    {
        var proc = ATerminalsKimi();
        proc.With(888, "S", 1);
        proc.Exes[888] = () => throw new UnauthorizedAccessException("denied");

        new LinuxProcessListing(proc, Shim).Snapshot().Should().BeOfType<ProcessListing.Unreadable>().Which.Reason.Should().Contain("/proc/888/exe");
    }

    [Fact]
    public void AnotherAccountsProcess_IsSkipped_NotCountedUnreadable()
    {
        var proc = ATerminalsKimi();
        proc.With(1000, "S", 1);
        proc.Owners[1000] = 0;
        proc.Exes[1000] = () => throw new UnauthorizedAccessException("denied");

        new LinuxProcessListing(proc, Shim).Snapshot().Should().BeOfType<ProcessListing.Listed>("another account's process cannot refresh this person's 0700 folder, and its exe answers EACCES for everyone")
            .Which.Processes.Select(p => p.Pid).Should().NotContain(1000);
    }

    [Fact]
    public void AListingWithoutTheShimsOwnPid_IsUnreadable()
    {
        var proc = new FakeProcReads().Running(1, 0, "/sbin/init", "init");

        new LinuxProcessListing(proc, Shim).Snapshot().Should().BeOfType<ProcessListing.Unreadable>().Which.Reason.Should().Contain("own pid");
    }

    [Fact]
    public void AProcDirectoryThatCannotBeListed_IsUnreadable()
    {
        var proc = new FakeProcReads { Listed = () => throw new IOException("no /proc") };

        new LinuxProcessListing(proc, Shim).Snapshot().Should().BeOfType<ProcessListing.Unreadable>().Which.Reason.Should().Contain("/proc could not be listed").And.Contain("IOException");
    }

    [Fact]
    public void ADeletedKimiBinary_IsStillKimi_AndAnUnrelatedZombie_IsSkipped()
    {
        var proc = ATerminalsKimi();
        proc.Exes[4242] = () => KimiExe + " (deleted)";
        proc.With(999, "Z", 1);
        proc.Exes[999] = () => throw new UnauthorizedAccessException("a zombie's exe cannot be read");

        var listing = new LinuxProcessListing(proc, Shim).Snapshot().Should().BeOfType<ProcessListing.Listed>("an unrelated zombie made the Kimi launch Busy").Subject;

        listing.Processes.Single(p => p.Pid == 4242).Name.Should().Be("kimi", "a running kimi whose binary was replaced was not seen");
        listing.Processes.Select(p => p.Pid).Should().NotContain(999);
        ForeignKimiWatch.Foreign(listing.Processes, Shim).Select(p => p.Pid).Should().Equal(4242);
    }

    [Fact]
    public void AProcessThatLeftBetweenTheListingAndTheRead_IsSkipped_AndOnlyOneDeletedSuffixIsStripped()
    {
        var proc = new FakeProcReads { Listed = () => [1, 2, Shim, 42] }
            .Running(1, 0, "/sbin/init", "init")
            .Running(2, 1, "/x/kimi (deleted) (deleted)")
            .Running(Shim, 1, "/usr/local/bin/coai-mcp", "coai-mcp");

        var listing = new LinuxProcessListing(proc, Shim).Snapshot().Should().BeOfType<ProcessListing.Listed>("pid 42 left between the listing and the read — never evidence of anything").Subject;

        listing.Processes.Single(p => p.Pid == 2).Name.Should().Be("kimi (deleted)", "ONE trailing ' (deleted)' is stripped, not every one");
    }

    [Fact]
    public void AnUnreadableListing_MakesTheShimsHoldBusy_WithTheSentence()
    {
        var holding = KimiShimHold.Taken(Places(), TimeSpan.FromSeconds(2), new ForeignKimiWatch(new ScriptedList(new ProcessListing.Unreadable("/proc/9/stat: denied")), Shim, Poll, _ => { }));

        holding.Should().BeOfType<AcpHolding.Busy>().Which.Sentence.Should().Be(ForeignKimiWatch.UnreadableSentence("/proc/9/stat: denied"));
        ForeignKimiWatch.UnreadableSentence("x").Should().Contain("cannot tell whether your own Kimi Code is running").And.Contain("DL10 of todo/PLAN_kimi_and_vibe_on_linux.md");
    }
}
