using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A LOCAL review line carries the round it belongs to — the Definition-of-Done item of
/// <c>research/PLAN_every_round_is_counted.md</c> ("every new ledger line carries round, attempt, effort/tier as sent with their
/// source"), which only the Team server's lines met.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> Story 4.1 minted the round key and stamped it on every <see cref="ReviewerInvocation"/>
/// (<c>RoundEngine</c> → <c>RoundWork.InRound</c>; <c>TurnLoop</c> carries it to a later turn), but the key reached only the
/// remote argv. <see cref="UsageLedger"/> never read <see cref="ReviewerInvocation.Round"/>, so a local plan round with one
/// reviewer wrote <c>attempt: "first"</c> and no round, and a local line could not be joined to <c>rounds.round_key</c>.
/// The plan deferred it "until the column exists"; the column has existed since story 4.2.</para>
/// <para><b>What must hold.</b> Every line of a review turn — the first launch, a repair, a follow-up, an earlier turn of a
/// conversation — carries the invocation's one key. A launch outside a round has no key and the line says none (empty,
/// which every reader reads as "not said"); a consultation, a question row and a probe are not rounds and write none.</para>
/// </remarks>
public sealed class ALocalReviewLineCarriesItsRoundTests : IDisposable
{
    private const string Round = "0123456789abcdef0123456789abcdef";

    private const string ProseWithUsage = """{"note":"prose, not findings","input_tokens":1000,"output_tokens":50}""";

    private const string CleanWithUsage = """{"findings": [], "input_tokens": 200, "output_tokens": 7}""";

    private readonly TempDir _dir = TempDir.For("coai-local-round-");

    public void Dispose() => _dir.Dispose();

    private static ReviewerInvocation Inside(string round) =>
        new("gemini", RoleCatalog.ArchitectureRole, new ProcessRequest("vendor", [], ".") { Timeout = TimeSpan.FromMinutes(1) })
        {
            Round = round,
        };

    private static ProcessResult Exited(int code, string stdOut) => new(code, stdOut, string.Empty, TimedOut: false);

    private static List<JsonElement> Written(UsageLedger ledger)
    {
        var lines = new List<JsonElement>();
        foreach (var line in File.ReadAllLines(ledger.Path))
        {
            using var doc = JsonDocument.Parse(line);
            lines.Add(doc.RootElement.Clone());
        }

        return lines;
    }

    private static string RoundOf(JsonElement line) =>
        line.TryGetProperty("round", out var value) ? value.GetString() ?? string.Empty : string.Empty;

    [Fact]
    public void AReviewLaunchInsideARound_WritesTheInvocationsRound()
    {
        var ledger = new UsageLedger(_dir.At("one"));

        ledger.Record(Inside(Round), new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(10, 2, null)), "m", "PlanReview", TimeSpan.FromSeconds(3));

        var line = Written(ledger).Should().ContainSingle().Subject;
        RoundOf(line).Should().Be(Round, "the round key is what joins a local line to rounds.round_key");
        line.GetProperty("attempt").GetString().Should().Be(LaunchAttempts.First);
    }

    [Fact]
    public async Task ARepairOfTheSameReviewer_CarriesTheSameRound()
    {
        var outcome = await new ReviewerExecutor(new ScriptedResults(Exited(0, ProseWithUsage), Exited(0, CleanWithUsage)))
            .RunAsync(Inside(Round), Inside(Round), TestContext.Current.CancellationToken);
        var ledger = new UsageLedger(_dir.At("repair"));

        ledger.Record(Inside(Round), outcome, "m", "CodeReview", TimeSpan.FromSeconds(9));

        var lines = Written(ledger);
        lines.Select(l => l.GetProperty("attempt").GetString()).Should().Equal(LaunchAttempts.First, LaunchAttempts.Repair);
        lines.Select(RoundOf).Should().Equal([Round, Round], "the malformed first launch and its repair are one round's launches");
    }

    [Fact]
    public void EveryTurnOfAConversation_CarriesTheSameRound()
    {
        var ledger = new UsageLedger(_dir.At("turns"));
        TurnUsage[] earlier =
        [
            new(1, new Usage(1000, 50, null), TimeSpan.FromSeconds(3)),
            new(2, new Usage(1100, 40, null), TimeSpan.FromSeconds(4)),
        ];

        ledger.Record(Inside(Round), new ReviewerOutcome.TimedOut { EarlierTurns = earlier }, "m", "FeatureReview", TimeSpan.FromSeconds(12));

        Written(ledger).Select(RoundOf).Should().Equal([Round, Round, Round], "two answered turns and the one that timed out are one round's");
    }

    [Fact]
    public void ALaunchOutsideARound_WritesNoRound_NeverAnEmptyKeyAReaderWouldCount()
    {
        var ledger = new UsageLedger(_dir.At("outside"));

        ledger.Record(Inside(string.Empty), new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(10, 2, null)), "m", "PlanReview", TimeSpan.FromSeconds(3));

        RoundOf(Written(ledger).Should().ContainSingle().Subject).Should().BeEmpty("no key was minted, and none is invented");
    }

    [Theory]
    [InlineData(UsageKinds.Consult, "Consultation")]
    [InlineData(UsageKinds.Question, "QuestionConsult")]
    public void AConsultOrQuestionLine_WritesNoRound(string kind, string stage)
    {
        var ledger = new UsageLedger(_dir.At("job-" + kind));

        ledger.RecordJob(string.Empty, "gemini", "m", RoleCatalog.ArchitectureRole, "ok", TimeSpan.FromSeconds(2), new Usage(10, 2, null), kind, stage);

        var line = Written(ledger).Should().ContainSingle().Subject;
        RoundOf(line).Should().BeEmpty("a consultation is not a round");
        line.GetProperty("kind").GetString().Should().Be(kind);
    }
}
