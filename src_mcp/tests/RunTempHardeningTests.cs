using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the E2 code gate and the Opus judge (2026-10-20) found about <see cref="RunTemp"/> and <see cref="RunTempFolder"/>, each pinned by a test that was red first
/// (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, "Code gate and judge" record: G2/J4, G3, G8, G14, G15, G18, J3, J6).
/// </summary>
/// <remarks>Every test uses a private root and the seams; none writes a process-wide variable.</remarks>
public sealed class RunTempHardeningTests
{
    private static RunTempSeams Quiet(Dictionary<string, string?> values, List<string> warnings) =>
        new(
            (name, value) => { if (value is null) { values.Remove(name); } else { values[name] = value; } },
            name => values.GetValueOrDefault(name))
        {
            Warn = warnings.Add,
        };

    private static Func<string> Draws(params string[] suffixes)
    {
        var queue = new Queue<string>(suffixes);

        return () => queue.Count > 0 ? queue.Dequeue() : suffixes[^1];
    }

    [Fact]
    public void ANameThatAlreadyExists_IsNeverShared_ANewSuffixIsDrawn()
    {
        using var root = TempDir.For("testsToDelete-e2-root-");
        var taken = Directory.CreateDirectory(root.At(RunTempFolder.Prefix + "aaaaaa")).FullName;
        File.WriteAllText(Path.Combine(taken, "belongs-to-someone-else.txt"), "x");

        var run = RunTempFolder.Create(root.Path, Quiet([], []) with { Suffix = Draws("aaaaaa", "aaaaaa", "bbbbbb") });

        Path.GetFileName(run.Folder).Should().Be(RunTempFolder.Prefix + "bbbbbb", "the two colliding draws were skipped");
        File.Exists(Path.Combine(taken, "belongs-to-someone-else.txt")).Should().BeTrue("the folder that was already there is untouched");
        run.Restore();
        Directory.Exists(taken).Should().BeTrue("restoring this run never removes a folder it did not make");
    }

    [Fact]
    public void WhenEveryDrawCollides_CreateGivesUp_AndSharesNothing()
    {
        using var root = TempDir.For("testsToDelete-e2-root-");
        Directory.CreateDirectory(root.At(RunTempFolder.Prefix + "aaaaaa"));

        var act = () => RunTempFolder.Create(root.Path, Quiet([], []) with { Suffix = () => "aaaaaa" });

        act.Should().Throw<IOException>("a bounded number of draws, then an error the initializer turns into one warning and a run without a folder of its own");
        Directory.GetFiles(root.Path, "*.owner").Should().BeEmpty("no claim for a folder this run did not make");
    }

    [Fact]
    public void AFolderThatCannotBeDeleted_KeepsItsClaim()
    {
        using var root = TempDir.For("testsToDelete-e2-root-");
        var run = RunTempFolder.Create(root.Path, Quiet([], []) with { WriteClaim = folder => File.WriteAllText(folder + ".owner", "claim"), Delete = _ => throw new IOException("held by a scanner") });

        run.Restore();

        File.Exists(run.ClaimFile).Should().BeTrue("a folder that is still there keeps the claim that says whose it was; a claimless folder could only ever be collected by age");
    }

    [Fact]
    public void AClaimThatCannotBeDeleted_IsWarnedAbout_ByItsPath_AndRestoreNeverThrows()
    {
        using var root = TempDir.For("testsToDelete-e2-root-");
        var warnings = new List<string>();
        var run = RunTempFolder.Create(root.Path, Quiet([], warnings) with { WriteClaim = folder => File.WriteAllText(folder + ".owner", "claim"), DeleteClaim = _ => throw new UnauthorizedAccessException("denied") });

        var act = () => run.Restore();

        act.Should().NotThrow();
        warnings.Should().ContainSingle(w => w.Contains(run.ClaimFile, StringComparison.Ordinal), "the claim goes through its own delete seam, like the folder, and a failure names the file");
        Directory.Exists(run.Folder).Should().BeFalse("the folder was removed before the claim was tried");
    }

    public static TheoryData<string, string> Trims() => OperatingSystem.IsWindows()
        ? new TheoryData<string, string> { { @"C:\", @"C:\" }, { @"C:\Temp\", @"C:\Temp" }, { @"C:\Temp", @"C:\Temp" }, { @"C:\Users\x\AppData\Local\Temp\", @"C:\Users\x\AppData\Local\Temp" } }
        : new TheoryData<string, string> { { "/", "/" }, { "/tmp/", "/tmp" }, { "/tmp", "/tmp" }, { "/var/folders/ab/T/", "/var/folders/ab/T" } };

    [Theory]
    [MemberData(nameof(Trims))]
    public void TheMachinesTemp_LosesItsTrailingSeparator_ButIsNeverTrimmedToNothingOrToADriveLetter(string temp, string expected)
    {
        var trimmed = RunTemp.Trimmed(temp);

        trimmed.Should().Be(expected);
        Path.IsPathRooted(Path.Combine(trimmed, RunTempFolder.Prefix + "abcdef")).Should().BeTrue("a root trimmed to empty would make the run folder a relative path");
    }

    [Fact]
    public void TheFolderCreationWarning_NamesThePath_SaysThereIsNoIsolation_AndWhatToDo()
    {
        var said = RunTemp.CreationWarning(@"/the/temp", new UnauthorizedAccessException("denied"));

        said.Should().Contain(@"/the/temp", "the path it tried").And.Contain("UnauthorizedAccessException")
            .And.Contain("WITHOUT isolation").And.Contain("machine's temp")
            .And.Contain("TEMP").And.Contain("TMP").And.Contain("TMPDIR", "what to do: make TEMP writable or point TMP/TMPDIR elsewhere");
    }

    [Fact]
    public void TheClaimWarning_SaysTheConsequencePlainly()
    {
        var said = RunTemp.ClaimWarning(@"/the/temp/testsToDelete-coai-run-abcdef", new IOException("disk full"));

        said.Should().Contain(@"/the/temp/testsToDelete-coai-run-abcdef").And.Contain("IOException")
            .And.Contain("one hour").And.Contain("age rule").And.Contain("test-leftovers.md", "collected only by the family janitor's age rule, after one hour");
    }
}
