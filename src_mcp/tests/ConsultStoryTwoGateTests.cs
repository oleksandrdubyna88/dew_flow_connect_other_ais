using CoaiMcp.Core.Consultation;
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

    [Fact]
    public void AConsultantMayREADTheTree_AndMayNotRunAShellInIt()
    {
        // `--permission-mode plan` is the CLI's promise; a shell is a way around it, since rm, mv and
        // `sed -i` change a tree no edit tool was ever asked for. The gate's security reviewer named
        // it, and the filesystem invariant would have caught it AFTER the fact — which is the worse
        // half of a pair, not a substitute for the better one.
        var args = new ClaudeConsultant(new ClaudeRuntime())
            .Build(new ConsultantLaunch("D:/repo", "p", string.Empty, "D:/out", new ReviewerSettings("claude")))
            .Request.Arguments;

        args.Should().Contain("Bash", "a consultant has no reason to run a command");
        args.Should().Contain("Edit").And.Contain("Write").And.Contain("NotebookEdit");
        args.Should().Contain("WebFetch").And.Contain("WebSearch");
        args.Should().Contain("Task").And.Contain("Agent");

        // And the half that must NOT be denied: reading the tree is the whole job.
        args.Should().NotContain("Read").And.NotContain("Glob").And.NotContain("Grep");
    }

    [Fact]
    public void OnlyTheVendorThatReportsCumulativelySaysSo()
    {
        // Through the INTERFACE: it is a defaulted member, so "says nothing" and "says false" are the
        // same answer, which is the point — a new adapter is cumulative only if it declares itself so.
        ((IConsultantRuntime)new AntigravityConsultant(new AntigravityRuntime())).UsageIsCumulative.Should().BeTrue();

        ((IConsultantRuntime)new CodexConsultant(new CodexRuntime())).UsageIsCumulative.Should().BeFalse();
        ((IConsultantRuntime)new ClaudeConsultant(new ClaudeRuntime())).UsageIsCumulative.Should().BeFalse();
        ((IConsultantRuntime)new LocalConsultant(new LocalRuntime("local", LocalRuntime.DefaultEndpoint), "local", _data))
            .UsageIsCumulative.Should().BeFalse();
    }

    [Fact]
    public void TheNewestTurnIsCUT_WhenItAloneExceedsTheWholeBudget()
    {
        // A pasted stack trace does this. Dropping it would carry nothing but a note, leaving the
        // consultant with no idea what it last said.
        var huge = new string('z', 40_000);

        var transcript = ConsultantPrompt.Transcript([("earlier", "earlier advice"), ("the huge one", huge)], 2_000);

        transcript.Should().Contain("cut here");
        transcript.Should().Contain("the huge one");
        transcript.Length.Should().BeLessThan(3_000);
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
        File.WriteAllText(old, "a diff nobody needs any more");
        File.WriteAllText(fresh, "a turn that is running right now");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-9));

        store.Sweep(_ => true, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7)).Should().Be(1);

        File.Exists(old).Should().BeFalse();
        File.Exists(fresh).Should().BeTrue("a file a running turn is still writing is younger than the window by definition");
    }
}

/// <summary>The cumulative-usage rule itself, in the core, where the service reads it from.</summary>
public sealed class CumulativeUsageTests
{
    [Fact]
    public void TheRunningTotalIsWhatGetsSubtracted()
    {
        // The REAL numbers, from the live check: turn 1 reported 14 138 in / 1 342 out and turn 2
        // reported 30 843 / 1 393, which is turn 1 plus turn 2. Left alone the ledger counts turn 1
        // again on every later turn.
        var share = ConsultationUsage.ThisTurnsShare([(14_138, 1_342, null)], (30_843, 1_393, null));

        share.TokensIn.Should().Be(16_705);
        share.TokensOut.Should().Be(51);
    }

    [Fact]
    public void TheFirstTurnIsWhatTheVendorSaid()
    {
        ConsultationUsage.ThisTurnsShare([], (14_138, 1_342, null)).TokensIn.Should().Be(14_138);
    }

    [Fact]
    public void ItKeepsSubtractingAcrossThreeTurns()
    {
        ConsultationUsage.ThisTurnsShare([(100, 10, null), (250, 25, null)], (600, 60, null))
            .Should().Be((250L, 25L, (double?)null));
    }

    [Fact]
    public void AVendorReportingLESSThanBeforeFloorsAtZero_NeverNegative()
    {
        // A number this arithmetic does not understand; a negative spending row would be worse than
        // a flat one.
        ConsultationUsage.ThisTurnsShare([(9_000, 100, null)], (500, 10, null)).TokensIn.Should().Be(0);
    }

    [Fact]
    public void MoneyIsSubtractedOnlyWhenBothSidesNameIt()
    {
        ConsultationUsage.ThisTurnsShare([(10, 1, 0.40)], (20, 2, 0.45)).CostUsd.Should().BeApproximately(0.05, 0.0001);
        ConsultationUsage.ThisTurnsShare([(10, 1, null)], (20, 2, null)).CostUsd.Should().BeNull();
        ConsultationUsage.ThisTurnsShare([(10, 1, null)], (20, 2, 0.3)).CostUsd.Should().Be(0.3,
            "a vendor that priced only this turn is not reporting cumulatively about money");
    }
}
