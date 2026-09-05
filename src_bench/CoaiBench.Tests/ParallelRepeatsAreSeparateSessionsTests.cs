using Xunit;
using FluentAssertions;
using CoaiBench.Cli;
using CoaiBench.Model;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>
/// Two repeats of one case, run at once, are two sessions on two branches — not one session that
/// two servers fight over.
/// </summary>
/// <remarks>
/// <para>The runner's own remark promised it: <i>"Each run gets its own BRANCH —
/// bench/&lt;arm&gt;-&lt;case&gt;-&lt;repeat&gt;"</i>. The code handed the case's COMMIT as the branch, so
/// three parallel repeats of one case shared one session key, one session file and one worktree
/// name. The five-window campaign of 2026-09-05 found it in its first minute:
/// <c>fatal: 'coai-wt-a0fc7e7d-r1' already exists</c> — two servers creating the same worktree for
/// the same repo+branch+round — and the session files on disk showed one round per branch while
/// three lanes were writing into it.</para>
/// <para>A session is keyed by repo+branch, and the branch has to be a real ref because the server
/// diffs it. So each run gets a real ref of its own at the case's commit, named for the run.</para>
/// </remarks>
public sealed class ParallelRepeatsAreSeparateSessionsTests
{
    private static readonly Case Reviewed = new("split-once", "artifacts/bench/plan-B.md", "7133c2f", "4a27a17");
    private static readonly Case PlanOnly = new("engine-lease", "research/PLAN_engine_lease.md");

    [Fact]
    public void ARunOfAReviewedCase_HasItsOwnBranch_NamedForTheRun()
    {
        Bench.BranchFor(new Cell(Reviewed, "codex,gemini,local", Repeat: 2))
            .Should().Be("bench/split-once-r2");
    }

    [Fact]
    public void TwoRepeatsOfOneCase_AreTwoBranches()
    {
        var first = Bench.BranchFor(new Cell(Reviewed, "codex", 1));
        var second = Bench.BranchFor(new Cell(Reviewed, "codex", 2));

        first.Should().NotBe(second, "or they are one session two servers write into");
    }

    [Fact]
    public void APlanOnlyCase_ReviewsTheCheckoutAsItStands()
    {
        // No commit means nothing to check out: the session is keyed on HEAD, as before.
        Bench.BranchFor(new Cell(PlanOnly, "codex", 1)).Should().Be("HEAD");
    }

    [Fact]
    public void TheRefAlwaysPointsAtTheCaseCommit()
    {
        // What the branch is FOR: the server diffs baseRef..branch, so the run's own ref must be the
        // reviewed commit and nothing newer.
        Bench.RefTarget(Reviewed).Should().Be("7133c2f");
    }
}
