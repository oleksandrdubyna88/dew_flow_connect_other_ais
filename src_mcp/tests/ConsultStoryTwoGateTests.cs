using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>What story 2's plan round found — the four that were true.</summary>
public sealed class ConsultStoryTwoGateTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-s2-gate-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    // Story 2's finding that a consultant may read the tree and may not run a shell in it is now asserted by
    // ClaudeConsultantArgvTests, whose goldens own the whole claude argv (an allowlist since epic 3).

    [Fact]
    public void OnlyTheVendorThatReportsCumulativelySaysSo()
    {
        // Through the INTERFACE: it is a defaulted member, so "says nothing" and "says false" are the
        // same answer, which is the point — a new adapter is cumulative only if it declares itself so.
        ((IConsultantRuntime)new AntigravityConsultant(new AntigravityRuntime())).UsageIsCumulative.Should().BeTrue();

        ((IConsultantRuntime)new CodexConsultant(new CodexRuntime())).UsageIsCumulative.Should().BeFalse();
        ((IConsultantRuntime)new ClaudeConsultant(new ClaudeRuntime())).UsageIsCumulative.Should().BeFalse();
        ((IConsultantRuntime)new LocalConsultant(new LocalRuntime("local", LocalRuntime.DefaultEndpoint), "local"))
            .UsageIsCumulative.Should().BeFalse();
    }

    [Fact]
    public void TheNewestTurnIsCUT_WhenItAloneExceedsTheWholeBudget()
    {
        // A pasted stack trace does this. Dropping it would carry nothing but a note, leaving the
        // consultant with no idea what it last said.
        var huge = new string('z', 40_000);

        var transcript = ConsultantPrompt.Transcript([("earlier", "earlier advice"), ("the huge one", huge)], 2_000);

        transcript.Should().Contain("[CUT:");
        transcript.Should().Contain("the huge one");
        // INSIDE the budget it advertises: the marker's own length is reserved before the slice, so a
        // carry cannot exceed the number the record froze. Only the dropped-turns note sits outside.
        transcript.Length.Should().BeLessThan(2_000 + 120);
    }

    [Fact]
    public void AnAnswerFilePastRetentionIsRemoved_AndAFreshOneIsNot()
    {
        // Every turn leaves one, and they hold the working-tree diff and the caller's problem —
        // somebody's source code. Nothing was deleting them.
        var store = new ConsultationStore(_data);
        var answers = Directory.CreateDirectory(Path.Combine(store.Directory, "answers")).FullName;
        var old = Path.Combine(answers, "codex-consult-old.txt");
        var fresh = Path.Combine(answers, "codex-consult-fresh.txt");
        // A file this product did NOT write, exactly as old as the one it did. Age is not ownership,
        // and a sweep that removes what it did not put there is a sweep nobody can trust with a
        // directory. (codex, code round.)
        var somebodyElses = Path.Combine(answers, "notes-i-left-here.md");
        File.WriteAllText(old, "a diff nobody needs any more");
        File.WriteAllText(fresh, "a turn that is running right now");
        File.WriteAllText(somebodyElses, "mine, and just as old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-9));
        File.SetLastWriteTimeUtc(somebodyElses, DateTime.UtcNow.AddDays(-9));

        store.Sweep(_ => true, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7)).Should().Be(1);

        File.Exists(old).Should().BeFalse();
        File.Exists(fresh).Should().BeTrue("a file a running turn is still writing is younger than the window by definition");
        File.Exists(somebodyElses).Should().BeTrue("age is not ownership");
    }
}

/// <summary>What story 2's SECOND code round found.</summary>
public sealed class ConsultStoryTwoSecondRoundTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-s2b-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    [Fact]
    public void AnUnwritableSchemaDirectoryIsREPORTED_NotSwallowed()
    {
        // It returned a path to a file that was not there, so a read-only data directory surfaced
        // minutes later as a child process complaining about a missing schema — nowhere near the
        // permission that caused it.
        var blocked = Path.Combine(_data, "not-a-directory");
        File.WriteAllText(blocked, "a file, so no directory can be made inside it");

        var provisioned = ConsultSchemaFile.Ensure(Path.Combine(blocked, "schemas"));

        provisioned.Ready.Should().BeFalse();
        provisioned.Problem.Should().Contain("answer schema").And.Contain("local-engine");
        provisioned.Path.Should().NotBeEmpty("the path is still named, so the sentence can point at it");
    }

    [Fact]
    public void AWritableDirectoryIsReady_AndTheSchemaIsThere()
    {
        var provisioned = ConsultSchemaFile.Ensure(Path.Combine(_data, "schemas"));

        provisioned.Ready.Should().BeTrue();
        provisioned.Problem.Should().BeEmpty();
        File.ReadAllText(provisioned.Path).Should().Contain("\"answer\"");
    }

    [Fact]
    public void ProvisioningTwiceIsOneFile_AndTheSecondCallRewritesNothing()
    {
        var first = ConsultSchemaFile.Ensure(Path.Combine(_data, "schemas"));
        var written = File.GetLastWriteTimeUtc(first.Path);

        var second = ConsultSchemaFile.Ensure(Path.Combine(_data, "schemas"));

        second.Path.Should().Be(first.Path);
        File.GetLastWriteTimeUtc(second.Path).Should().Be(written, "an unchanged schema is not rewritten");
        Directory.EnumerateFiles(Path.Combine(_data, "schemas")).Should().ContainSingle("no temp file is left behind");
    }

    [Fact]
    public void ONLYTheRouteThatNeedsTheSchemaSaysSo()
    {
        // A read-only data directory must not stop a consultation on a route that never wanted the
        // file — which is three of the four.
        ((IConsultantRuntime)new LocalConsultant(new LocalRuntime("local", LocalRuntime.DefaultEndpoint), "local"))
            .NeedsAnswerSchema.Should().BeTrue();

        ((IConsultantRuntime)new CodexConsultant(new CodexRuntime())).NeedsAnswerSchema.Should().BeFalse();
        ((IConsultantRuntime)new ClaudeConsultant(new ClaudeRuntime())).NeedsAnswerSchema.Should().BeFalse();
        ((IConsultantRuntime)new AntigravityConsultant(new AntigravityRuntime())).NeedsAnswerSchema.Should().BeFalse();
    }

    [Fact]
    public void TheNameASWEEPDeletesIsTheNameANADAPTERWROTE()
    {
        // The writer and the deleter are in different projects. An ad-hoc substring check in the
        // sweep would leak files the day an adapter renamed its output.
        var written = ConsultantArtefacts.Name("codex", ".txt");

        ConsultantArtefacts.Ours(written).Should().BeTrue();
        ConsultantArtefacts.Ours("local-consult-abc123.prompt").Should().BeTrue();
        ConsultantArtefacts.Ours("notes-i-left-here.md").Should().BeFalse();
        ConsultantArtefacts.Ours("codex-architecture-abc.txt").Should().BeFalse("a REVIEW artefact is not ours to sweep");
    }

    [Fact]
    public void TwoLaunchesNeverShareAnArtefactName()
    {
        ConsultantArtefacts.Name("codex", ".txt").Should().NotBe(ConsultantArtefacts.Name("codex", ".txt"));
    }
}

/// <summary>The cumulative-usage rule itself, in the core, where the service reads it from.</summary>
/// <remarks>
/// Against the running total the record keeps (<see cref="ConsultationBilled"/>) since epic 2 of
/// PLAN_the_consultant_works_on_every_vendor.md — it was a list of the answered turns, which billed a failed
/// or interrupted turn again inside the next answer — and through the one subtraction, <c>Less</c>.
/// </remarks>
public sealed class CumulativeUsageTests
{
    private static Usage Reported(long tokensIn, long tokensOut, double? cost = null) => new(tokensIn, tokensOut, cost);

    [Fact]
    public void TheRunningTotalIsWhatGetsSubtracted()
    {
        // The REAL numbers, from the live check: turn 1 reported 14 138 in / 1 342 out and turn 2
        // reported 30 843 / 1 393, which is turn 1 plus turn 2. Left alone the ledger counts turn 1
        // again on every later turn.
        var share = ConsultationUsage.Less(Reported(30_843, 1_393), new ConsultationBilled(14_138, 1_342));

        share.TokensIn.Should().Be(16_705);
        share.TokensOut.Should().Be(51);
    }

    [Fact]
    public void TheFirstTurnIsWhatTheVendorSaid()
    {
        ConsultationUsage.Less(Reported(14_138, 1_342), new ConsultationBilled()).TokensIn.Should().Be(14_138);
    }

    [Fact]
    public void ItKeepsSubtractingAcrossThreeTurns()
    {
        var billed = new ConsultationBilled().Plus(Reported(100, 10)).Plus(Reported(250, 25));

        ConsultationUsage.Less(Reported(600, 60), billed).Should().Be(Reported(250, 25));
    }

    [Fact]
    public void ACountBelowWhatWasBilled_FloorsAtZero_NeverNegative()
    {
        // The totals moved on, the cached and reasoning counts did not: a number this arithmetic does not understand, and a
        // negative spending row would be worse than a flat one — every field is floored.
        var share = ConsultationUsage.Less(new Usage(9_500, 110, null, TokensCached: 3, TokensReasoning: 1), new ConsultationBilled(9_000, 100, 0, 40, 5));

        share.Should().Be(new Usage(500, 10, null));
    }

    /// <summary>
    /// E5.4 deviation 31, antigravity's half: only agy's INPUT counter is measured cumulative (research/RESULTS_agy_consult_follow_up.md §3 — its output
    /// is not settled), so an output below what was billed is no restarted counter: the input is billed as its delta, the output floored.
    /// </summary>
    [Fact]
    public void AnAgyReport_WhoseOutputIsBelowWhatWasBilled_IsNoReset_AndItsInputIsADelta()
    {
        IConsultantRuntime agy = new AntigravityConsultant(new AntigravityRuntime());

        var share = ConsultationUsage.Less(Reported(30_843, 50), new ConsultationBilled(14_138, 1_342), agy.UsageCounterMayRestart);

        agy.UsageCounterMayRestart.Should().BeFalse("only agy's input is measured cumulative, so it declares no restart");
        share.TokensIn.Should().Be(16_705, "the input counter moved on by 16 705; billing its whole 30 843 again would count turn 1 twice");
        share.TokensOut.Should().Be(0);
    }

    /// <summary>
    /// E5.4 deviation 31: three reports of a vendor that declares a restart — a total, a restarted total, the restarted session's next total.
    /// The third is measured against the SECOND report (the counter), never against the sum of the shares (25 000 / 39), which would bill it whole.
    /// </summary>
    [Fact]
    public void AfterAReset_TheNextReportIsMeasuredAgainstTheRestartedCounter()
    {
        var first = ConsultationUsage.Next(Reported(20_000, 30), new ConsultationBilled(), mayRestart: true);
        var second = ConsultationUsage.Next(Reported(5_000, 9), first.Counter, mayRestart: true);
        var third = ConsultationUsage.Next(Reported(9_000, 15), second.Counter, mayRestart: true);

        (second.Share.TokensIn, second.Share.TokensOut).Should().Be((5_000L, 9L), "the restart is billed whole");
        (third.Share.TokensIn, third.Share.TokensOut).Should().Be((4_000L, 6L), "the restarted session's next total is billed its delta");
        third.Counter.Should().Be(new ConsultationBilled(9_000, 15), "the counter is the vendor's own last report");
        ConsultationUsage.Next(Usage.Unknown, third.Counter, mayRestart: true).Counter.Should().Be(third.Counter, "an uncaptured report moves no counter");
    }

    /// <summary>
    /// E5.4 deviations 31 and 35: agy declares no restart, and its next report is measured against its LAST report, not the sum of what was billed —
    /// the rule a question row's agy lookup and a consultation share (both call <see cref="ConsultationUsage.Next"/>). The output of turn 2 is below
    /// turn 1's (agy's output is not measured cumulative): floored to 0, and turn 3's output is billed past turn 2's report (10), where the sum of the
    /// shares (1 342) would have floored it to 0 again.
    /// </summary>
    [Fact]
    public void AnAgyCounter_IsMeasuredAgainstItsLastReport_AcrossThreeTurns()
    {
        IConsultantRuntime agy = new AntigravityConsultant(new AntigravityRuntime());

        var first = ConsultationUsage.Next(Reported(14_138, 1_342), new ConsultationBilled(), agy.UsageCounterMayRestart);
        var second = ConsultationUsage.Next(Reported(30_843, 50), first.Counter, agy.UsageCounterMayRestart);
        var third = ConsultationUsage.Next(Reported(31_000, 60), second.Counter, agy.UsageCounterMayRestart);

        (second.Share.TokensIn, second.Share.TokensOut).Should().Be((16_705L, 0L), "no restart: the input is a delta, the output floored");
        second.Counter.Should().Be(new ConsultationBilled(30_843, 50), "the counter is agy's own last report");
        (third.Share.TokensIn, third.Share.TokensOut).Should().Be((157L, 10L), "measured against turn 2's report, never against the sum of the shares");
    }

    /// <summary>
    /// A turn that reached no model reports <see cref="Usage.None"/> — captured, and all zeros: an empty launch list (the deadline struck in the
    /// wait for the sign-in's gate), a refused or not-started launch, a timed-out one. That is "this turn consumed nothing", never "the vendor's
    /// counter is now 0": it moves no counter and is no restart, so the NEXT real report is still measured against the last one (the E5.4 review
    /// of 2026-10-27, R1 — moving it to 0/0 billed turn 1 again inside turn 3).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReportOfNothing_MovesNoCounter_AndIsNoReset(bool mayRestart)
    {
        var first = ConsultationUsage.Next(Reported(20_000, 30), new ConsultationBilled(), mayRestart);
        var nothing = ConsultationUsage.Next(Usage.None, first.Counter, mayRestart);
        var third = ConsultationUsage.Next(Reported(20_004, 42), nothing.Counter, mayRestart);

        (nothing.Share.TokensIn, nothing.Share.TokensOut).Should().Be((0L, 0L), "a turn that reached no model consumed nothing");
        nothing.Counter.Should().Be(first.Counter, "a report of nothing is not the vendor's counter");
        ConsultationUsage.IsAReset(Usage.None, first.Counter).Should().BeFalse("nothing reported is no restart");
        (third.Share.TokensIn, third.Share.TokensOut).Should().Be((4L, 12L), "the next real report is billed its delta, not turn 1 again");
    }

    /// <summary>
    /// S5.4.14 (decision 12 of E5.4, <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): for a vendor that declares its counters can restart, a CAPTURED
    /// total below its counter's last report is a counter that RESTARTED — this turn is billed what the report said, whole, never a flat zero. Both
    /// totals are judged together (one counter below means the pair restarted); an uncaptured report is zeros that are not a counter, never a reset.
    /// </summary>
    [Theory]
    [InlineData(5_000, 9, false, 5_000, 9)]
    [InlineData(25_000, 20, false, 25_000, 20)]
    [InlineData(0, 0, true, 0, 0)]
    public void ATotalBelowWhatWasBilled_IsAReset_AndTheTurnIsBilledWhatItSaid(long reportedIn, long reportedOut, bool notCaptured, long shareIn, long shareOut)
    {
        var reported = notCaptured ? Usage.Unknown : new Usage(reportedIn, reportedOut, null, TokensCached: 7);

        var share = ConsultationUsage.Less(reported, new ConsultationBilled(20_000, 30, 0, 100), mayRestart: true);

        share.TokensIn.Should().Be(shareIn, "Expected the second turn's billed TokensIn to be the reset report's own");
        share.TokensOut.Should().Be(shareOut);
        share.NotCaptured.Should().Be(notCaptured, "the flags travel as the report has them");
        share.TokensCached.Should().Be(notCaptured ? 0 : 7, "a reset bills every field whole");
    }

    [Fact]
    public void MoneyIsSubtractedOnlyWhenThisReportNamesIt()
    {
        ConsultationUsage.Less(Reported(20, 2, 0.45), new ConsultationBilled(10, 1, 0.40)).CostUsd.Should().BeApproximately(0.05, 0.0001);
        ConsultationUsage.Less(Reported(20, 2), new ConsultationBilled(10, 1)).CostUsd.Should().BeNull("an unpriced report stays unpriced");
        ConsultationUsage.Less(Reported(20, 2, 0.3), new ConsultationBilled(10, 1)).CostUsd.Should().Be(0.3,
            "a total in which no turn was priced subtracts nothing from the first price named");
    }

    [Fact]
    public void TheTotalIsSummedByUsageAdd_SoItLosesNothing()
    {
        new ConsultationBilled(1, 2, 0.5, 3, 4).Plus(new Usage(10, 20, null, TokensCached: 30, TokensReasoning: 40))
            .Should().Be(new ConsultationBilled(11, 22, 0.5, 33, 44));
    }
}
