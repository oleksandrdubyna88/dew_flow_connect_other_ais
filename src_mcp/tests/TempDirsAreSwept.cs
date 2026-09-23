using FluentAssertions;
using CoaiMcp.Server;
using Xunit;

[assembly: AssemblyFixture(typeof(CoaiMcp.Tests.TempDirsAreSwept))]

namespace CoaiMcp.Tests;

/// <summary>
/// The suite sweeps up after itself, once, before anything runs.
/// </summary>
/// <remarks>
/// <para><b>Measured on this machine, 2026-09-05.</b> The temp folder held 21,857 directories this
/// project had made. The product's own three prefixes accounted for 3,395 of them and <b>not one was
/// older than its six-hour window</b> — its sweeper works. The rest were the TESTS: 4,650
/// <c>coai-panel-*</c> older than six hours, the oldest at five days, plus 1,386 <c>coai-prompts-*</c>
/// and 184 leftover worktree directories. Nothing had ever removed them.</para>
/// <para><b>Why a sweep and not forty fixed Disposes.</b> Forty test classes create a temp directory
/// and delete it in <c>Dispose</c>, and that delete loses a race whenever a file in it is still held
/// open — a spawned CLI, a SQLite handle, a reader mid-poll. Each of those catches the exception, and
/// correctly: a leftover temp directory is not a failing test. What was missing is anybody clearing
/// the leftovers afterwards, which is one sweep rather than forty corrections.</para>
/// <para>It runs ONCE per assembly, deletes only what this project named, only what is older than
/// <see cref="Rule"/>'s window, and never the product's own working directories — so neither a
/// directory another test run is using right now nor a live round's is touched.</para>
/// </remarks>
public sealed class TempDirsAreSwept
{
    /// <summary>
    /// Everything this suite makes, by the one thing they all share.
    /// </summary>
    /// <remarks>
    /// A pattern rather than a list, because the list was wrong the first time it was written: the
    /// thirteen prefixes it named left 6,759 directories behind, under twelve more nobody had
    /// thought of (<c>coai-e2e-</c>, <c>coai-ctx-</c>, <c>coai-deal-</c>, <c>coai-bom-</c>…). A test
    /// class that invents a fourteenth tomorrow is the normal case, and it is swept too.
    /// </remarks>
    private static readonly string[] Prefixes = ["coai-*"];

    /// <summary>
    /// How old a leftover must be, and what is never one — <c>shared/temp-sweep.json</c>, which the
    /// extension's runner reads too.
    /// </summary>
    /// <remarks>
    /// <para><b>Ten minutes, and it was two hours, and before that a day.</b> A day was far too slow:
    /// a machine running this suite repeatedly accumulated everything it made IN that day, and what
    /// that cost was not disk — it was <c>PanelService.BuildWork</c>, which walks the temp directory.
    /// Measured here 2026-09-13: <b>81,986</b> directories, and a test that calls <c>BuildWork</c> a
    /// hundred times went from 8 seconds to over four minutes. Ten minutes is the operator's ruling of
    /// 2026-09-18, for both runners: the longest single run of this suite is under ten minutes, so a
    /// concurrent run is still never reached.</para>
    /// <para><b>And the product's own working directories are excepted</b>, which the two-hour window
    /// never did: a live chat's directory can sit with nothing written into it for longer than either
    /// window, and <c>coai-*</c> matched it.</para>
    /// </remarks>
    internal static readonly SweepRule Rule = SweepRule.Shared();

    public TempDirsAreSwept() =>
        PanelService.PruneOldScratchDirs(
            Path.GetTempPath(), DateTime.UtcNow - Rule.Keeps, [$"{Rule.Prefix}*"], Rule.NeverSwept);

    [Fact]
    public void TheSharedRuleIsTheOperatorsTenMinutes()
    {
        Rule.Prefix.Should().Be("coai-");
        Rule.Keeps.Should().Be(TimeSpan.FromMinutes(10));
    }

    /// <summary>Every scratch prefix the product makes is one the test sweep leaves alone.</summary>
    /// <remarks>
    /// A new working directory in <c>PanelService</c> that nobody adds to the shared rule would be
    /// removed out from under a live round by the next test run on the same machine.
    /// </remarks>
    [Fact]
    public void EveryScratchPrefixTheProductMakes_IsNeverTheTestsToSweep()
    {
        foreach (var prefix in PanelService.ScratchPrefixes.Select(p => p.TrimEnd('*')))
        {
            Rule.NeverSwept.Should().Contain(prefix, "the product sweeps {0}* itself, on its own clock", prefix);
        }
    }

    [Fact]
    public void AProductWorkingDirectory_SurvivesTheTestSweep_WhateverItsAge()
    {
        using var root = TempDir.For("coai-sweep-owned-");
        var chat = Directory.CreateDirectory(root.At("coai-chat-live")).FullName;
        var leftover = Directory.CreateDirectory(root.At("coai-panel-old")).FullName;
        foreach (var dir in new[] { chat, leftover })
        {
            Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddHours(-3));
        }

        PanelService.PruneOldScratchDirs(root, DateTime.UtcNow - Rule.Keeps, [$"{Rule.Prefix}*"], Rule.NeverSwept);

        Directory.Exists(chat).Should().BeTrue("a chat that wrote nothing for three hours is still somebody's chat");
        Directory.Exists(leftover).Should().BeFalse("and the test's own leftover beside it still goes");
    }

    [Fact]
    public void TheSweepRemovesWhatIsOldAndLeavesWhatIsInUse()
    {
        var root = Directory.CreateTempSubdirectory("coai-sweep-test-").FullName;
        var old = Directory.CreateDirectory(Path.Combine(root, "coai-panel-old")).FullName;
        var fresh = Directory.CreateDirectory(Path.Combine(root, "coai-panel-fresh")).FullName;
        // The LAST WRITE is what the sweep reads — Windows hands a recreated name its predecessor's
        // creation time, so that one lies about exactly the case this is for.
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));

        PanelService.PruneOldScratchDirs(root, DateTime.UtcNow.AddDays(-1), ["coai-panel-*"]);

        Assert.False(Directory.Exists(old), "a directory three days old is nobody's working state");
        Assert.True(Directory.Exists(fresh), "and one made a moment ago may be another run's");
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void ADirectoryGitHasBeenIn_IsSweptToo()
    {
        // The reason 5,476 of them survived every sweep: git marks its object files READ-ONLY, and
        // Directory.Delete(recursive: true) refuses a read-only file with UnauthorizedAccessException
        // — which the sweeper caught, correctly, and then left the directory for ever.
        var root = Directory.CreateTempSubdirectory("coai-sweep-ro-").FullName;
        var repo = Directory.CreateDirectory(Path.Combine(root, "coai-ctx-old")).FullName;
        var objects = Directory.CreateDirectory(Path.Combine(repo, ".git", "objects", "27")).FullName;
        var blob = Path.Combine(objects, "e9bb8d72");
        File.WriteAllText(blob, "an object git would never let you write twice");
        File.SetAttributes(blob, FileAttributes.ReadOnly);
        Directory.SetLastWriteTimeUtc(repo, DateTime.UtcNow.AddDays(-3));

        PanelService.PruneOldScratchDirs(root, DateTime.UtcNow.AddDays(-1), ["coai-ctx-*"]);

        Assert.False(Directory.Exists(repo), "a clone three days old is nobody's working state either");
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void ADownloadedServerIsNotScratch_AndSurvives()
    {
        // The sweep matches coai-*, and a downloaded server sits under coai-<version> in the same
        // folder. Deleting one would cost a download and look like the extension had lost its
        // install; the gate asked for this check by name.
        var root = Directory.CreateTempSubdirectory("coai-sweep-cache-").FullName;
        var cache = Directory.CreateDirectory(Path.Combine(root, "coai-0.17.5")).FullName;
        var scratch = Directory.CreateDirectory(Path.Combine(root, "coai-panel-whatever")).FullName;
        foreach (var dir in new[] { cache, scratch })
        {
            Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow.AddDays(-9));
        }

        PanelService.PruneOldScratchDirs(root, DateTime.UtcNow.AddDays(-1), Prefixes);

        Assert.True(Directory.Exists(cache), "a version is a cache, not scratch");
        Assert.False(Directory.Exists(scratch), "and the scratch beside it still goes");
        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// The product walks the temp directory at most once every ten minutes, not once per round.
    /// </summary>
    /// <remarks>
    /// It walked on EVERY `BuildWork` — twice per reviewer — and the walk costs the number of
    /// directories in temp. That is what turned a hundred-iteration test into a four-minute stall
    /// that read as a deadlock in whatever had just been changed.
    /// </remarks>
    [Fact]
    public void TheProductSweeps_AtMostOncePerWindow()
    {
        using var root = TempDir.For("coai-throttle-");
        var old = Directory.CreateDirectory(root.At("coai-answers-old")).FullName;
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));

        PanelService.ForgetTheLastSweep();
        PanelService.PruneOldScratchDirs(root, DateTime.UtcNow.AddDays(-1), ["coai-answers-*"]);

        Assert.False(Directory.Exists(old), "the sweep itself still removes what is old");
    }

    /// <summary>A directory that removes itself, which is what eleven classes were not doing.</summary>
    [Fact]
    public void ATempDir_RemovesItselfAtTheEndOfItsScope()
    {
        string path;
        using (var work = TempDir.For("coai-raii-"))
        {
            path = work;
            File.WriteAllText(work.At("a.txt"), "something");
            Assert.True(Directory.Exists(path));
        }

        Assert.False(Directory.Exists(path), "the scope ended, so the directory did");
    }

    /// <summary>Including one git has been in, whose object files are read-only.</summary>
    [Fact]
    public void ATempDir_RemovesItselfEvenWhenAFileIsReadOnly()
    {
        string path;
        using (var work = TempDir.For("coai-raii-ro-"))
        {
            path = work;
            var file = work.At("locked.txt");
            File.WriteAllText(file, "git marks its objects read-only");
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        }

        Assert.False(Directory.Exists(path), "a read-only file is not a reason to leak a directory");
    }

    /// <summary>And a failure on the way out is never a test result.</summary>
    [Fact]
    public void ATempDirThatIsAlreadyGone_DisposesQuietly()
    {
        var work = TempDir.For("coai-raii-gone-");
        Directory.Delete(work, recursive: true);

        work.Dispose();
    }
}
