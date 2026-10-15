using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An adapter's refusal states its CAUSE (D5 of <c>research/PLAN_vibe_live_round_fixes.md</c>, the code gate of 2026-10-15): it has no
/// default, because a default made every adapter's refusal read as coai's own launch check saying no to an installed CLI — and a
/// second adapter (Kimi's) will refuse launches too.
/// </summary>
public sealed class ARefusalStatesItsCauseTests
{
    private static readonly IConsultantRuntime Consultant = new CodexConsultant(new CodexRuntime());

    /// <summary>The smallest adapter there is: it refuses every launch, for the cause it was given.</summary>
    private sealed class Refusing(RefusalCause cause) : IReviewerRuntime
    {
        public string Provider => "refuser";

        public ReviewerInvocation Build(string role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings) =>
            new("refuser", role, new ProcessRequest("refuser", [], "."), string.Empty, this);

        public string? ReadAnswer(ReviewerInvocation invocation, ProcessResult result) => null;

        public Task<LaunchTurn> TakeTurnAsync(ReviewerInvocation invocation, IProcessLauncher launcher, CancellationToken ct) =>
            Task.FromResult(LaunchTurn.Refused("the refuser said no", cause));
    }

    private static async Task<ReviewerOutcome> RefusedAs(RefusalCause cause)
    {
        var adapter = new Refusing(cause);
        var invocation = adapter.Build("Architecture", "p", ".", "s.json", ".", new ReviewerSettings("refuser"));

        return await new ReviewerExecutor(new NeverLaunches()).RunAsync(invocation, ct: TestContext.Current.CancellationToken);
    }

    private sealed class NeverLaunches : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("a refused launch starts nothing");
    }

    [Fact]
    public async Task ALaunchControlRefusal_IsMarkedAsTheControlsOwn_AndClassifiedLaunchRefused()
    {
        var outcome = await RefusedAs(RefusalCause.LaunchControl);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.ByLaunchControl.Should().BeTrue();
        Kind(outcome).Should().Be("launch-refused");
    }

    [Fact]
    public async Task AMissingCliRefusal_StaysCliNotFound()
    {
        var outcome = await RefusedAs(RefusalCause.CliMissing);

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.ByLaunchControl.Should().BeFalse();
        Kind(outcome).Should().Be("cli-not-found");
    }

    private static string Kind(ReviewerOutcome outcome) =>
        ConsultFailures.Classify(Consultant, new ReviewerLaunch(outcome, null, Usage.None, string.Empty, new ProcessResult(1, string.Empty, string.Empty, false)),
            "refuser", new ConsultFailure.Timeout()).Kind;
}
