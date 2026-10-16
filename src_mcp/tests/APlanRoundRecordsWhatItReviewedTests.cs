using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E1.S6 of <c>todo/PLAN_the_plan_gate_reads_the_committed_plan_E1.md</c>: every completed PLAN round records what it
/// reviewed — where the text came from, the commit, the blob, the bytes and the id of the text sent — in its session file,
/// and <c>status</c> shows it. A round that reviewed no plan, or was written before the field, has none.
/// </summary>
/// <remarks>
/// The field is nullable and never normalised, as <c>Caller</c> is: absent means "not recorded", and an empty record would
/// say "recorded as nothing" — which a save on every progress tick would write over every old round.
/// </remarks>
[Collection("fakecli-env")]
public sealed class APlanRoundRecordsWhatItReviewedTests : FakeCliRoundTests
{
    private const string Branch = "epic-1";
    private const string Plan = "# PLAN — recorded\n\n1. one\n2. two\n";

    private const string Scope = """
        # SCOPE

        The reviewer's own words must survive a failure. A reviewer that falls over is recorded with
        an outcome and nothing else, so the round summary says "unparseable" without the text that
        would not parse, and the same answer replayed by hand goes through cleanly.

        When it is done: the raw answer is kept beside the session, the refusal names the file, and
        a failed reviewer still reports what it consumed. Constraint: no new dependency.
        """;

    private SessionStore Store => new(_data, Defaults().Rounds.Catalog);

    private static PlanInput Committed(string path) => new(string.Empty, path, string.Empty);

    private async Task<string> Rev(string spec)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", ["rev-parse", spec], _repo));
        result.ExitCode.Should().Be(0, result.StdErr);

        return result.StdOut.Trim();
    }

    private async Task CommitPlanAsync(string path, string text)
    {
        var file = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, text);
        await Git("add", path);
        await Git("commit", "-m", $"add {path}");
    }

    private RoundRecord RoundOf(string stage) =>
        Store.Load(_repo, Branch)!.Rounds.Single(r => r.Stage == stage);

    private async Task<PanelService> OpenedAsync()
    {
        var service = ServiceFor(Defaults());
        await service.OpenAsync(_repo, Branch);
        Script(Clean);

        return service;
    }

    [Fact]
    public async Task ACommittedRound_RecordsItsSource_InTheSessionFile_AndStatusShowsIt()
    {
        // T13, the session-file and `status` half.
        await CommitPlanAsync("todo/PLAN_x.md", Plan);
        var sha = await Rev("HEAD");
        var oid = await Rev("HEAD:todo/PLAN_x.md");
        var service = await OpenedAsync();

        var answer = Parse(await service.ReviewPlanAsync(_repo, Branch, Committed("todo/PLAN_x.md"), CadenceArgs.None, TestContext.Current.CancellationToken));

        answer.TryGetProperty("error", out var error).Should().BeFalse($"the round ran, got: {error}");
        var expected = new PlanSource(PlanOrigin.Commit, sha, "todo/PLAN_x.md", oid, Encoding.UTF8.GetByteCount(Plan), DocumentRules.ArtifactIdOf(Plan));
        RoundOf("PlanReview").PlanSource.Should().Be(expected);
        var shown = Parse(await service.StatusAsync(_repo, Branch)).GetProperty("rounds").EnumerateArray().Single().GetProperty("planSource");
        shown.GetProperty("origin").GetString().Should().Be("commit");
        shown.GetProperty("commit").GetString().Should().Be(sha);
        shown.GetProperty("path").GetString().Should().Be("todo/PLAN_x.md");
        shown.GetProperty("blob").GetString().Should().Be(oid);
        shown.GetProperty("bytes").GetInt64().Should().Be(expected.Bytes);
        shown.GetProperty("textId").GetString().Should().Be(expected.TextId);
    }

    [Fact]
    public async Task ACommittedRound_IsAboutItsFile_AndAPastedOneKeepsTodaysTitleRule()
    {
        // The round's subject is the line a person reads in the log. A committed plan HAS a name; guessing a title from its text
        // is what a pasted plan has to do.
        await CommitPlanAsync("todo/PLAN_x.md", Plan);
        var service = await OpenedAsync();
        await service.ReviewPlanAsync(_repo, Branch, Committed("todo/PLAN_x.md"), CadenceArgs.None, TestContext.Current.CancellationToken);
        RoundOf("PlanReview").Subject.Should().Be("PLAN_x.md");

        var pasted = await OpenedPastedAsync("feature");
        await pasted.ReviewPlanAsync(_repo, "feature", Plan);
        Store.Load(_repo, "feature")!.Rounds.Single().Subject.Should().Be("PLAN — recorded");
    }

    private async Task<PanelService> OpenedPastedAsync(string branch)
    {
        var service = ServiceFor(Defaults());
        await service.OpenAsync(_repo, branch);
        Script(Clean);

        return service;
    }

    [Fact]
    public async Task APastedRound_RecordsItsOrigin_AndTheCommitTheBranchWasAt()
    {
        // T14, the record half: a pasted plan is "what was reviewed" too, with the commit the ENGINE resolved.
        var tip = await Rev(Branch);
        var service = await OpenedAsync();

        Parse(await service.ReviewPlanAsync(_repo, Branch, Plan)).TryGetProperty("error", out _).Should().BeFalse();

        RoundOf("PlanReview").PlanSource.Should().Be(new PlanSource(
            PlanOrigin.Text, tip, string.Empty, string.Empty, Encoding.UTF8.GetByteCount(Plan), DocumentRules.ArtifactIdOf(Plan)));
    }

    [Fact]
    public async Task ACodeRound_HasNoPlanSource_AndStatusOmitsTheField()
    {
        var service = await OpenedAsync();
        await service.ReviewPlanAsync(_repo, Branch, Plan);
        await service.ResolveAsync(_repo, Branch, "[]");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v2\n");
        await Git("add", "app.cs");
        await Git("commit", "-m", "the change");
        Script(Clean);

        var code = Parse(await service.ReviewCodeAsync(_repo, Branch, "main", Scope));

        code.TryGetProperty("error", out var error).Should().BeFalse($"the code round ran, got: {error}");
        RoundOf("PlanReview").PlanSource.Should().NotBeNull();
        RoundOf("CodeReview").PlanSource.Should().BeNull("only a plan round reviews a plan");
        var rounds = Parse(await service.StatusAsync(_repo, Branch)).GetProperty("rounds").EnumerateArray().ToList();
        rounds.Single(r => r.GetProperty("stage").GetString() == "PlanReview").TryGetProperty("planSource", out _).Should().BeTrue();
        rounds.Single(r => r.GetProperty("stage").GetString() == "CodeReview").TryGetProperty("planSource", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ASessionFileWithoutTheField_LoadsAndSavesWithoutOne()
    {
        // A round written before the field said nothing; a save must not turn that into "recorded as nothing".
        var service = await OpenedAsync();
        await service.ReviewPlanAsync(_repo, Branch, Plan);
        var file = Directory.GetFiles(_data, "*.json", SearchOption.AllDirectories)
            .Single(f => File.ReadAllText(f).Contains("\"planSource\""));
        var old = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        old["rounds"]!.AsArray().Select(r => r!.AsObject()).ToList().ForEach(r => r.Remove("planSource"));
        await File.WriteAllTextAsync(file, old.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var loaded = Store.Load(_repo, Branch)!;
        Store.Save(loaded);

        loaded.Rounds.Should().OnlyContain(r => r.PlanSource == null, "a missing field loads as null, not as an empty record");
        File.ReadAllText(file).Should().NotContain("planSource", "and a save does not invent one");
    }
}
