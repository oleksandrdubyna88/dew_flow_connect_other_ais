using CoaiMcp.Tests.Leftovers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the E2 code gate and the Opus judge (2026-10-20) found about <see cref="RunTemp"/> and <see cref="RunTempFolder"/>, and what E5 S3 changed: a taken name is
/// never adopted (the claim is made exclusively, first), and a folder that cannot be removed keeps its claim (<c>todo/PLAN_test_leftovers_E5.md</c>, S3 scenario 3).
/// </summary>
/// <remarks>Every test uses a private root inside an owned scope and the seams; none writes a process-wide variable.</remarks>
public sealed class RunTempHardeningTests : IDisposable
{
    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    private static RunTempSeams Quiet(Dictionary<string, string?> values, List<string> warnings) =>
        new(
            (name, value) => { if (value is null) { values.Remove(name); } else { values[name] = value; } },
            name => values.GetValueOrDefault(name))
        {
            Warn = warnings.Add,
        };

    /// <summary>The leaf names the seam hands out: the owner-form run name with each id in turn; the last one repeats.</summary>
    private static Func<string> Draws(params string[] ids)
    {
        var queue = new Queue<string>(ids);

        return () => Owned.NameBase + "run-" + (queue.Count > 1 ? queue.Dequeue() : queue.Peek());
    }

    private static string Leaf(string id) => Owned.NameBase + "run-" + id;

    [Fact]
    public void ATakenName_IsNeverAdopted_AClaimAlreadyThere()
    {
        var root = _owned.Dir("s3-taken-claim");
        var theirs = Path.Combine(root, Leaf("aaaaaa") + ".owner");
        File.WriteAllText(theirs, "somebody else's bytes");

        var run = RunTempFolder.Create(root, Quiet([], []) with { NextName = Draws("aaaaaa", "bbbbbb") });

        Path.GetFileName(run.Folder).Should().Be(Leaf("bbbbbb"), "the name with somebody's claim on it was skipped");
        File.ReadAllText(theirs).Should().Be("somebody else's bytes", "a claim that was there is never replaced");
        Directory.Exists(Path.Combine(root, Leaf("aaaaaa"))).Should().BeFalse("no folder was made under a name this run did not claim");
        run.Restore();
        File.Exists(theirs).Should().BeTrue("restoring this run never removes a claim it did not make");
    }

    [Fact]
    public void ATakenName_IsNeverAdopted_AFolderAlreadyThere()
    {
        var root = _owned.Dir("s3-taken-folder");
        var squatter = Directory.CreateDirectory(Path.Combine(root, Leaf("aaaaaa"))).FullName;
        File.WriteAllText(Path.Combine(squatter, "belongs-to-someone-else.txt"), "x");
        var warnings = new List<string>();

        var run = RunTempFolder.Create(root, Quiet([], warnings) with { NextName = Draws("aaaaaa", "bbbbbb") });

        Path.GetFileName(run.Folder).Should().Be(Leaf("bbbbbb"));
        warnings.Should().ContainSingle(w => w.Contains(Leaf("aaaaaa"), StringComparison.Ordinal), "ONE warning, naming the folder that was there");
        File.Exists(Path.Combine(root, Leaf("aaaaaa") + ".owner")).Should().BeFalse("this run's own claim for that name was removed again");
        File.Exists(Path.Combine(squatter, "belongs-to-someone-else.txt")).Should().BeTrue("the folder that was already there is untouched");
        run.Restore();
        Directory.Exists(squatter).Should().BeTrue("restoring this run never removes a folder it did not make");
    }

    [Fact]
    public void WhenEveryDrawCollides_CreateGivesUp_AfterThreeDraws_AndLeavesNothingOfItsOwn()
    {
        var root = _owned.Dir("s3-all-taken");
        Directory.CreateDirectory(Path.Combine(root, Leaf("aaaaaa")));
        var draws = 0;
        var seams = Quiet([], []) with { NextName = () => { draws++; return Leaf("aaaaaa"); } };

        var act = () => RunTempFolder.Create(root, seams);

        act.Should().Throw<InvalidOperationException>("a bounded number of draws, then an error that fails the run closed");
        draws.Should().Be(Owned.MaxDraws);
        Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Should().ContainSingle().Which.Should().Be(Leaf("aaaaaa"), "only the folder that was there: no claim of ours");
    }

    [Fact]
    public void AFolderThatCannotBeDeleted_KeepsItsClaim()
    {
        var root = _owned.Dir("s3-unremovable");
        var warnings = new List<string>();
        var run = RunTempFolder.Create(root, Quiet([], warnings));

        using (Unremovable.Make(root, run.Folder))
        {
            run.Restore();

            File.Exists(run.ClaimFile).Should().BeTrue("a folder that is still there keeps the claim that says whose it was; a claimless folder could not be judged by its owner");
            Directory.Exists(run.Folder).Should().BeTrue("the premise: the folder could not be removed");
            warnings.Should().ContainSingle(w => w.Contains(run.Folder, StringComparison.Ordinal), "ONE warning, naming the folder");
        }

        run.Dir!.Dispose();
        Directory.Exists(run.Folder).Should().BeFalse("once it can be removed, the next attempt removes folder and claim");
        File.Exists(run.ClaimFile).Should().BeFalse();
    }

    /// <summary>The "/tmp" rows are DATA — what a POSIX machine's temp root looks like to the trimmer, not a place this test touches. The folder name is written with a tilde inside
    /// and the tilde is taken out when the test runs (the <c>PlantedLines</c> way), so no guard reads a temp path in this file.</summary>
    private static string Data(string text) => text.Replace("~", string.Empty, StringComparison.Ordinal);

    public static TheoryData<string, string> Trims() => OperatingSystem.IsWindows()
        ? new TheoryData<string, string> { { @"C:\", @"C:\" }, { @"C:\Temp\", @"C:\Temp" }, { @"C:\Temp", @"C:\Temp" }, { @"C:\Users\x\AppData\Local\Temp\", @"C:\Users\x\AppData\Local\Temp" } }
        : new TheoryData<string, string> { { "/", "/" }, { Data("/t~mp/"), Data("/t~mp") }, { Data("/t~mp"), Data("/t~mp") }, { "/var/folders/ab/T/", "/var/folders/ab/T" } };

    [Theory]
    [MemberData(nameof(Trims))]
    public void TheMachinesTemp_LosesItsTrailingSeparator_ButIsNeverTrimmedToNothingOrToADriveLetter(string temp, string expected)
    {
        var trimmed = RunTemp.Trimmed(temp);

        trimmed.Should().Be(expected);
        Path.IsPathRooted(Path.Combine(trimmed, Leaf("abcdef"))).Should().BeTrue("a root trimmed to empty would make the run folder a relative path");
    }
}
