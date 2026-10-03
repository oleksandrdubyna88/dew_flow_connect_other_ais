using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The consultant check's retention (E4.4): leftover scratch older than a day goes, a live check's never does, and the
/// test suite's own temp sweep keeps its hands off the prefix.
/// </summary>
public sealed class ConsultCheckSweepTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("coai-checksweep-temp-").FullName;
    private readonly string _data = Directory.CreateTempSubdirectory("coai-checksweep-data-").FullName;

    public void Dispose()
    {
        foreach (var dir in (string[])[_temp, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not a failing test.
            }
        }
    }

    private string Scratch(string name, TimeSpan age)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_temp, name)).FullName;
        File.WriteAllText(Path.Combine(dir, "CHECK.md"), "The check word is wren-1.\n");
        Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow - age);

        return dir;
    }

    /// <summary>A test run that swept <c>coai-check-*</c> would delete a live check's repository from under its turn.</summary>
    [Fact]
    public void TheTestSuitesTempSweep_NeverTakesACheckScratch()
    {
        SweepRule.Shared().NeverSwept.Should().Contain(ConsultCheckScratch.Prefix);
    }

    [Fact]
    public void ScratchOlderThanADay_IsRemoved_AndYoungerOrForeignIsLeft()
    {
        var stale = Scratch(ConsultCheckScratch.Prefix + "stale", TimeSpan.FromDays(2));
        var live = Scratch(ConsultCheckScratch.Prefix + "live", TimeSpan.FromMinutes(3));
        var foreign = Scratch("coai-answers-stale", TimeSpan.FromDays(2));
        File.SetAttributes(Path.Combine(stale, "CHECK.md"), FileAttributes.ReadOnly);

        ConsultCheckScratch.Sweep(_temp, DateTime.UtcNow, _ => { }).Should().Be(1);

        Directory.Exists(stale).Should().BeFalse("a check never runs a day, and git's read-only files are no obstacle");
        Directory.Exists(live).Should().BeTrue("a scratch three minutes old may be a check running now");
        Directory.Exists(foreign).Should().BeTrue("only the check's own prefix is its to take");
    }

    /// <summary>A launcher whose every git command fails — the scratch cannot become a repository.</summary>
    private sealed class GitRefuses : CoaiMcp.Runners.Processes.IProcessLauncher
    {
        public Task<CoaiMcp.Runners.Processes.ProcessResult> RunAsync(
            CoaiMcp.Runners.Processes.ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CoaiMcp.Runners.Processes.ProcessResult(128, string.Empty, "fatal: not a git repository", TimedOut: false));
    }

    /// <summary>
    /// A scratch that git refused is removed at once, not left for a sweep a day later — the refusal carries no
    /// path, so nothing after it could (coai code round of epic 4, 2026-10-03).
    /// </summary>
    [Fact]
    public async Task AScratchGitRefused_IsRemovedAtOnce()
    {
        var made = await ConsultCheckScratch.CreateAsync(new GitRefuses(), _temp, _ => { }, CancellationToken.None);

        made.Should().BeOfType<ScratchOutcome.Refused>();
        Directory.EnumerateDirectories(_temp, ConsultCheckScratch.Prefix + "*").Should().BeEmpty(
            "a refused scratch holds the canary and the marker, and nothing else will ever remove it before tomorrow");
    }

    [Fact]
    public void AMissingTempDirectory_IsNothingToSweep_NotAFailure()
    {
        ConsultCheckScratch.Sweep(Path.Combine(_temp, "gone"), DateTime.UtcNow, _ => { }).Should().Be(0);
    }

    /// <summary>
    /// The sweeper's pacing clock starts when it is BUILT: <c>ConsultationService</c> is built in <c>PanelService</c>'s
    /// constructor, whose startup sweep must not walk the temp directory (the code round of epic 4).
    /// </summary>
    [Fact]
    public void TheSweepersFirstBeat_RightAfterItWasBuilt_WalksNoTempDirectory()
    {
        var now = DateTime.UtcNow;
        var stale = Scratch(ConsultCheckScratch.Prefix + "stale", TimeSpan.FromDays(2));

        new ConsultCheckSweeper(_data, _temp, now).Sweep(now, _ => { }).Scratch.Should().Be(0);
        Directory.Exists(stale).Should().BeTrue("the first walk waits a pace, and a check sweeps at its own start anyway");
    }

    [Fact]
    public void TheSweeper_SettlesStatesOnEveryBeat_ButWalksTheTempDirectoryAtItsOwnPace()
    {
        // Built a pace ago, so its first beat is due.
        var sweeper = new ConsultCheckSweeper(_data, _temp, DateTime.UtcNow - ConsultCheckSweeper.ScratchEvery);
        var store = new ConsultCheckStore(_data);
        var first = Scratch(ConsultCheckScratch.Prefix + "first", TimeSpan.FromDays(2));

        sweeper.Sweep(DateTime.UtcNow, _ => { }).Scratch.Should().Be(1);
        Directory.Exists(first).Should().BeFalse();

        var second = Scratch(ConsultCheckScratch.Prefix + "second", TimeSpan.FromDays(2));
        using (ConsultCheckLock.TryTake(store, "claude"))
        {
            store.Write(new ConsultCheckRecord { CallerKind = "claude", State = ConsultCheckStates.Checking });
        }

        var beat = sweeper.Sweep(DateTime.UtcNow, _ => { });

        beat.Abandoned.Should().Be(1, "a checking state with a free lock is settled on every beat");
        beat.Scratch.Should().Be(0, "the temp walk is paced to once every ten minutes");
        Directory.Exists(second).Should().BeTrue();
        sweeper.Sweep(DateTime.UtcNow + ConsultCheckSweeper.ScratchEvery, _ => { }).Scratch.Should().Be(1);
    }
}
