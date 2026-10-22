using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A code round reads a checkout of the commit in its own worktree, and the worktree goes with the round however it ends. A WITHDRAWAL throws from
/// inside the round's scope, so the lease's disposal must run on that road as it does on the completed one: a cancel that leaves a mounted
/// checkout behind per withdrawn round would fill the disk of exactly the person who cancels a lot.
/// </summary>
[Collection("fakecli-env")]
public sealed class AWithdrawnCodeRoundLeavesNoWorktreeTests : IAsyncLifetime
{
    private WithdrawalRig _rig = null!;

    public async ValueTask InitializeAsync() => _rig = await WithdrawalRig.CreateAsync();

    public async ValueTask DisposeAsync() => await _rig.DisposeAsync();

    /// <summary>How many worktrees the repository lists, the main checkout included.</summary>
    private async Task<int> WorktreesAsync()
    {
        var listed = await new ProcessLauncher().RunAsync(
            new ProcessRequest("git", ["worktree", "list", "--porcelain"], _rig.Repo), TestContext.Current.CancellationToken);

        return listed.StdOut.Split('\n').Count(line => line.StartsWith("worktree ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AWithdrawnCodeRound_RemovesTheWorktreeItMounted()
    {
        var play = Play.Answer;
        var reviewers = _rig.Playing(_ => play);
        var service = _rig.Service(reviewers, ["codex"]);
        await service.OpenAsync(_rig.Repo, "feature");
        await service.ReviewPlanAsync(_rig.Repo, "feature", "a plan nobody objects to");
        await service.ResolveAsync(_rig.Repo, "feature", "[]");
        play = Play.BlockUntilCancelled;

        using var call = new CancellationTokenSource();
        var code = service.ReviewCodeAsync(_rig.Repo, "feature", "main", WithdrawalRig.Scope, again: false, call.Token);
        (await _rig.SessionAsync(service, s => s.Rounds.LastOrDefault() is { Status: "running", Stage: "CodeReview" }, TimeSpan.FromSeconds(60))).Should().BeTrue();
        await reviewers.Of("codex").Arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        (await WorktreesAsync()).Should().Be(2, "while the round runs, the repository has its own checkout and the round's");

        await call.CancelAsync();
        var thrown = await Record.ExceptionAsync(() => code);

        thrown.Should().BeAssignableTo<OperationCanceledException>();
        _rig.LastRound(service).Status.Should().Be(RoundRecord.Cancelled);
        (await WorktreesAsync()).Should().Be(1, "the withdrawn round's worktree is removed as the completed round's is");
        var root = Path.Combine(_rig.Data, "worktrees");
        (Directory.Exists(root) ? Directory.GetDirectories(root) : []).Should().BeEmpty("nothing of the round is left on disk");
    }
}
