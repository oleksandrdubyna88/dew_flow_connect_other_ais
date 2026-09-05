using Xunit;
using FluentAssertions;
using CoaiBench.Judging;
using CoaiBench.Model;

namespace CoaiBench.Tests;

/// <summary>
/// A judgement stopped half way keeps the half it did.
/// </summary>
/// <remarks>
/// Written after the campaign of 2026-09-05: the Fable judgement was stopped twelve runs in — on
/// purpose, to change the model — and every one of those twelve was lost, because the pass wrote
/// its file once, after the loop. Twelve runs is around a hundred CLI turns. The run is the unit of
/// work, so the run is the unit of saving; and a pass restarted over the same file must not pay for
/// the answers already in it.
/// </remarks>
public sealed class JudgementSurvivesBeingInterruptedTests
{
    private const string Opus = "claude-opus-5";
    private static readonly Case Work = new("split-once", "artifacts/bench/plan-B.md", "7133c2f", "4a27a17");

    private static RunRecord Run(string arm, string judgedBy = "") =>
        new(Work, arm, 1, 1)
        {
            JudgedBy = judgedBy,
            Stages =
            [
                new StageResult("CodeReview", 12.0)
                {
                    Findings = [new Finding(Title: "something", File: "a.cs", Line: 4)],
                },
            ],
        };

    private static Task<RunRecord> Judged(RunRecord run, CancellationToken ct) =>
        Task.FromResult(run with { JudgedBy = Opus });

    [Fact]
    public async Task EveryJudgedRunIsOnDiskBeforeTheNextOneStarts()
    {
        var saves = new List<IReadOnlyList<RunRecord>>();
        var seen = 0;

        var run = () => JudgePass.RunAsync(
            [Run("codex"), Run("gemini"), Run("local")],
            Opus,
            async (r, ct) =>
            {
                // The interruption, made deterministic: the third run never comes back.
                seen++;
                return seen == 3 ? throw new OperationCanceledException() : await Judged(r, ct);
            },
            (runs, _) => { saves.Add([.. runs]); return Task.CompletedTask; },
            (_, _) => { },
            TestContext.Current.CancellationToken);

        await run.Should().ThrowAsync<OperationCanceledException>();

        saves.Should().HaveCount(2, "each finished run is written as soon as it is finished");
        saves[^1].Count(r => r.JudgedBy == Opus).Should().Be(2, "and what was written is what was judged");
    }

    [Fact]
    public async Task ARestartedPassOnlyJudgesWhatThisJudgeHasNotJudged()
    {
        var asked = new List<string>();

        await JudgePass.RunAsync(
            [Run("codex", Opus), Run("gemini"), Run("local", Opus)],
            Opus,
            (r, ct) => { asked.Add(r.Arm); return Judged(r, ct); },
            (_, _) => Task.CompletedTask,
            (_, _) => { },
            TestContext.Current.CancellationToken);

        asked.Should().Equal(["gemini"], "the other two were already judged by this judge");
    }

    [Fact]
    public async Task ADIFFERENTJudgeRejudgesEverything()
    {
        // Half a file in one model's opinion and half in another's is not a measurement. Changing the
        // judge is asking for a new judgement, which is exactly what "switch to Opus" meant.
        var asked = new List<string>();

        await JudgePass.RunAsync(
            [Run("codex", "claude-fable-5-1"), Run("gemini", "claude-fable-5-1")],
            Opus,
            (r, ct) => { asked.Add(r.Arm); return Judged(r, ct); },
            (_, _) => Task.CompletedTask,
            (_, _) => { },
            TestContext.Current.CancellationToken);

        asked.Should().Equal(["codex", "gemini"]);
    }
}
