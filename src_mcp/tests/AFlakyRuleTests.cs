using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Epoch 3 E4 s5 t4 (K3 to K6, K9 to K12, and K13 of the re-review): the rule of the re-run, pure — table-driven, one row for each line of SPEC section F.</summary>
public sealed class AFlakyRuleTests
{
    private static RunSummary Summary(int total = 10, int failed = 1, string[]? failures = null, string[]? passed = null, bool complete = true) =>
        new("mtp", true, total, failed, total - failed, failures ?? ["T.One"], passed ?? [], complete);

    private static RerunInput Input(Func<RerunInput, RerunInput>? change = null)
    {
        var input = new RerunInput(CheckStep.Test, "full", true, false, false, TimeSpan.FromSeconds(30), Summary(), [new FailedTest("T.One", "tests/OneTests.cs")], false, new HashSet<string>(["src/Thing.cs"]), 0, new HashSet<string>(), true);

        return change is null ? input : change(input);
    }

    [Fact]
    public void ACompleteSummary_OfAFailedFullTestStep_ThatIsNotTheJobsOwn_MayBeRunOnceMore_WholeWithoutTestFor()
    {
        var decision = FlakyRule.Decide(Input());

        (decision.Mode, decision.Tests.Count).Should().Be((RerunMode.Whole, 1));
        FlakyRule.Decide(Input(input => input with { HasTestFor = true })).Mode.Should().Be(RerunMode.Narrowed);
    }

    public static TheoryData<string, Func<RerunInput, RerunInput>> NeverRows => new()
    {
        { "K9 not a complete summary (a failure the reader cannot name)", input => input with { Summary = Summary(complete: false) } },
        { "K9 no summary line at all", input => input with { Summary = null } },
        { "K10 six failures", input => input with { Summary = Summary(failed: 6, failures: ["A", "B", "C", "D", "E", "F"]) } },
        { "K3 a selected scope", input => input with { Scope = "selected" } },
        { "K3 a build step", input => input with { Step = CheckStep.Build } },
        { "K3 a restore step", input => input with { Step = CheckStep.Restore } },
        { "K3 a typecheck step", input => input with { Step = CheckStep.Typecheck } },
        { "K3 a step that ran over its time", input => input with { TimedOut = true } },
        { "K3 a step that could not start", input => input with { Started = false } },
        { "K3 a step that left a unit", input => input with { LeftUnit = true } },
        { "K4 the failure names no file (the raw tail)", input => input with { Failed = [new FailedTest("T.One", string.Empty)] } },
        { "K4 the test's own file is one the job changed", input => input with { JobChangedFiles = new HashSet<string>(["tests/OneTests.cs"]) } },
        { "K4 the file its failure names is one the job added", input => input with { Failed = [new FailedTest("T.One", "src/Thing.cs")] } },
        { "K5 a test already excused once in this job", input => input with { AlreadyExcused = new HashSet<string>(["T.One"]) } },
        { "K6 a third excused test in one job", input => input with { ExcusedInJob = 2 } },
        { "K8 COAI_FLAKY_RERUNS=0", input => input with { Enabled = false } },
        { "K7 a first run of 301 seconds and no testFor", input => input with { FirstDuration = TimeSpan.FromSeconds(301) } },
        { "no failure is named", input => input with { Summary = Summary(failed: 0, failures: []) } },
    };

    [Theory]
    [MemberData(nameof(NeverRows))]
    public void TheRerunIsNeverAllowed_ForEachRowOfSectionF(string row, Func<RerunInput, RerunInput> change)
    {
        FlakyRule.Decide(Input(change)).Mode.Should().Be(RerunMode.None, row);
    }

    [Fact]
    public void K7_AFirstRunOf301Seconds_MayStillBeNarrowedWhenTheFileNamesTestFor()
    {
        FlakyRule.Decide(Input(input => input with { FirstDuration = TimeSpan.FromSeconds(301), HasTestFor = true })).Mode.Should().Be(RerunMode.Narrowed);
    }

    // ---- the proof ----

    private static RerunOutcome Clean(RunSummary summary, int exit = 0) => new(true, false, false, exit, summary);

    private static RunSummary Rerun(int total = 10, int failed = 0, string[]? failures = null, string[]? passed = null) =>
        new("mtp", true, total, failed, total - failed, failures ?? [], passed ?? [], true);

    [Fact]
    public void K1_ATestIsExcused_WhenItsNameIsExactlyOnceAmongThePassedTestsOfACleanRerun()
    {
        var decision = FlakyRule.Decide(Input(input => input with { HasTestFor = true }));

        FlakyRule.Excused(decision, Summary(), Clean(Rerun(total: 3, passed: ["T.One", "T.Two", "T.Three"]))).Should().Equal(new ExcusedTest("T.One", "tests/OneTests.cs", "named"));
    }

    [Fact]
    public void K1_AWholeRerun_OfAToolThatNamesNoPassedTest_IsProvedByTheTotals()
    {
        var decision = FlakyRule.Decide(Input());

        FlakyRule.Excused(decision, Summary(total: 10), Clean(Rerun(total: 10))).Should().Equal(new ExcusedTest("T.One", "tests/OneTests.cs", "totals"));
    }

    [Fact]
    public void K11_AndK12_ARerunInWhichNoTestRan_ANameThatMatchesTwice_OrATotalThatDiffers_ExcusesNothing()
    {
        var narrowed = FlakyRule.Decide(Input(input => input with { HasTestFor = true }));
        var whole = FlakyRule.Decide(Input());

        FlakyRule.Excused(narrowed, Summary(), Clean(Rerun(total: 0))).Should().BeEmpty("a filter that matched nothing: no test ran");
        FlakyRule.Excused(narrowed, Summary(), Clean(Rerun(total: 3, passed: ["T.One", "T.One"]))).Should().BeEmpty("the name matches twice");
        FlakyRule.Excused(narrowed, Summary(), Clean(Rerun(total: 3, passed: ["A", "B"]))).Should().BeEmpty("the name is not among the passed tests of a narrowed re-run");
        FlakyRule.Excused(whole, Summary(total: 10), Clean(Rerun(total: 9))).Should().BeEmpty("a total that differs");
    }

    [Fact]
    public void K13_AReRunThatItselfDoesNotCompleteCleanly_ExcusesNothing_EvenWhenTheNamedTestPassedInIt()
    {
        var decision = FlakyRule.Decide(Input(input => input with { HasTestFor = true }));
        var passed = Rerun(total: 3, passed: ["T.One", "T.Two", "T.Three"]);

        FlakyRule.Excused(decision, Summary(), Clean(passed, exit: 1)).Should().BeEmpty("the step exits non-zero");
        FlakyRule.Excused(decision, Summary(), Clean(Rerun(total: 3, failed: 1, failures: ["T.Two"], passed: ["T.One", "T.Three"]))).Should().BeEmpty("a new failure");
        FlakyRule.Excused(decision, Summary(), new RerunOutcome(true, true, false, 0, passed)).Should().BeEmpty("a timeout");
        FlakyRule.Excused(decision, Summary(), new RerunOutcome(true, false, true, 0, passed)).Should().BeEmpty("a unit that could not be stopped");
        FlakyRule.Excused(decision, Summary(), new RerunOutcome(false, false, false, -1, null)).Should().BeEmpty("it did not start");
        FlakyRule.Excused(decision, Summary(), Clean(null!)).Should().BeEmpty("no summary: nothing shows it ran");
    }
}
