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
/// <para>It runs ONCE per assembly, deletes only what this project named, and only what is a day
/// old — so a directory another test run is using right now is never touched.</para>
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

    public TempDirsAreSwept() =>
        PanelService.PruneOldScratchDirs(Path.GetTempPath(), DateTime.UtcNow.AddDays(-1), Prefixes);

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
}
