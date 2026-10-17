using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The one reader of a <c>/proc/&lt;pid&gt;/stat</c> line (DL10 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>), extracted from <c>ProcessTree</c>: pid, parent, <c>comm</c> and
/// state, over fake lines and a fake <see cref="IProcReads"/>. The name field is the trap — the kernel prints any thread name between brackets, spaces and brackets
/// included — and a process that has exited (<c>Z</c>, <c>X</c>) must read as that. <c>TheTreeIsReadByIdentityTests</c> stays green unchanged: that is the proof nothing moved.
/// </summary>
public sealed class ProcStatTests
{
    private const string Tail = "1234 1234 0 -1 4194560 100 0 0 0 1 2 0 0 20 0 1 0 4000 1000 100 18446744073709551615";

    private static ProcStatLine Line(string text, int pid = 1234) =>
        ProcStat.Parse(pid, text).Should().BeOfType<ProcStatRead.Read>($"'{text}' is a stat line").Subject.Line;

    [Fact]
    public void APlainLine_IsPidNameStateAndParent() =>
        Line($"1234 (bash) S 1 {Tail}").Should().Be(new ProcStatLine(1234, "bash", "S", 1, StartTicks: 4000));

    [Fact]
    public void TheStartTicks_AreField22_OfTheSameLine()
    {
        Line($"1234 (bash) S 1 {Tail}").StartTicks.Should().Be(4000, "field 22 of the kernel's line: 'starttime', in ticks after boot");
        Line("1234 (a b) S 1 1234 1234 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 18446744073709551615 9 9", 1234).StartTicks.Should().Be(ulong.MaxValue);
        Line("1234 (bash) S 1 1234", 1234).StartTicks.Should().Be(0, "a line that stops short of field 22 carries no ticks — never a guess");
    }

    [Fact]
    public void ANameWithSpaces_IsOneName() =>
        Line($"42 (Web Content) R 7 {Tail}", 42).Should().Be(new ProcStatLine(42, "Web Content", "R", 7, StartTicks: 4000));

    [Theory]
    [InlineData("99 (bash) S 1 1 1 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 4000 1")]
    [InlineData("x (bash) S 1 1 1 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 4000 1")]
    [InlineData("1234x (bash) S 1 1 1 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 4000 1")]
    public void ALineWhoseLeadingPidIsNotTheOneItWasReadFor_IsUnreadable(string text) =>
        ProcStat.Parse(1234, text).Should().BeOfType<ProcStatRead.Unreadable>("the leading field of /proc/1234/stat is 1234; a line from another process is not this one's");

    [Theory]
    [InlineData("a)b")]
    [InlineData("(x)")]
    [InlineData("x) S 5 (y")]
    [InlineData("tab\tname")]
    public void ANameWithBrackets_IsCutAtTheLastClosingBracket_AndTheFieldsAreCountedFromThere(string comm) =>
        Line($"42 ({comm}) S 9 {Tail}", 42).Should().Be(new ProcStatLine(42, comm, "S", 9, StartTicks: 4000), "the parent is 9, not the 5 inside the name");

    [Fact]
    public void AnEmptyName_IsAnEmptyName() =>
        Line($"42 () S 3 {Tail}", 42).Comm.Should().BeEmpty();

    [Fact]
    public void KimisAcpAgent_ReadsMainThread_NotKimi() =>
        Line($"3141 (MainThread) S 2718 {Tail}", 3141).Comm.Should().Be("MainThread", "L-K6: a running kimi acp is named by the kernel after its thread, so a caller recognises it by its exe, not by this");

    [Theory]
    [InlineData("Z")]
    [InlineData("X")]
    [InlineData("S")]
    [InlineData("R")]
    [InlineData("D")]
    [InlineData("T")]
    public void TheStateLetter_IsRead(string state) =>
        Line($"77 (worker) {state} 5 {Tail}", 77).State.Should().Be(state);

    [Fact]
    public void AnExitedEntry_ReadsAsZombie_WithItsParentStillNamed()
    {
        var zombie = Line($"900 (sleep) Z 4 {Tail}", 900);

        zombie.State.Should().Be("Z");
        zombie.ParentPid.Should().Be(4, "the parent that has not reaped it is still the one the table names");
        ProcessTree.ExitedEntry(zombie.State, [900], zombie.Pid).Should().BeTrue("the ExitedEntry rule of DL9 reads the same state letter");
        ProcessTree.ExitedEntry("X", [900], 900).Should().BeTrue();
        ProcessTree.ExitedEntry("Z", [900, 901], 900).Should().BeFalse("another thread of the group still runs");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no brackets here")]
    [InlineData("(bash) S 1")]
    [InlineData("12 (bash S 1 2")]
    [InlineData("12 (bash)")]
    [InlineData("12 (bash) S")]
    [InlineData("12 (bash) S x 2")]
    [InlineData("12 (bash) SS 1 2")]
    public void ALineThatIsNotAStatLine_IsUnreadable_NeverAGuess(string text) =>
        ProcStat.Parse(12, text).Should().BeOfType<ProcStatRead.Unreadable>().Which.Reason.Should().Contain("not a /proc stat line");

    [Fact]
    public void ReadFor_IsGoneForAProcessNotThere_AndUnreadableForEveryOtherFailure()
    {
        var proc = new FakeProcReads();
        proc.Stats[10] = () => throw new UnauthorizedAccessException("EACCES");
        proc.Stats[11] = () => throw new IOException("EIO");
        proc.Stats[12] = () => throw new FileNotFoundException("ENOENT");
        proc.Stats[13] = () => "garbage";
        proc.Stats[14] = () => $"14 (ok) S 1 {Tail}";

        ProcStat.ReadFor(10, proc).Should().BeOfType<ProcStatRead.Unreadable>().Which.Reason.Should().Contain("UnauthorizedAccessException").And.Contain("/proc/10/stat");
        ProcStat.ReadFor(11, proc).Should().BeOfType<ProcStatRead.Unreadable>().Which.Reason.Should().Contain("IOException");
        ProcStat.ReadFor(12, proc).Should().BeOfType<ProcStatRead.Gone>("ENOENT: the process ended before the read");
        ProcStat.ReadFor(99, proc).Should().BeOfType<ProcStatRead.Gone>("its directory is not there");
        ProcStat.ReadFor(13, proc).Should().BeOfType<ProcStatRead.Unreadable>("a line it cannot parse is not evidence of anything");
        ProcStat.ReadFor(14, proc).Should().Be(new ProcStatRead.Read(new ProcStatLine(14, "ok", "S", 1, StartTicks: 4000)));
    }

    [Fact]
    public void ThisProcess_ReadsAsItself_OnLinux()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "/proc is Linux's");

        var line = ProcStat.ReadFor(Environment.ProcessId, ProcReads.Real).Should().BeOfType<ProcStatRead.Read>().Subject.Line;

        line.Pid.Should().Be(Environment.ProcessId);
        line.Comm.Should().NotBeEmpty();
        line.StartTicks.Should().BeGreaterThan(0, "the real kernel's field 22 is the process's start in ticks after boot");
        line.State.Should().BeOneOf("R", "S", "D");
        ProcStat.ReadFor(int.MaxValue, ProcReads.Real).Should().BeOfType<ProcStatRead.Gone>("no process has that number");
        ProcReads.Real.Pids().Should().Contain(Environment.ProcessId);
    }

    [Fact]
    public void TheRealReads_OfThisProcess_AnswerOnLinux()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "/proc is Linux's");
        var reads = ProcReads.Real;

        reads.OwnerUid(Environment.ProcessId).Should().Be(reads.OwnUid(), "this process is owned by the account it runs as");
        reads.Threads(Environment.ProcessId).Should().Contain(Environment.ProcessId, "the leader is one of its own threads");
        reads.Threads(int.MaxValue).Should().Equal(-1);
        Path.GetFileName(reads.ExeTarget(Environment.ProcessId)).Should().NotBeEmpty("a running test host has an executable: dotnet, or its own apphost");
        reads.ExeTarget(int.MaxValue).Should().BeEmpty("ENOENT: no executable");
        FluentActions.Invoking(() => reads.OwnerUid(int.MaxValue)).Should().Throw<IOException>();
    }

    [Fact]
    public void TheRealCmdline_OfThisProcess_HoldsItsArgvZero_AndAGonePidIsGone()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "/proc is Linux's");

        var cmdline = ProcReads.Real.Cmdline(Environment.ProcessId);

        cmdline.Split((char)0)[0].Should().NotBeEmpty("a running test host was started with an argv[0]");
        cmdline.Should().EndWith(((char)0).ToString(), "every argument of a cmdline is NUL-ended");
        FluentActions.Invoking(() => ProcReads.Real.Cmdline(int.MaxValue)).Should().Throw<IOException>("a pid that is not there is gone (a DirectoryNotFoundException or a FileNotFoundException)")
            .Which.GetType().Should().Match(type => type == typeof(DirectoryNotFoundException) || type == typeof(FileNotFoundException));
    }
}
