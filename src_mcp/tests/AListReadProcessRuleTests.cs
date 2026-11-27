using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The time rule of the processes of the real list read (<see cref="ListReadProcesses"/>): what is allowed before the main process ran as sh or codex, what fails after it, and that a failure names the process.</summary>
public sealed class AListReadProcessRuleTests
{
    private static SeenProcess P(int sample, string comm, int pid = 100, string parent = "?", string exe = "?") => new(sample, pid, comm, parent, exe);

    [Fact]
    public void TheStartOfTheUnit_BeforeTheMainProcessRanAsShOrCodex_MayHoldSdUsernsAndTheBracketedSh()
    {
        SeenProcess[] seen = [P(0, "(sd-userns)", 90), P(0, "(sh)", 100), P(1, "(sd-userns)", 90), P(2, "sh", 100), P(3, "codex", 100), P(4, "lsb_release", 120), P(4, "(sh)", 119)];

        // sample 4 shows a (sh) AFTER the cutoff: that one fails; everything before it does not.
        var violations = ListReadProcesses.Violations(seen.Where(process => process.Sample < 4).ToList(), "100");

        violations.Should().BeEmpty();
        ListReadProcesses.Cutoff(seen).Should().Be(2);
    }

    [Theory]
    [InlineData("sd-userns")]
    [InlineData("(sd-userns)")]
    [InlineData("(sh)")]
    public void AtOrAfterTheFirstSampleThatShowsShOrCodex_TheseNamesFail_AndTheFailureNamesThem(string comm)
    {
        SeenProcess[] seen = [P(0, "(sh)", 100), P(1, "sh", 100), P(1, comm, 77), P(2, comm, 78)];

        var violations = ListReadProcesses.Violations(seen, "100");

        violations.Should().Contain(text => text.Contains(comm) && text.Contains("pid 77") && text.Contains("sample 1")).And.Contain(text => text.Contains("pid 78"));
    }

    [Fact]
    public void CodexAsTheFirstToShow_AlsoSetsTheCutoff()
    {
        SeenProcess[] seen = [P(0, "(sd-userns)", 90), P(1, "codex", 100), P(2, "(sd-userns)", 90)];

        var violations = ListReadProcesses.Violations(seen, "100");

        violations.Should().ContainSingle().Which.Should().Contain("(sd-userns)").And.Contain("sample 2");
    }

    [Fact]
    public void WhenNoSampleEverShowsShOrCodex_TheTimeRuleHasNoCutoff()
    {
        ListReadProcesses.Violations([P(0, "(sd-userns)"), P(1, "(sh)")], "100").Should().BeEmpty();
        ListReadProcesses.Cutoff([P(0, "(sd-userns)")]).Should().BeNull();
    }

    [Theory]
    [InlineData("lsb_release")]
    [InlineData("(lsb_release)")]
    [InlineData("cut")]
    [InlineData("sed")]
    [InlineData("tr")]
    [InlineData("(sh)")]
    public void TheNamesOfTheOsProbeAreAllowedAtAnyTime_ABracketIsTheSameName(string comm) =>
        ListReadProcesses.Violations([P(5, comm)], "100").Should().BeEmpty("a name of the probe, with no sample that shows sh or codex before it, holds");

    [Theory]
    [InlineData("curl")]
    [InlineData("bash")]
    [InlineData("(sd-pam)")]
    [InlineData("git")]
    public void AnyOtherName_Fails_AtAnyTime_AndIsNamed(string comm)
    {
        var violations = ListReadProcesses.Violations([P(0, comm, 55)], "100");

        violations.Should().ContainSingle().Which.Should().Contain(comm).And.Contain("pid 55").And.Contain("not on the list");
    }

    [Fact]
    public void WhenTheExecutableOfSdUsernsCanBeRead_ItIsASystemdProgram_NotTheReleaseBinaryNotDashNotAFileOfTheJob()
    {
        ListReadProcesses.Violations([P(0, "(sd-userns)", 90, "100", "/usr/lib/systemd/systemd-executor")], "100").Should().BeEmpty();
        foreach (var exe in new[] { "/home/jinx/.codex/packages/standalone/releases/0.171.0-x86_64-unknown-linux-musl/bin/codex", "/usr/bin/dash", "/home/jinx/coai-t3/coder-jobs/j1/coder/codex/home/helper" })
        {
            ListReadProcesses.Violations([P(0, "(sd-userns)", 90, "100", exe)], "100").Should().ContainSingle().Which.Should().Contain("not a program of systemd").And.Contain(exe);
        }
    }

    [Fact]
    public void WhenTheParentOfSdUsernsCanBeRead_ItIsTheUnitsMainPid_AndWhenItCannotBeRead_TheTimeRuleAloneDecides()
    {
        ListReadProcesses.Violations([P(0, "(sd-userns)", 90, "100", "/usr/lib/systemd/systemd-executor")], "100").Should().BeEmpty();
        ListReadProcesses.Violations([P(0, "(sd-userns)", 90, "999", "/usr/lib/systemd/systemd-executor")], "100").Should().ContainSingle().Which.Should().Contain("parent 999").And.Contain("main pid 100");
        ListReadProcesses.Violations([P(0, "(sd-userns)", 90, "?", "?")], "100").Should().BeEmpty("neither could be read: the time rule alone decides, and the sample is before the cutoff");
        ListReadProcesses.Violations([P(0, "(sd-userns)", 90, "999", "/usr/lib/systemd/systemd-executor")], string.Empty).Should().BeEmpty("the main pid is not known: the parent cannot be held");
    }

    [Fact]
    public void ThereIsNoParentRule_ShAndCodexAreOnePidThatChangesByExec()
    {
        // (sh), sh and codex all have the same pid in the three samples; none is held to a parent.
        ListReadProcesses.Violations([P(0, "(sh)", 100, "320"), P(1, "sh", 100, "320"), P(2, "codex", 100, "320"), P(3, "lsb_release", 130, "3400")], "100").Should().BeEmpty();
    }
}
