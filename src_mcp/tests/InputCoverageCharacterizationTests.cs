using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Pins every sentence <see cref="InputCoverage.Of"/> can answer and the boundary between them, so splitting
/// it to complexity 4 cannot move a reviewer from "unverified" to "incomplete" or back.
/// </summary>
/// <remarks>
/// Written against the code BEFORE the split and observed green there
/// (research/PLAN_security_lane_methods_within_complexity_4.md). <see cref="SecurityCoverageTests"/> asserts the
/// guarantee (usage never proves coverage); this file asserts the exact texts and the half-estimate edge.
/// </remarks>
public sealed class InputCoverageCharacterizationTests
{
    private const string NoUsage = "Input coverage unverified: the engine reported no prompt usage.";
    private const string Truncated = "Input coverage incomplete: reported prompt usage is less than half the UTF-8 / 4 estimate; possible input truncation.";
    private const string Plausible = "Input coverage unverified: reported usage passes the heuristic, but no tokenizer or explicit non-truncation evidence is available.";

    private static string Of(bool engine, int? promptBytes, Usage usage) =>
        InputCoverage.Of(
            new ReviewerWork(new("qwen", "redteam-sql", new ProcessRequest("fixture", [], "."), SharedResource: engine ? "engine" : ""),
                PromptBytes: promptBytes)
            { IsSecurity = true },
            new ReviewerOutcome.Ok(new([], []), false, usage));

    [Fact]
    public void A_hosted_reviewer_gets_no_coverage_sentence_whatever_it_reported()
    {
        Of(false, 8000, Usage.Unknown).Should().BeEmpty();
        Of(false, 8000, new Usage(1, 0, null)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(5, true)]
    public void An_engine_that_reported_nothing_usable_is_unverified(long tokensIn, bool notCaptured) =>
        Of(true, 8000, new Usage(tokensIn, 0, null, NotCaptured: notCaptured)).Should().Be(NoUsage);

    [Theory]
    [InlineData(8000, 999, Truncated)]
    [InlineData(8000, 1000, Plausible)]
    [InlineData(8000, 5000, Plausible)]
    [InlineData(2, 1, Plausible)]
    [InlineData(4, 1, Plausible)]
    [InlineData(12, 1, Truncated)]
    public void Below_half_the_estimate_is_incomplete_and_at_half_it_passes(int promptBytes, long tokensIn, string expected) =>
        Of(true, promptBytes, new Usage(tokensIn, 0, null)).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Without_a_prompt_size_there_is_no_estimate_to_fall_short_of(int? promptBytes) =>
        Of(true, promptBytes, new Usage(1, 0, null)).Should().Be(Plausible);
}
