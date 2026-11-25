using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>featureFolder</c> as the third plan input of <c>review_plan</c> (epoch 3 E3 s3 t2): exactly one input, the branch tip resolved once and the commit of the round, the whole feature validated with the device's limits BEFORE any round,
/// and the whole preparation inside one deadline. Real git and FakeCli reviewers: what was launched is the recorded argv, and "nothing was written" is the session read back and the rounds table counted.
/// </summary>
[Collection("fakecli-env")]
public sealed class AFeatureFolderPlanIsValidatedFirstTests : FakeCliRoundTests
{
    private const string Branch = "epic-1";

    private const string Feature = "plan";

    private static PlanInput Folder(string folder = Feature, string expected = "", string workItem = "") => new(string.Empty, string.Empty, expected) { FeatureFolder = folder, WorkItem = workItem };

    private PanelService Service(Func<string, string, Task<string>>? tip = null, Func<string, string?>? environment = null) =>
        new(
            Defaults() with { Providers = [new("codex") { ExecutablePath = FakeCliExe }, new("gemini") { ExecutablePath = FakeCliExe }] },
            VaultKeys.None("no vault in tests"), default, _launcher, Logger.None, Noticing.None, environment)
        {
            TipResolver = tip,
        };

    private async Task<string> CommitFilesAsync(Dictionary<string, string> files, string message = "the plan")
    {
        await Git("checkout", Branch);
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(_repo, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, text, new UTF8Encoding(false));
        }

        await Git("add", "-A");
        await Git("commit", "-m", message);

        return (await _launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], _repo))).StdOut.Trim();
    }

    private static Dictionary<string, string> Under(Dictionary<string, string> files) => files.ToDictionary(pair => $"{Feature}/{pair.Key}", pair => pair.Value);

    private static Dictionary<string, string> FourEpics()
    {
        var files = WorkPlan.Base();
        files["epoch-1/epoch.yaml"] = "id: epoch-1\ntitle: Foundation\ndependsOn: []\nepics:\n  - e1-core\n  - e2-b\n  - e3-c\n  - e4-d\n";
        foreach (var id in new[] { "e2-b", "e3-c", "e4-d" })
        {
            files[$"epoch-1/{id}/epic.yaml"] = $"id: {id}\ntitle: Epic {id}\nmvp: false\ndependsOn: []\n";
            files[$"epoch-1/{id}/stories/s1-first/story.yaml"] = "id: s1-first\ntitle: First\ndependsOn: []\n";
            files[$"epoch-1/{id}/stories/s1-first/tasks/t1-one/task.yaml"] = WorkPlan.Task("t1-one");
        }

        return files;
    }

    private static Dictionary<string, string> BrokenPlan(int errors)
    {
        var files = WorkPlan.Base();
        for (var i = 1; i <= errors; i++)
        {
            files[$"{WorkPlan.Story}/tasks/t{i + 1}-x{i}/task.yaml"] = WorkPlan.Task($"t{i + 1}-x{i}", dependsOn: "\n  - t99-nobody");
        }

        return files;
    }

    private async Task<(JsonElement Answer, string[] Prompts)> RoundAsync(PanelService service, PlanInput input, string branch = Branch)
    {
        var record = TempDir.For("coai-folder-").Path;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", record);
        try
        {
            Script(Clean);
            var answer = Parse(await service.ReviewPlanAsync(_repo, branch, input, TestContext.Current.CancellationToken));
            string[] prompts = [.. Directory.GetFiles(record, "*.argv").Select(file => LaunchRecords.Read(file).Split('\0')[^1])];

            return (answer, prompts);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(record, recursive: true);
        }
    }

    private long RoundRows()
    {
        var file = Path.Combine(_data, Store.RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return 0;
        }

        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM rounds";

        return (long)read.ExecuteScalar()!;
    }

    private string RoundsColumn(string column)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_data, Store.RoundsDb.FileName)};Pooling=False");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = $"SELECT {column} FROM rounds WHERE stage = 'PlanReview'";

        return read.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    /// <summary>A refusal leaves nothing behind: no reviewer launched, no round row, the session read as it was.</summary>
    private async Task<string> RefusedWithNothingSpentAsync(PanelService service, PlanInput input)
    {
        await service.OpenAsync(_repo, Branch);
        var before = await service.StatusAsync(_repo, Branch);
        var rows = RoundRows();

        var (answer, prompts) = await RoundAsync(service, input);

        answer.TryGetProperty("error", out var error).Should().BeTrue($"a refusal is an error answer, got: {answer}");
        prompts.Should().BeEmpty("no reviewer was launched");
        RoundRows().Should().Be(rows, "no round row was written");
        (await service.StatusAsync(_repo, Branch)).Should().Be(before, "the stage did not move and no round number was taken");

        return error.GetString()!;
    }

    // ---------- P3 ----------

    [Fact]
    public async Task P3_EachWrongPairOfInputs_IsRefusedWithItsOwnSentence_AndTheCorrectOneRuns()
    {
        await CommitFilesAsync(Under(WorkPlan.Base()));
        await CommitFilesAsync(new() { ["plan.md"] = "a plan\n" });
        var service = Service();

        var textAndFolder = await RefusedWithNothingSpentAsync(service, new PlanInput("pasted", string.Empty, string.Empty) { FeatureFolder = Feature });
        var fileAndFolder = await RefusedWithNothingSpentAsync(service, new PlanInput(string.Empty, "plan.md", string.Empty) { FeatureFolder = Feature });
        var textAndFile = await RefusedWithNothingSpentAsync(service, new PlanInput("pasted", "plan.md", string.Empty));
        var all = await RefusedWithNothingSpentAsync(service, new PlanInput("pasted", "plan.md", string.Empty) { FeatureFolder = Feature });

        textAndFolder.Should().ContainAll("planText", "featureFolder", "not both");
        fileAndFolder.Should().ContainAll("committedPlan", "featureFolder", "not both");
        textAndFile.Should().ContainAll("planText", "committedPlan", "not both");
        new[] { textAndFolder, fileAndFolder, textAndFile }.Distinct().Should().HaveCount(3, "each pair has its own sentence");
        all.Should().ContainAll("planText", "committedPlan", "featureFolder");
        (await RefusedWithNothingSpentAsync(service, new PlanInput(string.Empty, string.Empty, string.Empty))).Should().ContainAll("planText", "committedPlan", "featureFolder");
    }

    [Fact]
    public async Task P3_ExpectedCommit_PinsTheCommitTheFolderIsReadAt_AsItDoesForACommittedPlan()
    {
        var first = await CommitFilesAsync(Under(WorkPlan.Base()));
        var service = Service();
        await service.OpenAsync(_repo, Branch);

        (await RefusedWithNothingSpentAsync(service, new PlanInput(string.Empty, string.Empty, first) { FeatureFolder = string.Empty })).Should().ContainAll("expectedCommit", "committedPlan", "featureFolder");

        var tip = await CommitFilesAsync(new() { ["other.md"] = "a peer's commit\n" });
        (await RefusedWithNothingSpentAsync(service, Folder(expected: first))).Should().ContainAll(tip[..12], first[..12], "expectedCommit", $"check what landed (git log {Branch}), then pass expectedCommit = {tip[..12]}");
        (await RefusedWithNothingSpentAsync(service, Folder(expected: "1a2b3c"))).Should().ContainAll("expectedCommit", "40");

        var (answer, prompts) = await RoundAsync(service, Folder(expected: tip.ToUpperInvariant()));
        answer.TryGetProperty("error", out var error).Should().BeFalse($"a right expectedCommit runs the round, got {error}");
        prompts.Should().HaveCount(2);
    }

    [Fact]
    public async Task P3_TheTipIsResolvedOnce_AndIsTheCommitOfTheRound()
    {
        var a = await CommitFilesAsync(Under(WorkPlan.Base()));
        var b = await CommitFilesAsync(Under(new() { ["feature.yaml"] = "id: demo\ntitle: \"A broken one\"\nepochs: []\n" }), "the plan, changed after");
        b.Should().NotBe(a);
        var resolved = 0;
        var service = Service((_, _) =>
        {
            resolved++;

            return Task.FromResult(a);
        });
        await service.OpenAsync(_repo, Branch);

        var (answer, prompts) = await RoundAsync(service, Folder());

        answer.TryGetProperty("error", out var error).Should().BeFalse($"the folder was read at A, which is valid; got: {error}");
        resolved.Should().Be(1, "one resolve for the preparation; the engine takes the commit it is given");
        prompts.Should().HaveCount(2).And.OnlyContain(prompt => prompt.Contains("Demo plan") && !prompt.Contains("A broken one"));
        RoundsColumn("head_sha").Should().Be(a);
    }

    [Theory]
    [InlineData("../plan")]
    [InlineData("/plan")]
    [InlineData("C:/plan")]
    [InlineData("a\0b")]
    public async Task AFeatureFolderThatIsNotARepositoryRelativePath_IsRefusedBeforeAnyGit(string folder)
    {
        (await RefusedWithNothingSpentAsync(Service(), Folder(folder))).Should().Contain("featureFolder");
    }

    [Fact]
    public async Task AFolderThatIsNotInTheCommit_IsRefusedNamingIt()
    {
        await CommitFilesAsync(Under(WorkPlan.Base()));

        (await RefusedWithNothingSpentAsync(Service(), Folder("nowhere"))).Should().ContainAll("nowhere", "not in commit");
    }

    // ---------- P2 and P12 ----------

    [Fact]
    public async Task P2_AnInvalidFolder_IsRefusedWithAtMostTenErrors_TheTotalAndTheSourceOfTheLimits_AndNothingIsLaunchedOrWritten()
    {
        var files = BrokenPlan(12);
        var total = WorkPlan.Validate(files).Errors.Count;
        total.Should().BeGreaterThan(10, "the fixture has more errors than are listed");
        await CommitFilesAsync(Under(files));

        var sentence = await RefusedWithNothingSpentAsync(Service(), Folder());

        sentence.Split('\n').Count(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d+\. ")).Should().Be(10, "at most 10 errors are listed");
        sentence.Should().ContainAll($"{total} errors", "the first 10", "limits source default", "No reviewer was launched");
        sentence.Should().MatchRegex(@"\d+\. [a-z-]+ at epoch-1/e1-core/s1-first[^ ]* — ", "each error carries its code, its place and its message").And.Contain("t99-nobody");
    }

    [Fact]
    public async Task P2_AFolderWithNoFeatureFile_IsRefused()
    {
        await CommitFilesAsync(Under(WorkPlan.Base().Where(pair => pair.Key != "feature.yaml").ToDictionary(pair => pair.Key, pair => pair.Value)));

        (await RefusedWithNothingSpentAsync(Service(), Folder())).Should().ContainAll("feature.yaml", "not a work plan");
    }

    [Fact]
    public async Task P12_ADeviceCapOfThreeEpics_RefusesAFolderOfFour_AndNamesTheDevice_WhileTheDefaultsAcceptIt()
    {
        await CommitFilesAsync(Under(FourEpics()));
        string? Env(string name) => name == "COAI_ROLES_WORK_METHOD" ? """{"maxEpicsPerEpoch":3}""" : null;

        var refused = await RefusedWithNothingSpentAsync(Service(environment: Env), Folder());

        refused.Should().ContainAll("too-many-epics", "device", "4 epics", "3");
        var (answer, prompts) = await RoundAsync(Service(), Folder());
        answer.TryGetProperty("error", out var error).Should().BeFalse($"the shipped limit is 5, got: {error}");
        prompts.Should().HaveCount(2);
    }

    // ---------- the round over a valid folder ----------

    [Fact]
    public async Task AValidFolder_RunsTheRound_OnThePackAndItsSourceLine()
    {
        var sha = await CommitFilesAsync(Under(WorkPlan.Base()));
        var service = Service();
        await service.OpenAsync(_repo, Branch);

        var (answer, prompts) = await RoundAsync(service, Folder());

        answer.TryGetProperty("error", out var error).Should().BeFalse($"the round ran, got: {error}");
        prompts.Should().HaveCount(2);
        foreach (var prompt in prompts)
        {
            prompt.Should().ContainAll("**Not sent**", "## plan/feature.yaml", $"## plan/{WorkPlan.TaskFile}", $"(packed by the server from {Feature} at commit {sha[..12]}, approval ", "validate_work: valid, limits default)");
        }
    }

    // ---------- P11 ----------

    [Fact]
    public async Task P11_APreparationThatGitCannotFinishInTime_IsRefusedBeforeAnyRound()
    {
        var started = DateTime.UtcNow;

        var outcome = await PlanInputs.PrepareAsync(
            Folder(), _repo, Branch, (_, _) => Task.FromResult(new string('a', 40)), new Stalling(), TestContext.Current.CancellationToken, deadline: TimeSpan.FromMilliseconds(400));

        outcome.Should().BeOfType<PlanInputOutcome.Refused>().Which.Sentence.Should().ContainAll("git did not answer in time", "the round was not started");
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(15), "the deadline ended the wait, not the launcher's own budget");
    }

    [Fact]
    public async Task P11_AResolveThatNeverAnswers_IsTheSameRefusal()
    {
        var outcome = await PlanInputs.PrepareAsync(
            Folder(), _repo, Branch, async (_, _) => { await Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken); return string.Empty; }, _launcher, TestContext.Current.CancellationToken,
            deadline: TimeSpan.FromMilliseconds(300));

        outcome.Should().BeOfType<PlanInputOutcome.Refused>().Which.Sentence.Should().Contain("git did not answer in time");
    }

    private sealed class Stalling : IProcessLauncher
    {
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
            }
            catch (OperationCanceledException)
            {
                return new ProcessResult(-1, string.Empty, string.Empty, TimedOut: false, Cancelled: true);
            }

            return new ProcessResult(0, string.Empty, string.Empty, TimedOut: false);
        }
    }
}
