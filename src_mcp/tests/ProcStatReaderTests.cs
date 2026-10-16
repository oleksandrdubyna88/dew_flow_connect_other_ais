using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The one reader of a <c>/proc/&lt;pid&gt;/stat</c> line (DL10 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>), extracted from <c>ProcessTree</c>:
/// pid, parent, <c>comm</c> and state, over fake lines. The name field is the trap — the kernel prints any thread name between brackets, spaces and
/// brackets included — and a process that has exited (<c>Z</c>, <c>X</c>) must read as that.
/// </summary>
public sealed class ProcStatReaderTests
{
    private const string Tail = "1234 1234 0 -1 4194560 100 0 0 0 1 2 0 0 20 0 1 0 4000 1000 100 18446744073709551615";

    private static ProcStat Stat(string line) =>
        ProcStatReader.Parse(line).Should().BeOfType<ProcStatReading.Read>($"'{line}' is a stat line").Subject.Stat;

    [Fact]
    public void APlainLine_IsPidNameStateAndParent() =>
        Stat($"1234 (bash) S 1 {Tail}").Should().Be(new ProcStat(1234, "bash", "S", 1));

    [Fact]
    public void ANameWithSpaces_IsOneName() =>
        Stat($"42 (Web Content) R 7 {Tail}").Should().Be(new ProcStat(42, "Web Content", "R", 7));

    [Theory]
    [InlineData("a)b", "a)b")]
    [InlineData("(x)", "(x)")]
    [InlineData("x) S 5 (y", "x) S 5 (y")]
    [InlineData("tab\tname", "tab\tname")]
    public void ANameWithBrackets_IsCutAtTheLastClosingBracket_AndTheFieldsAreCountedFromThere(string comm, string expected) =>
        Stat($"42 ({comm}) S 9 {Tail}").Should().Be(new ProcStat(42, expected, "S", 9), "the parent is 9, not the 5 inside the name");

    [Fact]
    public void AnEmptyName_IsAnEmptyName() =>
        Stat($"42 () S 3 {Tail}").Comm.Should().BeEmpty();

    [Fact]
    public void KimisAcpAgent_ReadsMainThread_NotKimi() =>
        Stat($"3141 (MainThread) S 2718 {Tail}").Comm.Should().Be("MainThread", "L-K6: a running kimi acp is named by the kernel after its thread, so a caller recognises it by its exe, not by this");

    [Theory]
    [InlineData("Z")]
    [InlineData("X")]
    [InlineData("S")]
    [InlineData("R")]
    [InlineData("D")]
    [InlineData("T")]
    public void TheStateLetter_IsRead(string state) =>
        Stat($"77 (worker) {state} 5 {Tail}").State.Should().Be(state);

    [Fact]
    public void AnExitedEntry_ReadsAsZombie_WithItsParentStillNamed()
    {
        var zombie = Stat($"900 (sleep) Z 4 {Tail}");

        zombie.State.Should().Be("Z");
        zombie.Parent.Should().Be(4, "the parent that has not reaped it is still the one the table names");
        ProcessTree.ExitedEntry(zombie.State, [900], zombie.Pid).Should().BeTrue("the ExitedEntry rule of DL9 reads the same state letter");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no brackets here")]
    [InlineData("(bash) S 1")]
    [InlineData("abc (bash) S 1 2")]
    [InlineData("12 (bash S 1 2")]
    [InlineData("12 (bash)")]
    [InlineData("12 (bash) S")]
    [InlineData("12 (bash) S x 2")]
    [InlineData("12 (bash) SS 1 2")]
    public void ALineThatIsNotAStatLine_IsUnreadable_NeverAGuess(string line) =>
        ProcStatReader.Parse(line).Should().BeOfType<ProcStatReading.Unreadable>().Which.Reason.Should().Contain("not a /proc stat line");

    [Fact]
    public void AFileThatIsNotThere_IsGone_AndAFileThatCannotBeRead_IsUnreadable()
    {
        using var dir = TempDir.For("testsToDelete-procstat-");

        ProcStatReader.ReadFile(dir.At("never", "stat")).Should().BeOfType<ProcStatReading.Gone>("ENOENT: the process ended before the read");
        ProcStatReader.ReadFile(dir.Path).Should().BeOfType<ProcStatReading.Unreadable>("a folder where the file should be is not evidence that a process is gone");
    }

    [Fact]
    public void AFileOnDisk_IsReadLikeALine()
    {
        using var dir = TempDir.For("testsToDelete-procstat-");
        var path = dir.At("stat");
        File.WriteAllText(path, $"5150 (my (odd) name) S 77 {Tail}\n");

        ProcStatReader.ReadFile(path).Should().Be(new ProcStatReading.Read(new ProcStat(5150, "my (odd) name", "S", 77)));
    }

    [Fact]
    public void ThisProcess_ReadsAsItself_OnLinux()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "/proc is Linux's");

        var stat = ProcStatReader.Read(Environment.ProcessId).Should().BeOfType<ProcStatReading.Read>().Subject.Stat;

        stat.Pid.Should().Be(Environment.ProcessId);
        stat.Comm.Should().NotBeEmpty();
        stat.State.Should().BeOneOf("R", "S", "D");
        ProcStatReader.Read(int.MaxValue).Should().BeOfType<ProcStatReading.Gone>("no process has that number");
    }
}
