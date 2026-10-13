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

        var host = Host();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var loop = ConsultationSweeper.RunAsync(() => host.Current, TimeSpan.FromMilliseconds(20), Logger.None, stop.Token);
        for (var deadline = DateTime.UtcNow.AddSeconds(20); Directory.Exists(scratch) && DateTime.UtcNow < deadline; await Task.Delay(50, TestContext.Current.CancellationToken)) { }
        await stop.CancelAsync();
        await loop;

        Directory.Exists(scratch).Should().BeFalse("the beat sweeps the product's old scratch directories, snapshot included, with no round built");
    }
}
