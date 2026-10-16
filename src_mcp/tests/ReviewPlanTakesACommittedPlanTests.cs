using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E1.S5 of <c>todo/PLAN_the_plan_gate_reads_the_committed_plan_E1.md</c>: <c>review_plan</c> takes <c>committedPlan</c>
/// (and <c>expectedCommit</c>), the server reads the file at ONE commit, and every refusal is answered before a round is
/// begun — so a refusal leaves the session as it was and launches nobody.
/// </summary>
/// <remarks>Real git and FakeCli: what each reviewer was launched with is the recorded prompt, the one place the text can be seen.</remarks>
[Collection("fakecli-env")]
public sealed class ReviewPlanTakesACommittedPlanTests : FakeCliRoundTests
{
    private const string Branch = "epic-1";

    private static PlanInput Committed(string path, string expected = "") => new(string.Empty, path, expected);

    private PanelService TwoVendors(Func<string, string, Task<string>>? tip = null) =>
        new(
            Defaults() with
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }, new("gemini") { ExecutablePath = FakeCliExe }],
            },
            VaultKeys.None("no vault in tests"), default, _launcher, Logger.None, Noticing.None)
        {
            TipResolver = tip,
        };

    /// <summary>Commits <paramref name="text"/> at <paramref name="path"/> on <paramref name="branch"/>, then returns HEAD to <c>epic-1</c>.</summary>
    private async Task<string> CommitAsync(string branch, string path, string text)
    {
        await Git("checkout", branch);
        var file = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, text);
        await Git("add", path);
        await Git("commit", "-m", $"add {path}");
        var sha = await Rev("HEAD");
        await Git("checkout", Branch);

        return sha;
    }

    private async Task<string> Rev(string spec)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", ["rev-parse", spec], _repo));
        result.ExitCode.Should().Be(0, result.StdErr);

        return result.StdOut.Trim();
    }

    private static string BigPlan(int bytes, string lastLine)
    {
        var text = new StringBuilder("# PLAN — a large committed plan\n\n");
        for (var i = 0; text.Length < bytes; i++)
        {
            text.Append($"{i:D5} the build step that comes after step {i - 1} and keeps this line a plausible length\n");
        }

        return text.Append(lastLine).ToString();
    }

    private async Task<(JsonElement Answer, string[] Prompts)> RoundAsync(PanelService service, string branch, PlanInput input)
    {
        var record = Directory.CreateTempSubdirectory("coai-committed-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            Script(Clean);
            var answer = Parse(await service.ReviewPlanAsync(_repo, branch, input, CadenceArgs.None, TestContext.Current.CancellationToken));
            string[] prompts = [.. Directory.GetFiles(record, "*.argv").Select(f => LaunchRecords.Read(f).Split('\0')[^1])];

            return (answer, prompts);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(record, recursive: true);
        }
    }

    private string RoundsColumn(string column)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_data, Store.RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = $"SELECT {column} FROM rounds WHERE stage = 'PlanReview'";

        return read.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    /// <summary>The refusal must leave nothing behind: no reviewer launched, and the session reads as it did.</summary>
    private async Task AssertRefusedWithNothingSpent(PanelService service, string branch, PlanInput input, params string[] words)
    {
        await service.OpenAsync(_repo, branch);
        var before = await service.StatusAsync(_repo, branch);

        var (answer, prompts) = await RoundAsync(service, branch, input);

        answer.TryGetProperty("error", out var error).Should().BeTrue($"a refusal is an error answer, got: {answer}");
        error.GetString().Should().ContainAll(words);
        prompts.Should().BeEmpty("no reviewer was launched");
        (await service.StatusAsync(_repo, branch)).Should().Be(before, "no round number was taken and nothing was saved");
    }

    [Fact]
    public async Task A100KbCommittedPlan_ReachesEveryReviewerWhole_WithItsSourceLineAndItsEndLine()
    {
        const string last = "the very last line of this plan, unique: Q7-ZEBRA-4417";
        var plan = BigPlan(100_000, last);
        var sha = await CommitAsync(Branch, "todo/PLAN_big.md", plan);
        var oid = await Rev("HEAD:todo/PLAN_big.md");
        var service = TwoVendors();
        await service.OpenAsync(_repo, Branch);

        var (answer, prompts) = await RoundAsync(service, Branch, Committed("todo/PLAN_big.md"));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"the round ran, got: {error}");
        prompts.Should().HaveCount(2);
        var id = DocumentRules.ArtifactIdOf(plan);
        foreach (var prompt in prompts)
        {
            prompt.Should().Contain(last).And.Contain($"END OF PLAN {id}");
            prompt.Should().Contain(plan, "the whole plan, byte for byte, not a pointer to it");
            prompt.Should().Contain(
                $"(read by the server from todo/PLAN_big.md at commit {sha[..12]}, blob {oid[..12]}, {Encoding.UTF8.GetByteCount(plan)} bytes)");
        }
    }

    [Fact]
    public async Task BothInputs_AreRefused_NamingBoth()
    {
        await CommitAsync(Branch, "plan.md", "a plan\n");

        await AssertRefusedWithNothingSpent(TwoVendors(), Branch, new PlanInput("pasted", "plan.md", string.Empty), "planText", "committedPlan", "not both");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NeitherInput_IsRefused_NamingBoth(string committed)
    {
        await AssertRefusedWithNothingSpent(TwoVendors(), Branch, new PlanInput(string.Empty, committed, string.Empty), "planText", "committedPlan");
    }

    [Fact]
    public async Task AWhitespaceCommittedPlan_IsAbsent_SoThePastedPlanIsReviewedAsToday()
    {
        var service = TwoVendors();
        await service.OpenAsync(_repo, Branch);

        var (answer, prompts) = await RoundAsync(service, Branch, new PlanInput("# PLAN — pasted\n\nstep one", "  ", string.Empty));

        answer.TryGetProperty("error", out _).Should().BeFalse();
        prompts.Should().HaveCount(2);
        prompts.Should().OnlyContain(p => p.Contains("step one") && !p.Contains("read by the server"));
    }

    [Fact]
    public async Task ExpectedCommitAlone_IsRefused_BecauseItWouldBeSilentlyIgnored()
    {
        await AssertRefusedWithNothingSpent(
            TwoVendors(), Branch, new PlanInput("pasted", string.Empty, new string('a', 40)), "expectedCommit", "committedPlan");
    }

    [Fact]
    public async Task AMalformedExpectedCommit_IsRefused()
    {
        await CommitAsync(Branch, "plan.md", "a plan\n");

        await AssertRefusedWithNothingSpent(TwoVendors(), Branch, Committed("plan.md", "1a2b3c"), "expectedCommit", "40");
    }

    [Fact]
    public async Task AStaleExpectedCommit_IsRefused_NamingTheTipAndTheExpected()
    {
        var first = await CommitAsync(Branch, "plan.md", "a plan\n");
        var tip = await CommitAsync(Branch, "other.md", "a peer's commit\n");

        await AssertRefusedWithNothingSpent(TwoVendors(), Branch, Committed("plan.md", first), tip[..12], first[..12], "expectedCommit");
    }

    [Fact]
    public async Task ARightExpectedCommit_RunsTheRound()
    {
        var tip = await CommitAsync(Branch, "plan.md", "a plan for the tip\n");
        var service = TwoVendors();
        await service.OpenAsync(_repo, Branch);

        var (answer, prompts) = await RoundAsync(service, Branch, Committed("plan.md", tip.ToUpperInvariant()));

        answer.TryGetProperty("error", out _).Should().BeFalse();
        prompts.Should().HaveCount(2).And.OnlyContain(p => p.Contains("a plan for the tip"));
    }

    [Fact]
    public async Task ThePlanIsReadAtTheCommitTheBranchResolvesTo_AndTheRoundRunsOnThatCommit()
    {
        // T12. The branch moves between the resolve and the engine's own resolve; the plan, the cadence outline and the
        // record must still be ONE commit. The seam answers A (older) while the branch tip is B (newer, other text).
        var a = await CommitAsync(Branch, "plan.md", "the plan as A had it\n");
        var b = await CommitAsync(Branch, "plan.md", "the plan as B has it\n");
        b.Should().NotBe(a);
        var service = TwoVendors((_, _) => Task.FromResult(a));
        await service.OpenAsync(_repo, Branch);

        var (answer, prompts) = await RoundAsync(service, Branch, Committed("plan.md"));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"the round ran, got: {error}");
        prompts.Should().HaveCount(2).And.OnlyContain(p => p.Contains("the plan as A had it") && !p.Contains("as B has it"));
        RoundsColumn("head_sha").Should().Be(a, "the engine resolves the commit the plan was read at, not the branch again");
    }

    [Fact]
    public async Task ACredentialShapedPlan_IsRefusedBeforeAnyRound()
    {
        await CommitAsync(Branch, ".env", "SECRET=1\n");

        await AssertRefusedWithNothingSpent(TwoVendors(), Branch, Committed(".env"), "credential file", ".env*");
    }

    [Fact]
    public async Task AnEditedPlan_IsRefusedBeforeAnyRound()
    {
        await CommitAsync(Branch, "plan.md", "a committed plan\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "plan.md"), "an edit nobody committed\n");

        await AssertRefusedWithNothingSpent(TwoVendors(), Branch, Committed("plan.md"), "uncommitted", "'plan.md'");
    }

    [Fact]
    public async Task APathThatIsNotInTheCommit_IsRefusedNamingIt()
    {
        await AssertRefusedWithNothingSpent(TwoVendors(), Branch, Committed("todo/PLAN_missing.md"), "not in commit", "'todo/PLAN_missing.md'");
    }

    [Fact]
    public async Task ABranchThatDoesNotResolve_IsAnErrorAnswer_NotAToolException()
    {
        // T20: `ResolveShaAsync` THROWS; here it sits outside the engine's try, so it is caught and answered.
        var service = TwoVendors();

        var (answer, prompts) = await RoundAsync(service, "no-such-branch", Committed("plan.md"));

        answer.GetProperty("error").GetString().Should().Contain("no-such-branch");
        prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task AHeadOnAnotherBranch_SaysTheWorkingTreeWasNotCompared_OnTheReviewerLine()
    {
        await CommitAsync("feature", "plan.md", "the plan on feature\n");
        var service = TwoVendors();
        await service.OpenAsync(_repo, "feature");

        var (answer, prompts) = await RoundAsync(service, "feature", Committed("plan.md"));

        answer.TryGetProperty("error", out _).Should().BeFalse();
        prompts.Should().OnlyContain(p => p.Contains("the plan on feature"));
        answer.GetProperty("reviewers").GetString().Should().Contain("not compared").And.Contain("HEAD is");
    }
}
