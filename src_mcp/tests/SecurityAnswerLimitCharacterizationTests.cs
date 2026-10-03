using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Pins the exact reason <see cref="SecurityAnswerLimit"/> refuses a lane answer with, and which reason wins
/// when an answer breaks several rules at once — the order is the protocol, and the reason is what the round
/// log shows.
/// </summary>
/// <remarks>
/// Written against the code BEFORE its methods were split to complexity 4 and observed green there
/// (todo/PLAN_security_lane_methods_within_complexity_4.md). <see cref="SecurityProtocolTests"/> drives the
/// real parser and asserts refusal; this file builds reviews directly so every rule can be reached alone.
/// </remarks>
public sealed class SecurityAnswerLimitCharacterizationTests
{
    private const string TooMany = "security response exceeds 100 findings";
    private const string TooLarge = "security evidence exceeds 131072 characters per response";
    private const string Prose = "security answers must contain only the declared status and valid findings, without prose notes";
    private const string Nit = "security findings must use blocking (CRITICAL), major (HIGH) or minor (MEDIUM); Low/Info are not accepted";
    private const string Evidence = "every security finding needs nonempty trigger, mechanism and consequence, at most 8000 characters total";

    private static readonly AttackEvidence Complete = new("t", "m", "c");

    private static Finding Defect(Severity severity = Severity.Major, AttackEvidence? attack = null, Reproduction? reproduction = null) =>
        new(severity, Category.Security, "A.cs", 1, "title", "why", "fix", ["qwen"])
        {
            AttackEvidence = attack ?? Complete,
            Reproduction = reproduction,
        };

    private static Finding Bare() => new(Severity.Major, Category.Security, "A.cs", 1, "title", "why", "fix", ["qwen"]);

    private static NormalisedReview Review(string? status, params Finding[] findings) =>
        new([.. findings], []) { SecurityStatus = status };

    private static readonly ReviewerWork Lane =
        new(new("qwen", "redteam-sql", new ProcessRequest("fixture", [], "."))) { IsSecurity = true };

    private static string Reason(NormalisedReview review)
    {
        var outcome = SecurityAnswerLimit.Apply(Lane, new ReviewerOutcome.Ok(review, false));
        return outcome is ReviewerOutcome.Unparseable refused ? refused.Reason : "kept";
    }

    private static Finding[] Many(int count) => [.. Enumerable.Range(0, count).Select(_ => Defect())];

    [Fact]
    public void Work_that_is_not_the_lanes_is_returned_untouched_whatever_it_holds()
    {
        var ordinary = Lane with { IsSecurity = false };
        var answer = new ReviewerOutcome.Ok(Review(null, Many(101)) with { Notes = "prose" }, false);
        SecurityAnswerLimit.Apply(ordinary, answer).Should().BeSameAs(answer);
    }

    [Fact]
    public void An_outcome_that_is_not_an_answer_is_returned_untouched()
    {
        var timedOut = new ReviewerOutcome.TimedOut();
        SecurityAnswerLimit.Apply(Lane, timedOut).Should().BeSameAs(timedOut);
    }

    [Fact]
    public void An_answer_that_keeps_every_rule_is_returned_as_the_same_instance()
    {
        var answer = new ReviewerOutcome.Ok(Review("FINDINGS", Defect()), false);
        SecurityAnswerLimit.Apply(Lane, answer).Should().BeSameAs(answer);
    }

    [Fact]
    public void A_refusal_keeps_what_the_answer_consumed_and_its_earlier_turns()
    {
        var usage = new Usage(10, 20, 0.5);
        TurnUsage[] earlier = [new(1, new Usage(3, 4, null), TimeSpan.FromSeconds(2))];
        var answer = new ReviewerOutcome.Ok(Review("SECURE", Defect()), false, usage) { EarlierTurns = earlier };
        var refused = SecurityAnswerLimit.Apply(Lane, answer).Should().BeOfType<ReviewerOutcome.Unparseable>().Subject;
        refused.Reason.Should().Be("security status must be FINDINGS for this findings list");
        refused.Usage.Should().Be(answer.LastTurnUsage);
        refused.EarlierTurns.Should().BeSameAs(earlier);
    }

    [Fact]
    public void One_hundred_findings_pass_the_count_and_one_more_does_not()
    {
        Reason(Review("FINDINGS", Many(100))).Should().Be("kept");
        Reason(Review("FINDINGS", Many(101))).Should().Be(TooMany);
    }

    [Fact]
    public void The_count_is_judged_before_every_other_rule()
    {
        var everything = Many(101).Append(Defect(Severity.Nit, reproduction: new("", new string('s', 131072), "", ""))).ToArray();
        Reason(Review(null, everything) with { Notes = "prose" }).Should().Be(TooMany);
    }

    [Fact]
    public void Reproduction_and_attack_evidence_are_summed_across_findings_up_to_the_limit()
    {
        // 131069 reproduction characters + 3 attack-evidence characters = exactly the limit.
        var atLimit = Defect(reproduction: new("p", new string('s', 131066), "e", "a"));
        Reason(Review("FINDINGS", atLimit)).Should().Be("kept");
        var overByOne = Defect(reproduction: new("p", new string('s', 131067), "e", "a"));
        Reason(Review("FINDINGS", overByOne)).Should().Be(TooLarge);
        var acrossTwo = new[] { Defect(reproduction: new("", new string('s', 65535), "", "")), Defect(reproduction: new("", new string('s', 65535), "", "")) };
        Reason(Review("FINDINGS", acrossTwo)).Should().Be(TooLarge);
    }

    [Fact]
    public void Attack_evidence_alone_counts_toward_the_size_and_its_absence_counts_as_nothing()
    {
        Reason(Review("FINDINGS", Defect(attack: new(new string('t', 131072), "m", "c")))).Should().Be(TooLarge);
        Reason(Review("FINDINGS", Bare())).Should().Be(Evidence, "no evidence at all is sized as zero and refused later, for its absence");
    }

    [Fact]
    public void The_size_is_judged_before_prose_status_severity_and_evidence()
    {
        var oversized = Defect(Severity.Nit, attack: new("", "", ""), reproduction: new("", new string('s', 131073), "", ""));
        Reason(Review("SECURE", oversized) with { Notes = "prose" }).Should().Be(TooLarge);
    }

    [Fact]
    public void Notes_or_rejected_entries_refuse_before_the_status_is_read()
    {
        Reason(Review("FINDINGS") with { Notes = "prose" }).Should().Be(Prose);
        Reason(new NormalisedReview([], [new RejectedEntry(0, "bad")]) { SecurityStatus = "SECURE" }).Should().Be(Prose);
        Reason(Review(null, Defect(Severity.Nit, attack: new("", "", ""))) with { Notes = "prose" }).Should().Be(Prose);
    }

    [Fact]
    public void The_status_must_match_the_findings_list()
    {
        Reason(Review("SECURE")).Should().Be("kept");
        Reason(Review(null)).Should().Be("security status must be SECURE for this findings list");
        Reason(Review("FINDINGS")).Should().Be("security status must be SECURE for this findings list");
        Reason(Review("secure")).Should().Be("security status must be SECURE for this findings list");
        Reason(Review("SECURE", Defect())).Should().Be("security status must be FINDINGS for this findings list");
        Reason(Review(null, Defect(Severity.Nit, attack: new("", "", "")))).Should().Be("security status must be FINDINGS for this findings list");
    }

    [Fact]
    public void A_nit_is_refused_before_incomplete_evidence()
    {
        Reason(Review("FINDINGS", Defect(), Defect(Severity.Nit))).Should().Be(Nit);
        Reason(Review("FINDINGS", Defect(Severity.Nit, attack: new("", "", "")))).Should().Be(Nit);
    }

    [Theory]
    [InlineData(Severity.Blocking)]
    [InlineData(Severity.Major)]
    [InlineData(Severity.Minor)]
    public void Every_other_severity_is_accepted(Severity severity) =>
        Reason(Review("FINDINGS", Defect(severity))).Should().Be("kept");

    [Fact]
    public void Every_finding_needs_complete_attack_evidence()
    {
        Reason(Review("FINDINGS", Defect(), Bare())).Should().Be(Evidence);
        Reason(Review("FINDINGS", Defect(attack: new("t", " ", "c")))).Should().Be(Evidence);
        Reason(Review("FINDINGS", Defect(attack: new("t", new string('m', 7998), "c")))).Should().Be("kept");
        Reason(Review("FINDINGS", Defect(attack: new("t", new string('m', 7999), "c")))).Should().Be(Evidence);
    }
}
