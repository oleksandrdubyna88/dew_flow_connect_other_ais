using Xunit;
using FluentAssertions;
using CoaiBench.Cli;
using CoaiBench.Model;
using CoaiBench.Reporting;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>The matrix, the lanes, and the tables — everything that does not need a real round.</summary>
public sealed class BenchMatrixTests
{
    private static readonly Case Plan = new("plan-a", "todo/PLAN_a.md", "abc123", "abc122");
    private static readonly Case Other = new("plan-b", "todo/PLAN_b.md");

    private static Bench BenchOf(Options options, params Case[] corpus) =>
        new(options, corpus, _ => { });

    [Fact]
    public void EveryCaseTimesEveryArmTimesEveryRepeat()
    {
        var bench = BenchOf(
            new Options { Arms = ["codex", "gemini"], Repeat = 3 }, Plan, Other);

        bench.Cells().Should().HaveCount(12, "2 cases x 2 arms x 3 repeats");
    }

    [Fact]
    public void LanesDefaultToTheNumberOfArms()
    {
        // Asked for explicitly: comparing three vendors is three arms with nothing to say to each
        // other, and running them in series triples the evening for no reason.
        BenchOf(new Options { Arms = ["codex", "gemini", "local"] }, Plan).Lanes().Should().Be(3);
    }

    [Fact]
    public void OneArm_IsOneLane() =>
        BenchOf(new Options { Arms = ["codex"] }, Plan).Lanes().Should().Be(1);

    [Fact]
    public void AnExplicitNumberWins_BecauseThatIsTheWindowsCase() =>
        BenchOf(new Options { Arms = ["codex"], Parallel = 5 }, Plan).Lanes().Should().Be(5);

    // ---------- the tables ----------

    private static RunRecord RunOf(string arm, string verdict, int findings, double seconds, string useful = "unjudged") =>
        new(Plan, arm, 1, 1)
        {
            Stages =
            [
                new StageResult("plan", seconds, Verdict: verdict, GatingCount: 1, TokensIn: 40_000, TokensOut: 4_000)
                {
                    Findings = [.. Enumerable.Range(0, findings).Select(i =>
                        new Finding(Title: $"finding {i}") { Useful = useful })],
                },
            ],
        };

    [Fact]
    public void AnUnjudgedRunSaysSo_RatherThanZero()
    {
        // Zero is a measurement. "Nobody has looked" is not, and a table that prints one for the
        // other is a table that will be quoted.
        var table = Tables.PerArm([RunOf("codex", "proceed", 3, 12)]);

        table.Should().Contain("| — |");
    }

    [Fact]
    public void AJudgedRunCountsWhatWasWorthHaving()
    {
        var table = Tables.PerArm([RunOf("codex", "proceed", 2, 12, useful: "yes")]);

        table.Should().Contain("2/2");
    }

    [Fact]
    public void AFailedRunIsNamed_NeverAveragedAway()
    {
        // Three runs where one produced nothing is not "mostly fine": the verdict column says both.
        var table = Tables.PerArm(
        [
            RunOf("codex", "proceed", 1, 10),
            RunOf("codex", "proceed", 1, 12),
            RunOf("codex", "", 0, 300),
        ]);

        table.Should().Contain("FAILED");
    }

    [Fact]
    public void ARunThatNeverStarted_StillHasARow()
    {
        var table = Tables.PerRun([new RunRecord(Plan, "codex", 1, 1) { HarnessError = "the server did not start" }]);

        table.Should().Contain("NOTHING RAN");
    }

    [Fact]
    public void TheMedianIsTheMiddle_NotTheMean() =>
        Tables.Median([10, 12, 300]).Should().Be(12, "one rate-limited reviewer must not move the number");

    [Theory]
    [InlineData(950, "950")]
    [InlineData(40_000, "40k")]
    [InlineData(957_383, "957.4k")]
    [InlineData(1_400_000, "1.4M")]
    public void TokensReadAsAPersonWouldSayThem(long value, string expected) =>
        Tables.Thousands(value).Should().Be(expected);
}
