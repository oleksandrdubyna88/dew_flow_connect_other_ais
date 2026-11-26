using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 follow-up, the security verify's M4: the systemd user manager replaces <c>${NAME}</c> and a whole-word <c>$NAME</c> in EVERY argument of a unit's command line from its own environment (measured on systemd 259:
/// an unset name becomes an empty argument, even through <c>env -i</c>). A toolchain argv, a task's file name and a preflight path are on that line, so each unit of this family is started with <c>--expand-environment=no</c>.
/// </summary>
/// <remarks>Real <c>systemd-run --user</c> units on temp folders only; skipped, with the reason, where there is no user manager. The literal items are fixed text, no variable of this process is read or printed.</remarks>
public sealed class AUnitCommandLineIsNotExpandedTests
{
    private const string Flag = "--expand-environment=no";

    private static readonly string[] Items = ["${HOME}", "$HOME", "$$", "${UNSET_X}", "pre${HOME}post"];

    [Fact]
    public async Task OnARealUnit_TheArgvItemsArriveLiterally_NothingIsReplacedByTheUserManager()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var bed = await BoundaryBed.CreateAsync();

        var run = await bed.CommandAsync(["/bin/sh", "-c", "printf '<%s>\\n' \"$@\"", "sh", .. Items], new Dictionary<string, string?>());

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal(Items.Select(item => $"<{item}>"), "every argument is what the toolchain file declared, byte for byte");
    }

    [Fact]
    public async Task OnARealUnit_AnItemThatNamesAVariableOfTheUserManager_IsNotItsValue()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var bed = await BoundaryBed.CreateAsync();

        // XDG_RUNTIME_DIR is set in every user manager and is no secret: it stands for a name the manager has (the control of the test above, which names a name it may not have).
        var run = await bed.CommandAsync(["/bin/sh", "-c", "printf '<%s>\\n' \"$@\"", "sh", "${XDG_RUNTIME_DIR}", "$XDG_RUNTIME_DIR"], new Dictionary<string, string?>());

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal("<${XDG_RUNTIME_DIR}>", "<$XDG_RUNTIME_DIR>");
    }

    [Fact]
    public void TheCheckAndTheCoderLine_BothCarryTheFlag_BeforeTheCommand()
    {
        var unitLine = BoundaryUnit.Wrap("coai-check-x-s1", "/work", [], ["MemoryMax=64M"], ["/usr/bin/env", "-i", "A=${B}"], degrade: null);
        var runnerLine = DetachedLauncher.SystemdRunArguments(new DetachSpec("/bin/true", ["$X"], "/work", "coai-role-x", 64));

        unitLine.Should().Contain(Flag);
        runnerLine.Should().Contain(Flag);
        unitLine.ToList().IndexOf(Flag).Should().BeLessThan(unitLine.ToList().IndexOf("--"), "an option after `--` would be the program's own argument");
        runnerLine.ToList().IndexOf(Flag).Should().BeLessThan(runnerLine.ToList().IndexOf("--"));
    }

    [Fact]
    public void AClientThatDoesNotKnowTheFlag_IsNamedByTheRefusal()
    {
        DetachedLauncher.ExpandEnvironmentHint("systemd-run: unrecognized option '--expand-environment=no'").Should().Contain("254");
        DetachedLauncher.ExpandEnvironmentHint("Failed to connect to user scope bus").Should().BeEmpty("another fault says nothing about the version");
    }

    [Fact]
    public void ThePreflight_OfAnOldSystemd_RefusesWithTheVersionInTheRow()
    {
        var refused = new UnitStart(new CoaiMcp.Runners.Processes.ProcessResult(1, string.Empty, "systemd-run: unrecognized option '--expand-environment=no'", TimedOut: false), string.Empty);

        var rows = BoundaryPreflight.Rows(refused, gone: true, items: ["x"]);

        rows.Should().ContainSingle().Which.Held.Should().BeFalse("the unit never started, so the job is refused");
        rows[0].Detail.Should().Contain("systemd 254");
    }
}
