using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S2 of <c>research/PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md</c>, as a real <c>review_plan</c> / <c>review_code</c>
/// round against FakeCli: a reviewer whose first prompt is over its row's <c>maxPromptKib</c> is never launched, writes no
/// ledger line, is named in the reply, and — when every reviewer is over — the stage refuses before it records a round.
/// </summary>
/// <remarks>
/// A 1 KiB cap is under every reviewer prompt there is (the reviewer instruction alone is several), so it needs no number
/// typed from the prompt's size; <c>ARowIsOnlyGivenAPromptItCanReadTests</c> pins the size and the cap to the byte.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AnOverCapReviewerIsLeftOutOfTheRoundTests : FakeCliRoundTests
{
    /// <summary>Long enough to be taken for the SCOPE of a change as well, which a code round asks for.</summary>
    private const string Plan = """
        # PLAN — a thing

        A reviewer must not be sent more than its row says it can read. When it is done: the round leaves an over-cap
        reviewer out, says so, and the reviewers that fit are asked exactly as before.

        ## Build order

        1. one
        2. two
        """;

    /// <summary>The branch the refusal tests use — the one <see cref="Change"/> commits to, because a code round reviews a COMMITTED change over main.</summary>
    private const string Branch = "epic-1";

    private async Task Change()
    {
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v2\n", TestContext.Current.CancellationToken);
        await Git("add", ".");
        await Git("commit", "-m", "the change");
    }

    private ProviderSettings Codex(int capKib = 0) => new("codex") { ExecutablePath = FakeCliExe, PromptCap = CoaiMcp.Core.Catalog.PromptCap.OfKib(capKib) };

    private ProviderSettings Gemini(int capKib = 0) => new("gemini") { ExecutablePath = FakeCliExe, PromptCap = CoaiMcp.Core.Catalog.PromptCap.OfKib(capKib) };

    private PanelService Rows(params ProviderSettings[] rows) => ServiceFor(Defaults() with { Providers = rows });

    /// <summary>One plan round, and the argv of every launch FakeCli recorded for it.</summary>
    private async Task<(JsonElement Answer, string[][] Launches)> PlanRoundRecording(PanelService service, string branch = "feature")
    {
        var record = Directory.CreateTempSubdirectory("coai-prompt-cap-record-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            await service.OpenAsync(_repo, branch);
            Script(Clean);
            var answer = Parse(await service.ReviewPlanAsync(_repo, branch, Plan, TestContext.Current.CancellationToken));

            return (answer, [.. Directory.GetFiles(record, "*.argv").Select(f => LaunchRecords.Read(f).Split('\0'))]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(record, recursive: true);
        }
    }

    private string[] LedgerProviders() =>
        File.Exists(Path.Combine(_data, "usage.jsonl"))
            ? [.. File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Select(l => JsonDocument.Parse(l).RootElement.GetProperty("provider").GetString()!)]
            : [];

    [Fact]
    public async Task ACappedRowUnderThePrompt_IsNeverLaunched_NamedInTheReply_AndWritesNoLedgerLine()
    {
        var (answer, launches) = await PlanRoundRecording(Rows(Codex(capKib: 1), Gemini()));

        launches.Should().ContainSingle("only the row that can read the prompt was launched");
        launches[0].Should().NotContain("--output-schema", "that is codex's argv shape — the capped row never started");
        var reviewers = answer.GetProperty("reviewers").GetString()!;
        reviewers.Should().Contain("all 1 reviewers answered").And.Contain("codex").And.Contain("maxPromptKib 1").And.Contain("raise the row's cap");
        LedgerProviders().Should().Equal(["gemini"], "nothing was spent on the row that was left out, so no line says it was");
    }

    [Fact]
    public async Task ARowWithNoCap_IsAskedExactlyAsBefore_AndASpaciousCapChangesNothing()
    {
        var (plainAnswer, plain) = await PlanRoundRecording(Rows(Codex(), Gemini()));
        var (roomyAnswer, roomy) = await PlanRoundRecording(Rows(Codex(capKib: 4096), Gemini(capKib: 4096)), "epic-1");

        PromptsOf(roomy).Should().BeEquivalentTo(PromptsOf(plain), "the prompt a reviewer is handed is the one it was always handed");
        plain.Should().HaveCount(2);
        roomyAnswer.GetProperty("reviewers").GetString().Should().Be(plainAnswer.GetProperty("reviewers").GetString());
    }

    /// <summary>The prompt of every launch: the last field of its record, which is what went down stdin.</summary>
    private static string[] PromptsOf(string[][] launches) => [.. launches.Select(argv => argv[^1])];

    [Theory]
    [InlineData("plan")]
    [InlineData("code")]
    public async Task WhenEveryRowIsOverItsCap_TheStageRefusesBeforeItRecordsARound(string stage)
    {
        await Change();
        await PlanPassesUnderNoCap(stage);
        var service = Rows(Codex(capKib: 1), Gemini(capKib: 1));
        await service.OpenAsync(_repo, Branch);
        var recorded = (await RoundsRecorded(service)).Count;
        var launches = Directory.CreateTempSubdirectory("coai-prompt-cap-record-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", launches);
        try
        {
            var answer = Parse(stage == "plan"
                ? await service.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken)
                : await service.ReviewCodeAsync(_repo, Branch, "main", Plan, ct: TestContext.Current.CancellationToken));

            var refusal = answer.GetProperty("error").GetString()!;
            refusal.Should().Contain("could not go to codex").And.Contain("could not go to gemini").And.Contain("maxPromptKib 1")
                .And.Contain("raise the row's cap");
            Directory.GetFiles(launches, "*.argv").Should().BeEmpty("nobody was launched");
            (await RoundsRecorded(service)).Should().HaveCount(recorded, "no round was recorded — nothing ran and nothing was spent");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(launches, recursive: true);
        }
    }

    /// <summary>A code round is only admitted after a plan round passed: run one, with nobody capped, on the same data directory.</summary>
    private async Task PlanPassesUnderNoCap(string stage)
    {
        if (stage != "code")
        {
            return;
        }

        var open = Rows(Codex(), Gemini());
        await open.OpenAsync(_repo, Branch);
        Script(Clean);
        Parse(await open.ReviewPlanAsync(_repo, Branch, Plan, TestContext.Current.CancellationToken))
            .GetProperty("verdict").GetString().Should().Be("proceed", "the fixture: the plan gate is passed, so a code round is admitted");
        await open.ResolveAsync(_repo, Branch, "[]");
    }

    private async Task<IReadOnlyList<JsonElement>> RoundsRecorded(PanelService service) =>
        [.. Parse(await service.StatusAsync(_repo, Branch)).GetProperty("rounds").EnumerateArray()];
}
