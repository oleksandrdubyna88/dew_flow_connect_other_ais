using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The scratch sweep has a second caller (plan §7, gate finding 2026-10-12): a crashed <c>consult</c> followed by no round
/// would have left its snapshot in temp for ever, because <c>PruneOldAnswerDirs</c> ran only on the round's build path. The
/// consultation beat — which runs while the server serves, round or no round — now calls the same throttled sweep.
/// </summary>
/// <remarks>
/// <para>In the serial <c>fakecli-env</c> collection: it calls <c>PanelService.ForgetTheLastSweep</c>, which resets a static of the PROCESS, and the next
/// <c>BuildWork</c> anywhere then walks the real temp folder — inside whatever round deadline that call runs under (C6 of
/// <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AScratchSweepRunsOnTheConsultationBeatTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-sweeper-").FullName;
    private readonly ProcessLauncher _launcher = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The real host, as <c>ConsultationSweeperTests</c> builds it — no vendor, no round.</summary>
    private PanelServiceHost Host()
    {
        var env = new Dictionary<string, string?> { ["COAI_DATA_DIR"] = _dir, ["COAI_PROVIDERS"] = "codex" };

        return new PanelServiceHost(
            name => env.GetValueOrDefault(name),
            VaultKeys.None("no vault in this test"),
            default,
            _launcher,
            Logger.None,
            Noticing.None);
    }

    [Fact]
    public async Task AnOldAnswersDirectory_HoldingASnapshot_IsGoneAfterOneBeat_WithNoRoundBuilt()
    {
        // In the REAL temp root, under the product's own prefix: that is what the product's sweep walks.
        var scratch = Directory.CreateTempSubdirectory("coai-answers-snapshot-").FullName;
        await using var repo = await TempGitRepo.InitAsync(_launcher, "coai-snap-repo-");
        await repo.WriteAsync("a.txt", "a consult's tree");
        await repo.CommitAsync("base");
        var made = await SnapshotTestKit.Snapshots(_launcher).MakeAsync(new SnapshotSource.Live(repo.Path), scratch, TestContext.Current.CancellationToken);
        var snapshot = made.Should().BeOfType<SnapshotOutcome.Made>().Which.Snapshot;
        Directory.Exists(snapshot.Root).Should().BeTrue();
        // Never disposed: the consult crashed. Older than the sweep's six-hour window.
        Directory.SetLastWriteTimeUtc(scratch, DateTime.UtcNow.AddHours(-7));
        PanelService.ForgetTheLastSweep();

        await RunBeatsWhileAsync(() => Directory.Exists(scratch));

        Directory.Exists(scratch).Should().BeFalse("the beat sweeps the product's old scratch directories, snapshot included, with no round built");
    }

    /// <summary>The consultation beat, on a 20 ms tick, until <paramref name="pending"/> says the sweep is done or twenty seconds pass.</summary>
    private async Task RunBeatsWhileAsync(Func<bool> pending)
    {
        var host = Host();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var loop = ConsultationSweeper.RunAsync(() => host.Current, TimeSpan.FromMilliseconds(20), Logger.None, stop.Token);
        for (var deadline = DateTime.UtcNow.AddSeconds(20); pending() && DateTime.UtcNow < deadline; await Task.Delay(50, TestContext.Current.CancellationToken)) { }
        await stop.CancelAsync();
        await loop;
    }

    /// <summary>
    /// A question row's scratch as a killed server leaves it (D8 of <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>): a copy
    /// of a repository in a numbered sub-folder, the row's lock file with nobody holding it, and a folder time of <paramref name="age"/> ago.
    /// Made in the REAL temp root under the row's own prefix, which is what the product's sweep walks.
    /// </summary>
    private static string StrandedQuestionScratch(TimeSpan age)
    {
        var scratch = Directory.CreateTempSubdirectory(QuestionRowLaunch.ScratchPrefix).FullName;
        var copy = Directory.CreateDirectory(Path.Combine(scratch, "0", "repo-a")).FullName;
        File.WriteAllText(Path.Combine(copy, "a.txt"), "a copy a row was reading");
        File.WriteAllText(Path.Combine(scratch, FirstEntry), "an entry that sorts before the lock file");
        File.WriteAllText(Path.Combine(scratch, QuestionRowLaunch.LockName), string.Empty);
        Directory.SetLastWriteTimeUtc(scratch, DateTime.UtcNow - age);

        return scratch;
    }

    /// <summary>
    /// A top-level entry that NTFS lists BEFORE the lock file (<c>!</c> sorts ahead of <c>.</c>): a sweep that ignored the lock would delete
    /// it before the held lock file refused the delete — so a lock-ignorant sweep is seen destroying it on Windows too, not saved by the
    /// listing order. On Linux the lock is advisory and a lock-ignorant sweep removes everything anyway.
    /// </summary>
    private const string FirstEntry = "!notes.txt";

    private static void DeleteQuietly(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task AnOldQuestionRowScratch_HoldingCopies_IsGoneAfterOneBeat()
    {
        var scratch = StrandedQuestionScratch(TimeSpan.FromHours(7));
        PanelService.ForgetTheLastSweep();
        try
        {
            await RunBeatsWhileAsync(() => Directory.Exists(scratch));

            Directory.Exists(scratch).Should().BeFalse("the beat sweeps a question row's old scratch, copies included — a killed server stranded it, and nothing else ever removes it");
        }
        finally
        {
            DeleteQuietly(scratch);
        }
    }

    /// <summary>
    /// A row's budget has no upper bound, so age alone would delete the copies a live row is reading. The row's lock is what says it
    /// lives — and the sweep is run straight, not through a beat, so "it survived" is proved by a finished sweep, not by waiting.
    /// </summary>
    [Fact]
    public void AQuestionRowScratch_OlderThanTheWindow_WhoseLockIsHeld_SurvivesTheSweep_UntilTheLockIsReleased()
    {
        // Young first, the lock taken, THEN aged: a folder aged while its lock is still free is one the real sweep of a parallel class could take in that window.
        var scratch = StrandedQuestionScratch(TimeSpan.Zero);
        var copy = Path.Combine(scratch, "0", "repo-a", "a.txt");
        try
        {
            var held = HeldFile.Take(Path.Combine(scratch, QuestionRowLaunch.LockName)).Should().BeOfType<Hold.Taken>().Subject;
            using (held.Stream)
            {
                Directory.SetLastWriteTimeUtc(scratch, DateTime.UtcNow.AddHours(-7));
                PanelService.ForgetTheLastSweep();
                PanelService.PruneOldAnswerDirs(_dir);

                Directory.Exists(scratch).Should().BeTrue("a live row holds its lock, so the sweep keeps its scratch however old it looks");
                File.Exists(copy).Should().BeTrue("the copies a live row is reading are untouched");
                File.Exists(Path.Combine(scratch, FirstEntry)).Should().BeTrue("nothing of a live row's folder is removed, not even what is listed before its lock");
            }

            Directory.SetLastWriteTimeUtc(scratch, DateTime.UtcNow.AddHours(-7));
            PanelService.ForgetTheLastSweep();
            PanelService.PruneOldAnswerDirs(_dir);

            Directory.Exists(scratch).Should().BeFalse("once the row let go of its lock, the next sweep removes the scratch");
        }
        finally
        {
            DeleteQuietly(scratch);
        }
    }

    [Fact]
    public void AQuestionRowScratch_YoungerThanTheWindow_IsKept_WithItsLockFree()
    {
        var scratch = StrandedQuestionScratch(TimeSpan.FromHours(1));
        try
        {
            PanelService.ForgetTheLastSweep();
            PanelService.PruneOldAnswerDirs(_dir);

            Directory.Exists(scratch).Should().BeTrue("a scratch inside the six-hour window is a row that may have just ended, never swept by looking");
        }
        finally
        {
            DeleteQuietly(scratch);
        }
    }
}
