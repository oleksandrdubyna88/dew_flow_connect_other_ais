using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Pins the lane's sentence in the round reply (<see cref="SecurityRound.Clause"/>) and when it raises its
/// durable notice (<see cref="SecurityRound.Notice"/>), arm by arm, so splitting both to complexity 4 cannot
/// change a word or a decision.
/// </summary>
/// <remarks>
/// Written against the code BEFORE the split and observed green there
/// (research/PLAN_security_lane_methods_within_complexity_4.md). The existing tests assert how a clause STARTS;
/// this file asserts it whole — the counts, the per-pair tail, and the trailing space the reply relies on.
/// </remarks>
public sealed class SecurityRoundCharacterizationTests
{
    private static ReviewerWork Lane(string provider, string prompt, bool engine = false) =>
        new(new(provider, prompt, new ProcessRequest("fixture", [], "."), SharedResource: engine ? "engine" : "")) { IsSecurity = true };

    private static ReviewerWork Ordinary(string provider) =>
        new(new(provider, RoleCatalog.ArchitectureRole, new ProcessRequest("fixture", [], ".")));

    private static readonly ReviewerOutcome Answered = new ReviewerOutcome.Ok(new([], []), false);
    private static readonly ReviewerOutcome Failed = new ReviewerOutcome.NonZeroExit(7, "fixture failure");

    private static RoundWork Active(IReadOnlyList<ReviewerWork> reviewers, IReadOnlyList<ExcludedRole>? excluded = null) =>
        new(reviewers, [], excluded ?? []) { SecurityActive = true };

    private static (ReviewerInvocation, ReviewerOutcome) Result(ReviewerWork work, ReviewerOutcome outcome) => (work.Invocation, outcome);

    [Fact]
    public void An_inactive_lane_says_nothing()
    {
        var lane = Lane("codex", "redteam-sql");
        SecurityRound.Clause([Result(lane, Failed)], new RoundWork([lane], [])).Should().BeEmpty();
    }

    [Fact]
    public void No_lane_result_with_an_excluded_pairing_is_incomplete()
    {
        var work = Active([Ordinary("codex")], [new("qwen", "redteam-sql", "reviewer row disabled")]);
        SecurityRound.Clause([Result(Ordinary("codex"), Answered)], work)
            .Should().Be("Security lane incomplete: configured pairings could not run. ");
    }

    [Fact]
    public void No_lane_result_and_only_an_ordinary_role_excluded_is_a_skip()
    {
        var work = Active([], [new("qwen", RoleCatalog.ArchitectureRole, "credentials unavailable")]);
        SecurityRound.Clause([], work)
            .Should().Be("Security lane skipped: no pairing was due (conditions, stage selection or round budget). ");
    }

    [Fact]
    public void A_result_whose_row_is_not_lane_work_does_not_count_as_a_lane_answer()
    {
        var lane = Lane("codex", "redteam-sql");
        var stranger = Lane("gemini", "redteam-sql");
        SecurityRound.Clause([Result(stranger, Answered)], Active([lane]))
            .Should().Be("Security lane skipped: no pairing was due (conditions, stage selection or round budget). ");
    }

    [Fact]
    public void Every_hosted_answer_complete_leaves_an_empty_tail()
    {
        var lane = Lane("codex", "redteam-sql");
        SecurityRound.Clause([Result(lane, Answered)], Active([lane])).Should().Be("Security lane: 1/1 complete answers.  ");
    }

    [Fact]
    public void An_engine_answer_alone_is_no_complete_answer_and_is_named_as_unverified()
    {
        var lane = Lane("qwen", "redteam-sql", engine: true);
        SecurityRound.Clause([Result(lane, Answered)], Active([lane])).Should().Be(
            "Security lane incomplete: no complete answer. qwen/redteam-sql: input coverage unverified; findings retained as evidence ");
    }

    [Fact]
    public void A_partial_lane_counts_the_complete_answers_and_names_the_rest_in_order()
    {
        var hosted = Lane("codex", "redteam-sql");
        var broken = Lane("gemini", "redteam-authz");
        var local = Lane("qwen", "redteam-xss", engine: true);
        var ordinary = Ordinary("codex");
        var clause = SecurityRound.Clause(
            [Result(broken, Failed), Result(ordinary, Answered), Result(hosted, Answered), Result(local, Answered)],
            Active([hosted, broken, local, ordinary]));
        clause.Should().Be("Security lane: 1/3 complete answers. gemini/redteam-authz: " + ReviewerSummaryFactory.Describe(Failed)
            + "; qwen/redteam-xss: input coverage unverified; findings retained as evidence ");
    }

    [Fact]
    public void Only_failures_are_no_complete_answer()
    {
        var lane = Lane("gemini", "redteam-authz");
        var engineFailure = Lane("qwen", "redteam-sql", engine: true);
        SecurityRound.Clause([Result(lane, Failed), Result(engineFailure, Failed)], Active([lane, engineFailure])).Should().Be(
            "Security lane incomplete: no complete answer. gemini/redteam-authz: " + ReviewerSummaryFactory.Describe(Failed)
            + "; qwen/redteam-sql: " + ReviewerSummaryFactory.Describe(Failed) + " ");
    }

    private static List<ServerNotice> Noticed(IReadOnlyList<(ReviewerInvocation, ReviewerOutcome)> results, RoundWork work)
    {
        var notices = new List<ServerNotice>();
        SecurityRound.Notice(results, work, new Noticing(n => { notices.Add(n); return true; }, Serilog.Core.Logger.None));
        return notices;
    }

    [Fact]
    public void An_inactive_lane_raises_nothing_even_when_its_pairing_failed()
    {
        var lane = Lane("codex", "redteam-sql");
        Noticed([Result(lane, Failed)], new RoundWork([lane], [], [new("qwen", "redteam-sql", "broken")])).Should().BeEmpty();
    }

    [Fact]
    public void One_complete_hosted_answer_silences_the_notice_beside_failures()
    {
        var hosted = Lane("codex", "redteam-sql");
        var broken = Lane("gemini", "redteam-authz");
        Noticed([Result(hosted, Answered), Result(broken, Failed)], Active([hosted, broken], [new("x", "redteam-xss", "broken")]))
            .Should().BeEmpty();
    }

    [Fact]
    public void Nothing_due_and_nothing_broken_raises_nothing()
    {
        Noticed([], Active([], [new("qwen", RoleCatalog.ArchitectureRole, "credentials unavailable")])).Should().BeEmpty();
    }

    [Fact]
    public void A_broken_pairing_with_no_lane_result_raises_the_notice_with_the_clause()
    {
        var work = Active([], [new("qwen", "redteam-sql", "reviewer row disabled")]);
        Noticed([], work).Should().ContainSingle().Which.Should().Match<ServerNotice>(n =>
            n.Class == "failure" && n.Source == "coai-mcp" && n.Code == ServerNoticeCodes.SecurityLaneIncomplete
            && n.Subject == SecurityCatalog.Gate && n.Title == "Security lane incomplete: no complete answer"
            && n.Detail == ServerNotice.Shortened("Security lane incomplete: configured pairings could not run. "));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_lane_with_only_engine_answers_or_failures_raises_the_notice(bool engineAnswer)
    {
        var lane = Lane("qwen", "redteam-sql", engine: engineAnswer);
        (ReviewerInvocation, ReviewerOutcome)[] results = [Result(lane, engineAnswer ? Answered : Failed)];
        var work = Active([lane]);
        Noticed(results, work).Should().ContainSingle().Which.Detail.Should().Be(ServerNotice.Shortened(SecurityRound.Clause(results, work)));
    }
}
