using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// DL10's re-decision of 2026-10-17 (<c>research/PLAN_kimi_and_vibe_on_linux.md</c>): Linux reads <c>/proc/&lt;pid&gt;/exe</c> under <c>PTRACE_MODE_READ_FSCREDS</c>, so a SAME-account process can still hide it — one with extra
/// capabilities, or a non-dumpable one. The ubuntu runner has <c>systemd --user</c> hiding it, and so does the owner's WSL2 (<c>systemd</c> and <c>(sd-pam)</c>). Every Kimi launch there would be Busy, which is "refuse
/// everything", not "refuse only what cannot be made safe". So the identity is read from <c>exe</c>, then — when it is denied — from <c>argv[0]</c> and <c>comm</c>; only a process whose <c>exe</c> AND <c>cmdline</c> are
/// both unreadable makes the listing unreadable. The three shapes measured, on the fake <c>/proc</c>.
/// </summary>
public sealed class LinuxHiddenExeTests
{
    private const int Shim = 100;

    private static FakeProcReads Machine() => new FakeProcReads()
        .Running(1, 0, "/sbin/init", "init")
        .Running(Shim, 1, "/usr/local/bin/coai-mcp", "coai-mcp");

    private static ProcessListing Listing(FakeProcReads proc) => new LinuxProcessListing(proc, Shim).Snapshot();

    private static IReadOnlyList<ProcessEntry> Listed(FakeProcReads proc) =>
        Listing(proc).Should().BeOfType<ProcessListing.Listed>("a hidden exe with a readable cmdline made the whole listing unreadable").Subject.Processes;

    [Fact]
    public void AHiddenExe_WhoseCmdlineNamesSystemd_IsNotKimi_AndDoesNotMakeTheListingUnreadable()
    {
        var proc = Machine().Hidden(700, 1, ["/usr/lib/systemd/systemd", "--user"], comm: "systemd");

        var processes = Listed(proc);

        processes.Single(entry => entry.Pid == 700).Name.Should().Be("systemd", "the identity is the basename of argv[0] when the exe is denied");
        ForeignKimiWatch.Foreign(processes, Shim).Should().BeEmpty("systemd --user is not a Kimi");
        KimiPresence.Of(new Fixed(Listing(proc))).Should().BeOfType<KimiPresence.NoKimi>();
    }

    [Fact]
    public void ANonDumpableSdPam_WithNoCapabilities_IsReadTheSameWay()
    {
        var proc = Machine().Hidden(701, 1, ["(sd-pam)"], comm: "(sd-pam)");

        Listed(proc).Single(entry => entry.Pid == 701).Name.Should().Be("(sd-pam)");
    }

    [Theory]
    [InlineData("kimi")]
    [InlineData("/home/me/.local/bin/kimi")]
    public void AHiddenExe_WhoseCmdlineNamesKimi_IsKimi_ByItsArgvZero(string argv0)
    {
        var proc = Machine().Hidden(4242, 1, [argv0, "acp"]);

        var answer = new ForeignKimiWatch(new Fixed(Listing(proc)), Shim, TimeSpan.FromSeconds(1), _ => { }).Wait(TimeSpan.Zero);

        answer.Should().BeOfType<ForeignKimiAnswer.Running>("a Kimi whose exe is hidden was not seen").Which.Still.Select(entry => entry.Pid).Should().Equal(4242);
        KimiPresence.Of(new Fixed(Listing(proc))).Should().BeOfType<KimiPresence.Running>().Which.Pids.Should().Equal(4242);
    }

    [Fact]
    public void AHiddenExe_WhoseCommIsKimi_IsKimi_AlthoughArgvZeroSaysNode()
    {
        var proc = Machine().Hidden(4243, 1, ["node", "app.js"], comm: "kimi");

        ForeignKimiWatch.Foreign(Listed(proc), Shim).Select(entry => entry.Pid).Should().Equal(4243);
    }

    [Fact]
    public void WhenBothTheExeAndTheCmdlineAreUnreadable_TheListingIsUnreadable_NamingThePidAndWhatFailed()
    {
        var proc = Machine().With(888, "S", 1);
        proc.Exes[888] = () => throw new UnauthorizedAccessException("/proc/888/exe: errno 13 (EACCES)");
        proc.Cmdlines[888] = () => throw new UnauthorizedAccessException("/proc/888/cmdline: denied");

        var reason = Listing(proc).Should().BeOfType<ProcessListing.Unreadable>("a process that could be a Kimi, and cannot be identified, was read as 'not a Kimi'").Subject.Reason;

        reason.Should().Contain("888").And.Contain("/proc/888/exe").And.Contain("/proc/888/cmdline");
    }

    [Fact]
    public void AHiddenExe_WhoseProcessLeftBeforeItsCmdlineWasRead_IsSkipped_NotUnreadable()
    {
        var proc = Machine().With(889, "S", 1);
        proc.Exes[889] = () => throw new UnauthorizedAccessException("/proc/889/exe: errno 13 (EACCES)");

        var processes = Listed(proc);

        processes.Select(entry => entry.Pid).Should().NotContain(889, "a process that left (ESRCH/ENOENT) is never evidence of anything");
    }

    [Fact]
    public void AnExeThatFailsForAnyOtherReason_FallsBackToArgvZeroToo()
    {
        var proc = Machine().With(890, "S", 1);
        proc.Exes[890] = () => throw new IOException("/proc/890/exe: errno 5");
        proc.Cmdlines[890] = () => "/opt/kimi/kimi\0acp\0";

        ForeignKimiWatch.Foreign(Listed(proc), Shim).Select(entry => entry.Pid).Should().Equal(890);
    }

    [Fact]
    public void ADeletedSuffixIsStrippedFromTheExeOnly_NeverFromArgvZero()
    {
        var proc = Machine().Hidden(891, 1, ["/opt/kimi (deleted)"]);

        Listed(proc).Single(entry => entry.Pid == 891).Name.Should().Be("kimi (deleted)", "the kernel appends ' (deleted)' to an exe link, never to a process's own argv[0]");
    }

    [Fact]
    public void AHiddenExe_WithAnEmptyCmdline_IsListed_AndIdentifiedByCommAlone()
    {
        var proc = Machine().With(892, "S", 1, comm: "worker");
        proc.Exes[892] = () => throw new UnauthorizedAccessException("denied");
        proc.Cmdlines[892] = () => string.Empty;

        var entry = Listed(proc).Single(process => process.Pid == 892);

        (entry.Name, entry.Comm).Should().Be((string.Empty, "worker"));
    }

    private sealed class Fixed(ProcessListing listing) : IProcessList
    {
        public ProcessListing Snapshot() => listing;
    }
}
