using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Commands;

namespace CoaiMcp.Tests;

/// <summary>
/// The orders a round hands back, and the switches that produce them.
/// </summary>
/// <remarks>
/// Every switch is off by default, and an empty list is exactly what every release before this one
/// returned — which is the first test here, because a feature that changes behaviour when nobody
/// asked for it is the one that gets reverted.
/// </remarks>
public sealed class GateCommandsTests
{
    private const string SmallPlan = "# PLAN — a nit\n\nOne paragraph. Change `panelView.ts`.\n";

    private static string PlanOf(int lines, int steps, int files, int areas)
    {
        var text = new System.Text.StringBuilder("# PLAN — generated\n\n## Build order\n\n");
        for (var i = 1; i <= steps; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i}. do the {i}th thing\n");
        }
        text.Append("\n## Notes\n\n");
        for (var i = 0; i < files; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"- touches `file{i}.cs`\n");
        }
        foreach (var area in new[] { "src_mcp", "src_vs_code", ".github", "research", "prompts" }.Take(areas))
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"- under `{area}/`\n");
        }
        while (text.ToString().Split('\n').Length < lines)
        {
            text.Append("filler\n");
        }

        return text.ToString();
    }

    [Fact]
    public void WithEverythingOff_ThereAreNoCommands() =>
        GateCommands.For(new CommandContext(PlanText: SmallPlan, PlanStage: true))
            .Should().BeEmpty("a switch nobody set must change nothing at all");

    [Fact]
    public void TheSplitSwitch_AddsExactlyItsOwnCommand()
    {
        var commands = GateCommands.For(new CommandContext(SplitPlan: true, PlanText: SmallPlan, PlanStage: true));

        commands.Should().ContainSingle();
        commands[0].Should().Contain("review_code").And.Contain("commit");
    }

    [Fact]
    public void TheSplitCommand_IsNotIssuedOnACodeRound()
    {
        // A code round has a diff and no plan; a split verdict computed from source would be a
        // number invented. Raised twice in this change's plan round.
        GateCommands.For(new CommandContext(SplitPlan: true, PlanText: "", PlanStage: false))
            .Should().BeEmpty();
    }

    [Fact]
    public void TheAutonomySwitch_SaysWhatToDoWithBothKindsOfQuestion()
    {
        var commands = GateCommands.For(new CommandContext(Autonomous: true, PlanStage: true));

        commands.Should().ContainSingle();
        commands[0].Should().Contain("does not block").And.Contain("END of your final summary");
        commands[0].Should().Contain("interrupted once", "one interruption with everything beats five with one each");
    }

    [Fact]
    public void TheAutonomyCommand_DoesNotTellYouToRereadEpicsThatDoNotExist()
    {
        var alone = GateCommands.For(new CommandContext(Autonomous: true, PlanStage: true))[0];
        var withSplit = GateCommands.For(
            new CommandContext(Autonomous: true, SplitPlan: true, PlanText: SmallPlan, PlanStage: true))[1];

        alone.Should().Contain("re-read the whole plan").And.NotContain("every epic");
        withSplit.Should().Contain("every epic");
    }

    [Fact]
    public void Fable_IsNotNamedWhenTheSwitchIsOff()
    {
        var without = GateCommands.For(new CommandContext(
            SplitPlan: true, SplitWithFable: false, PlanText: SmallPlan, PlanStage: true));

        without.Should().ContainSingle("the split command, and nothing about which model does what");
        string.Join(' ', without).Should().NotContain("Fable");
    }

    [Fact]
    public void TheSwitchAloneIssuesTheFableOrder()
    {
        // There is deliberately no second condition. This once asked whether a Fable REVIEWER was
        // configured, on the reasoning that a command must never name a model this machine has not
        // got — sound reasoning, wrong premise: Fable is not a reviewer, it is a model of the AI
        // that CALLED us. Nobody configures it as a vendor here, so the check was false on every
        // real machine and the switch was inert. Corrected by the operator; the test that asserted
        // the old rule was deleted rather than adjusted, because it encoded the mistake.
        var commands = GateCommands.For(new CommandContext(
            SplitPlan: true, SplitWithFable: true, PlanText: SmallPlan, PlanStage: true));

        commands.Should().HaveCount(2);
        commands[1].Should().Contain("Fable").And.Contain("Opus");
        commands[1].Should().Contain("payments").And.Contain("security");
    }

    /// <summary>The model order as every release up to mcp 0.32.0 wrote it, verbatim.</summary>
    private const string TodaysModelOrder =
        "Do the SPLIT itself with Fable at its highest available version — deciding what the epics "
            + "and stories are is the judgement that shapes everything after it. Then implement: "
            + "ordinary stories on Opus, and anything where being wrong is expensive — payments, "
            + "money, authentication, security, architecture, data migration — on Fable (max) again. "
            + "Name the model you used for each story in your summary.";

    [Fact]
    public void AClaudeCodeCaller_WithNothingConfigured_GetsTodaysOrderByteForByte()
    {
        // Issue #117 made the two models a choice per caller kind. The shipped choice for Claude
        // Code is the pair every release before it named, and a person who changed nothing must not
        // be able to tell the feature landed — so this is equality, not containment.
        var commands = GateCommands.For(new CommandContext(
            SplitPlan: true, SplitWithFable: true, PlanText: SmallPlan, PlanStage: true));

        commands[1].Should().Be(TodaysModelOrder);
    }

    private static string ModelOrderFor(ModelPair models) =>
        GateCommands.For(new CommandContext(
            SplitPlan: true, SplitWithFable: true, PlanText: SmallPlan, PlanStage: true)
        { Models = models })[1];

    [Fact]
    public void ACodexCaller_IsToldItsOwnPair_AndNoClaudeModel()
    {
        // The command is carried out by the CALLER. A Codex session told "use Fable and Opus" has
        // neither, so it was being handed an order it could not follow (issue #117).
        var order = ModelOrderFor(new ModelPair("gpt-6-astra", "gpt-6-luna"));

        order.Should().Contain("with gpt-6-astra at its highest available version")
            .And.Contain("ordinary stories on gpt-6-luna")
            .And.Contain("on gpt-6-astra (max) again");
        order.Should().NotContain("Fable").And.NotContain("Opus");
    }

    [Fact]
    public void ACallerWithNothingNamed_IsToldTheGenericWords_NeverAnotherVendorsModel()
    {
        var order = ModelOrderFor(new ModelPair(string.Empty, string.Empty));

        order.Should().Contain("with the strongest model your client offers at its highest available version")
            .And.Contain("ordinary stories on your usual model");
        order.Should().NotContain("Fable").And.NotContain("Opus");
    }

    [Theory]
    [InlineData("gpt-6-astra", "", "with gpt-6-astra at", "stories on your usual model")]
    [InlineData("", "gpt-6-luna", "with the strongest model your client offers at", "stories on gpt-6-luna")]
    public void APairWithOneNameSet_UsesItForThatSlotAndTheGenericWordsForTheOther(
        string strongest, string implementation, string splitWords, string implementWords)
    {
        // Never half-blank: an empty slot is not "with  at its highest available version".
        ModelOrderFor(new ModelPair(strongest, implementation))
            .Should().Contain(splitWords).And.Contain(implementWords).And.NotContain("  ");
    }

    [Fact]
    public void TheWordsEverySplitOrderCarries_AreTheOnesTheSharedFileHolds()
    {
        using var document = System.Text.Json.JsonDocument.Parse(SharedFixtures.Text("command-models.json"));

        GateCommands.GateOrderMarker.Should().Be(document.RootElement.GetProperty("splitOrderCarries").GetString());
    }

    [Fact]
    public void TheWordsTheBenchReads_AreTheOnesTheSharedFileHolds()
    {
        using var document = System.Text.Json.JsonDocument.Parse(SharedFixtures.Text("command-models.json"));

        GateCommands.ModelOrderMarker.Should().Be(document.RootElement.GetProperty("orderOpensWith").GetString());
    }

    [Theory]
    [InlineData("Fable", "Opus")]
    [InlineData("gpt-6-astra", "gpt-6-luna")]
    [InlineData("", "")]
    public void EveryVariant_OpensWithTheWordsTheBenchReads(string strongest, string implementation)
    {
        // The bench recognises the order by these words, never by a model name — a name is now a
        // setting, and the old check ("contains Fable") would call every Codex round unapplied.
        ModelOrderFor(new ModelPair(strongest, implementation))
            .Should().StartWith(GateCommands.ModelOrderMarker);
    }

    [Fact]
    public void WithEverythingOn_TheOrderIsSplitThenModelThenAutonomy()
    {
        // The order is the order they are carried out in, and it is asserted whole because three
        // per-switch tests can all pass while the sequence is wrong.
        var commands = GateCommands.For(new CommandContext(
            Autonomous: true, SplitPlan: true, SplitWithFable: true,
            PlanText: PlanOf(lines: 150, steps: 5, files: 3, areas: 2), PlanStage: true));

        commands.Should().HaveCount(3);
        commands[0].Should().Contain("STORIES");
        commands[1].Should().Contain("Fable");
        commands[2].Should().Contain("AUTONOMOUSLY");
    }

    [Theory]
    // Issue #131: five sizes, from build steps and length only, each at both of its edges. The
    // thresholds come from the 187 PLAN_*.md of this repository on 2026-09-23, measured with this
    // reader: 8 % / 68 % / 14 % / 5 % / 2 %.
    [InlineData(120, 2, PlanShape.Split.AsItIs)]
    [InlineData(121, 2, PlanShape.Split.Small)]
    [InlineData(100, 3, PlanShape.Split.Small)]
    [InlineData(350, 6, PlanShape.Split.Small)]
    [InlineData(351, 6, PlanShape.Split.Medium)]
    [InlineData(100, 7, PlanShape.Split.Medium)]
    [InlineData(600, 9, PlanShape.Split.Medium)]
    [InlineData(601, 9, PlanShape.Split.Large)]
    [InlineData(100, 10, PlanShape.Split.Large)]
    [InlineData(900, 12, PlanShape.Split.Large)]
    [InlineData(901, 12, PlanShape.Split.Huge)]
    [InlineData(100, 13, PlanShape.Split.Huge)]
    public void TheSize_IsTheLargerOfWhatTheStepsAndTheLengthSay(int lines, int steps, PlanShape.Split expected) =>
        new PlanShape(lines, steps, Files: 3, Areas: 2).Verdict.Should().Be(expected);

    [Fact]
    public void ManyFilesAndAreas_DoNotMakeASmallPlanIntoEpics()
    {
        // The regression #131 is about: the median plan here names 15 files and nine in ten "touch"
        // four areas, so `Files >= 14` alone sent 106 of 187 plans to epics. Files and areas are
        // still reported; they no longer decide.
        new PlanShape(Lines: 150, Steps: 3, Files: 40, Areas: 9).Verdict.Should().Be(PlanShape.Split.Small);
    }

    [Fact]
    public void ThePlanItselfIsMeasured_NotGuessedAt()
    {
        var shape = PlanShapeReader.Of(PlanOf(lines: 320, steps: 7, files: 6, areas: 4));

        shape.Steps.Should().Be(7);
        shape.Files.Should().Be(6);
        shape.Areas.Should().BeGreaterThanOrEqualTo(4);
        shape.Lines.Should().BeGreaterThanOrEqualTo(320);
        shape.Verdict.Should().Be(PlanShape.Split.Medium);
    }

    [Fact]
    public void APlanWithNoBuildOrder_IsStillSizedByItsLength()
    {
        // PLAN_every_message_is_written_down is 1224 lines with no recognised build order and was
        // built as several epics; length alone has to be able to say so.
        var text = "# PLAN\n\n" + string.Join("\n", Enumerable.Range(0, 1000).Select(i => $"line {i} `f{i % 20}.cs`"));

        var shape = PlanShapeReader.Of(text);

        shape.Steps.Should().Be(0);
        shape.Verdict.Should().Be(PlanShape.Split.Huge, "long, whatever shape it is written in");
    }

    private static string PlanWithEpicHeadings(int epics, int steps = 0, int lines = 0)
    {
        var text = new System.Text.StringBuilder("# PLAN — generated\n\n## Build order\n\n");
        for (var i = 1; i <= steps; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i}. do the {i}th thing\n");
        }
        for (var epic = 1; epic <= epics; epic++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\n### Epic {epic} — piece {epic}\n\n#### Story {epic}.1 — one\n");
        }
        while (text.ToString().Split('\n').Length < lines)
        {
            text.Append("filler\n");
        }

        return text.ToString();
    }

    [Theory]
    // todo/PLAN_consult_on_a_cadence.md, D8: a plan that names its epics has answered the question.
    [InlineData(1, PlanShape.Split.Small)]
    [InlineData(2, PlanShape.Split.Medium)]
    [InlineData(3, PlanShape.Split.Medium)]
    [InlineData(4, PlanShape.Split.Large)]
    [InlineData(5, PlanShape.Split.Huge)]
    [InlineData(6, PlanShape.Split.Massive)]
    [InlineData(14, PlanShape.Split.Massive)]
    public void APlanThatNamesItsEpics_IsSizedByThem(int epics, PlanShape.Split expected) =>
        PlanShapeReader.Of(PlanWithEpicHeadings(epics)).Verdict.Should().Be(expected);

    [Fact]
    public void APlanWithTenEpicHeadings_IsMassive_WhateverItsSteps()
    {
        // email-service's PLAN_first_application_live: 10 epics, 50 stories, 5 numbered steps.
        var shape = PlanShapeReader.Of(PlanWithEpicHeadings(epics: 10, steps: 5));

        shape.Steps.Should().Be(5);
        shape.Epics.Should().Be(10);
        shape.Verdict.Should().Be(PlanShape.Split.Massive);
    }

    [Fact]
    public void APlanWithFourEpicHeadingsAndThirtyOneSteps_IsLarge()
    {
        // email-service's PLAN_stage_foundation: 4 epics, 31 numbered steps, 1452 lines — the longer
        // file is the smaller plan, and only its headings say so.
        PlanShapeReader.Of(PlanWithEpicHeadings(epics: 4, steps: 31, lines: 1452)).Verdict
            .Should().Be(PlanShape.Split.Large);
    }

    [Fact]
    public void StoryHeadingsWithoutEpics_CountAsSteps() =>
        PlanShapeReader.Of("# PLAN\n\n" + string.Concat(Enumerable.Range(1, 10).Select(i => $"### Story {i} — s\n\ntext\n\n")))
            .Verdict.Should().Be(PlanShape.Split.Large, "ten stories is ten steps, the #131 table");

    [Fact]
    public void ANumbersLineWithoutEpicHeadings_IsWhatItAlwaysWas() =>
        new PlanShape(100, 3, 2, 1).Numbers.Should().Be("100 lines, 3 build step(s), 2 file(s) named, 1 area(s) touched");

    [Fact]
    public void ANumbersLineWithStoryHeadingsThatDecidedTheSize_SaysHowMany()
    {
        // CodeRabbit on #540: ten story headings and no numbered steps size a plan Large, and the order must
        // show the number that did it — the numbers are there so the AI can argue with them.
        var shape = PlanShapeReader.Of("# PLAN\n\n" + string.Concat(Enumerable.Range(1, 10).Select(i => $"### Story {i} — s\n\ntext\n\n")));

        shape.Verdict.Should().Be(PlanShape.Split.Large);
        shape.Numbers.Should().Contain("10 story heading(s)");
    }

    [Fact]
    public void ANumbersLineWithEpicHeadings_SaysHowMany() =>
        PlanShapeReader.Of(PlanWithEpicHeadings(epics: 7)).Numbers.Should().Contain("7 epic heading(s)");

    [Fact]
    public void TheMassiveOrder_SaysSplitThePlanPastFourteen()
    {
        var order = GateCommands.For(new CommandContext(SplitPlan: true, PlanText: PlanWithEpicHeadings(9), PlanStage: true))[0];

        order.Should().StartWith("Split this plan into 6-14 EPICS");
        order.Should().Contain("never more than 14").And.Contain("two plans");
        order.Should().Contain("per EPIC", "a Massive plan has epics, so it is gated per epic");
    }

    [Theory]
    [InlineData(200, 4, "3-5 logically complete STORIES")]
    [InlineData(400, 8, "2-3 EPICS, each of 2-3 logically complete STORIES")]
    [InlineData(700, 11, "3-4 EPICS, each of 3-4 logically complete STORIES")]
    [InlineData(1000, 14, "4-5 EPICS, each of 3-5 logically complete STORIES")]
    public void EachSize_OrdersTheOwnersNumbers_AndNoMore(int lines, int steps, string numbers)
    {
        var order = GateCommands.For(new CommandContext(
            SplitPlan: true, PlanText: PlanOf(lines, steps, files: 3, areas: 2), PlanStage: true))[0];

        order.Should().StartWith("Split this plan into ").And.Contain(numbers);
        order.Should().Contain("Fewer is fine when the work is smaller; never more");
    }

    [Theory]
    [InlineData(GateScope.Epic)]
    [InlineData(GateScope.Task)]
    public void NoOrderEverGatesEachStory(GateScope scope)
    {
        // Issue #131, symptom 2: "After EVERY story: call review_code" cost a branch, a plan round
        // and a code round per story, because a gate session ends at its code round.
        foreach (var (lines, steps) in new[] { (100, 1), (200, 4), (400, 8), (700, 11), (1000, 14) })
        {
            foreach (var first in new[] { true, false })
            {
                var orders = GateCommands.For(new CommandContext(
                    SplitPlan: true, PlanText: PlanOf(lines, steps, 3, 2), PlanStage: true, FirstPlanRound: first)
                { GatePer = scope });

                string.Join(' ', orders).Should().NotContain("After EVERY story").And.NotContain("every story:");
            }
        }
    }

    [Fact]
    public void OneGatePerEpic_StacksTheEpics_AndCommitsEachAsOne()
    {
        var order = GateCommands.For(new CommandContext(
            SplitPlan: true, PlanText: PlanOf(400, 8, 3, 2), PlanStage: true))[0];

        order.Should().Contain(GateCommands.GateOrderMarker + " per EPIC")
            .And.Contain("starting from the previous epic's commit")
            .And.Contain("previous epic's commit as baseRef")
            .And.Contain("fold the fixes into the epic's ONE commit");
    }

    /// <summary>
    /// Every gate order COMMITS before it calls review_code — never the other way round.
    /// </summary>
    /// <remarks>
    /// The epic and the default orders used to say "then ONE review_code … and commit", review
    /// first. review_code reviews committed changes only, so an agent obeying them sent an EMPTY
    /// diff: the epic's branch starts at the previous epic's commit, which is also its baseRef. That
    /// used to pass as a clean `proceed` and end the session; it is refused now, and an order that
    /// walks every obedient caller into a refusal is the defect this pins
    /// (research/PLAN_a_failed_round_can_be_retried.md, S1).
    /// </remarks>
    [Fact]
    public void EveryGateOrder_CommitsBeforeItCallsReviewCode()
    {
        var orders = new (string Name, string Order, string Commit)[]
        {
            ("per epic", GateCommands.For(new CommandContext(
                SplitPlan: true, PlanText: PlanOf(400, 8, 3, 2), PlanStage: true))[0], "COMMIT the epic"),
            ("for the task", GateCommands.For(new CommandContext(
                SplitPlan: true, PlanText: PlanOf(400, 8, 3, 2), PlanStage: true) { GatePer = GateScope.Task })[0],
                "commit each epic"),
            ("stories only", GateCommands.For(new CommandContext(
                SplitPlan: true, PlanText: PlanOf(200, 4, 3, 2), PlanStage: true))[0], "COMMIT it"),
        };

        foreach (var (name, order, commit) in orders)
        {
            var committed = order.IndexOf(commit, StringComparison.Ordinal);
            var reviewed = order.IndexOf("review_code over", StringComparison.Ordinal);

            committed.Should().BeGreaterThan(-1, $"the {name} order must say to commit ({order})");
            committed.Should().BeLessThan(reviewed, $"the {name} order must commit BEFORE review_code ({order})");
        }
    }

    /// <summary>
    /// Every gate order says where the SECOND code round is. "ONE review_code" was read as "one, ever",
    /// and an agent that crashed mid-epic refused its operator's checkpoint round to keep the final one
    /// (issue #490).
    /// </summary>
    [Fact]
    public void EveryGateOrder_NamesTheDoorToAnotherCodeRound()
    {
        var contexts = new[]
        {
            new CommandContext(SplitPlan: true, PlanText: PlanOf(400, 8, 3, 2), PlanStage: true),
            new CommandContext(SplitPlan: true, PlanText: PlanOf(400, 8, 3, 2), PlanStage: true) { GatePer = GateScope.Task },
            new CommandContext(SplitPlan: true, PlanText: PlanOf(200, 4, 3, 2), PlanStage: true),
        };

        foreach (var context in contexts)
        {
            GateCommands.For(context)[0].Should().Contain("review_code with again: true");
        }
    }

    [Fact]
    public void OneGateForTheTask_IsOneCodeRoundAtTheEnd_AndStillACommitPerEpic()
    {
        var order = GateCommands.For(new CommandContext(
            SplitPlan: true, PlanText: PlanOf(400, 8, 3, 2), PlanStage: true)
        { GatePer = GateScope.Task })[0];

        order.Should().Contain(GateCommands.GateOrderMarker + " for the WHOLE task")
            .And.Contain("ONE review_code over the whole task's diff")
            .And.Contain("commit each epic as ONE commit");
    }

    [Fact]
    public void AStoriesOnlyPlan_IsOneUnitOfReview()
    {
        var order = GateCommands.For(new CommandContext(
            SplitPlan: true, PlanText: PlanOf(200, 4, 3, 2), PlanStage: true))[0];

        order.Should().Contain(GateCommands.GateOrderMarker).And.Contain("review_code over the whole diff")
            .And.Contain("fold the fixes into that ONE commit");
        order.Should().NotContain("EPICS").And.NotContain("per EPIC");
    }

    [Fact]
    public void APieceComingBack_UnderOneGateForTheTask_IsNotGatedOnItsOwn()
    {
        var again = GateCommands.For(new CommandContext(
            SplitPlan: true, PlanText: SmallPlan, PlanStage: true, FirstPlanRound: false)
        { GatePer = GateScope.Task })[0];

        again.Should().Contain("do NOT split it again").And.Contain("not gated on its own").And.Contain("ONE commit");
    }

    [Fact]
    public void FilesAreCountedByPath_NotByName()
    {
        // `src/a.cs` and `tests/a.cs` are two files. Collapsing them by base name made a plan that
        // names fourteen look like one naming seven — and fourteen is the threshold the epics
        // verdict turns on. (codex, this change's code round.)
        var text = "# PLAN\n\n" + string.Join(
            "\n",
            Enumerable.Range(0, 7).SelectMany(i => new[] { $"- `src/a{i}.cs`", $"- `tests/a{i}.cs`" }));

        PlanShapeReader.Of(text).Files.Should().Be(14);
    }

    [Fact]
    public void ABuildOrderWithPhasesUnderIt_CountsEveryStepInEveryPhase()
    {
        // Issue #131 made the steps decide the size, and the section used to end at ANY heading — so a
        // "### Phase 1" under "## Build order" cut it at zero steps and a plan built in phases read as
        // small. The section ends at a heading of its own level or higher. (#131's code review.)
        var text = "# PLAN\n\n## Build order\n\n### Phase 1\n\n1. a\n2. b\n3. c\n\n### Phase 2\n\n4. d\n5. e\n\n"
            + "## Test plan\n\n1. x\n2. y\n";

        PlanShapeReader.Of(text).Steps.Should().Be(5, "both phases are the build order, and the test plan is not");
    }

    [Fact]
    public void AChecklistIsNotABuildOrder()
    {
        // With no build-order heading only the longest CONTIGUOUS run counts: four acceptance
        // criteria in one place and a numbered example in another are not six build steps.
        var text = "# PLAN\n\n## What must be true\n\n1. a\n2. b\n3. c\n4. d\n\n"
            + "Prose that breaks the run.\n\n## An example\n\n1. x\n2. y\n";

        PlanShapeReader.Of(text).Steps.Should().Be(4, "the longest run, not the sum of every list");
    }

    [Fact]
    public void TheCommandsSayTheyMustBeFollowed() =>
        GateCommands.Preamble.Should().Contain("outrank").And.Contain("operator");

    // ---------- the loop, and its floor ----------

    [Fact]
    public void AnEpicComingBack_IsToldItIsAPiece_NotToSplitAgain()
    {
        // The operator's question, and the whole reason FirstPlanRound exists: a plan is split into
        // epics, each epic comes back for its own plan review, and a gate with no memory tells each
        // one to split into epics. Epics of epics, for ever.
        var again = GateCommands.For(new CommandContext(
            SplitPlan: true, PlanText: PlanOf(lines: 554, steps: 7, files: 8, areas: 2),
            PlanStage: true, FirstPlanRound: false));

        again.Should().ContainSingle();
        again[0].Should().Contain("do NOT split it again").And.Contain("as one unit");
        again[0].Should().NotContain("EPICS").And.NotContain("STORIES");
    }

    [Fact]
    public void TheAlreadySplitOrder_StillSaysWhatToDoWithEachPiece()
    {
        // "Do not split" must not read as "do not review": the per-unit loop is the valuable half
        // of the order and it applies to a piece exactly as it applies to a story.
        var again = GateCommands.For(new CommandContext(
            SplitPlan: true, PlanText: SmallPlan, PlanStage: true, FirstPlanRound: false))[0];

        again.Should().Contain("review its diff").And.Contain("commit");
        again.Should().Contain("say so in your summary", "an oversized piece is reported, not silently built");
    }

    [Fact]
    public void AnEpicComingBack_IsNeverToldToSplitItWithFable()
    {
        // The Fable order is about the SPLIT — "do the splitting with Fable". Handed to a piece
        // that must not be split, it is an instruction with nothing to apply to.
        var again = GateCommands.For(new CommandContext(
            SplitPlan: true, SplitWithFable: true,
            PlanText: SmallPlan, PlanStage: true, FirstPlanRound: false));

        again.Should().ContainSingle();
        string.Join(' ', again).Should().NotContain("Fable");
    }

    [Fact]
    public void TheAutonomyOrder_SurvivesTheEpic()
    {
        // Autonomy is not part of the split and has no reason to stop when the split does — a
        // second interruption policy halfway through a task is the opposite of what was asked for.
        var again = GateCommands.For(new CommandContext(
            Autonomous: true, SplitPlan: true, PlanText: SmallPlan, PlanStage: true, FirstPlanRound: false));

        again.Should().HaveCount(2);
        again[1].Should().Contain("AUTONOMOUSLY");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithTheSplitSwitchOff_NeitherOrderIsGiven(bool firstRound)
    {
        // The operator's first question: with the box unticked, nothing goes — not the split order,
        // and not the "you are a piece" order either, which would be a command about a feature
        // nobody switched on.
        GateCommands.For(new CommandContext(
            SplitPlan: false, SplitWithFable: true,
            PlanText: SmallPlan, PlanStage: true, FirstPlanRound: firstRound))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, true, true, true)]     // the one case that orders a split
    [InlineData(true, true, false, false)]   // an epic coming back
    [InlineData(true, false, true, false)]   // a code round
    [InlineData(false, true, true, false)]   // the switch is off
    public void OrdersSplit_AnswersExactlyWhatForActuallyDid(
        bool splitPlan, bool planStage, bool firstRound, bool expected)
    {
        // The server records the order it gave, and it must ask ONE question to know it gave one.
        // Two copies of the same condition is how the surface-name check ended up with three.
        var context = new CommandContext(
            SplitPlan: splitPlan, PlanText: SmallPlan, PlanStage: planStage, FirstPlanRound: firstRound);

        GateCommands.OrdersSplit(context).Should().Be(expected);
        // The gate ending rather than "Split this plan": a plan small enough to build as it stands
        // is still a split ORDER — it says so, and the gate ending is the half that always applies.
        // (It was "After EVERY story" until issue #131.)
        GateCommands.For(context).Any(c => c.Contains(GateCommands.GateOrderMarker, StringComparison.Ordinal))
            .Should().Be(expected, "what it says and what it reports must be the same event");
    }
}
