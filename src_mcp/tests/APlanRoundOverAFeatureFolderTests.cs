using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>review_plan(featureFolder)</c> over a REAL <c>coai-mcp</c> on real stdio with a FakeCli reviewer (epoch 3 E3 s3 t5): a valid folder launches the reviewer with the pack and its source line; an invalid one is refused with no launch, no round row, no
/// stage change and no worktree (P2); the working tree guard names an edited, a staged or an untracked plan file when HEAD is the commit, and says it did not compare when HEAD is another (P4). No model is called.
/// </summary>
[Collection("fakecli-env")]
public sealed class APlanRoundOverAFeatureFolderTests : IDisposable
{
    private const string Clean = """{"findings": []}""";

    private const string Branch = "feature";

    private const string Task = $"plan/{WorkPlan.TaskFile}";

    private readonly TempDir _data = TempDir.For("coai-folder-stdio-data-");
    private readonly TempDir _home = TempDir.For("coai-folder-stdio-home-");
    private readonly TempDir _record = TempDir.For("coai-folder-stdio-record-");
    private readonly IDisposable _steering;

    public APlanRoundOverAFeatureFolderTests()
    {
        _steering = FakeCliSteering.Write(new Dictionary<string, string>
        {
            ["FAKECLI_MODE"] = "vendor",
            ["FAKECLI_STDOUT"] = Clean,
            ["FAKECLI_OUTFILE_TEXT"] = Clean,
            ["FAKECLI_EXIT"] = "0",
            ["FAKECLI_RECORD_DIR"] = _record.Path,
        });
    }

    public void Dispose()
    {
        _steering.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private static void Lay(TempGitRepo repo, Dictionary<string, string> files)
    {
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(repo.Path, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text, new UTF8Encoding(false));
        }
    }

    /// <summary>A repository on branch <c>feature</c> that holds the plan folder <c>plan</c> (a valid one by default), committed.</summary>
    private static async Task<TempGitRepo> RepoAsync(Dictionary<string, string>? plan = null)
    {
        var repo = await TempGitRepo.InitAsync(new ProcessLauncher(), "coai-folder-stdio-repo-");
        await repo.GitAsync("config", "core.autocrlf", "false");
        Lay(repo, new() { ["app.cs"] = "v1\n" });
        await repo.CommitAsync("base");
        await repo.GitAsync("checkout", "-b", Branch);
        var files = plan ?? WorkPlan.Base();
        files["feature.yaml"] = files["feature.yaml"] + "plan: PLAN.md\n";
        files["PLAN.md"] = "# The plan\n\nWhat the feature is, in a few words.\n";
        Lay(repo, files.ToDictionary(pair => $"plan/{pair.Key}", pair => pair.Value));
        await repo.CommitAsync("the plan folder");

        return repo;
    }

    private System.Diagnostics.Process Server() => StdioServer.Start(
        _data.Path,
        logLevel: "warning",
        alsoInTheEnvironment:
        [
            ("HOME", _home.Path),
            ("COAI_VENDORS", JsonSerializer.Serialize(new[] { new Dictionary<string, string> { ["id"] = "codex", ["runtime"] = "codex", ["executablePath"] = FakeCliExe } })),
            ("FAKECLI_MODE", "vendor"),
            ("FAKECLI_STDOUT", Clean),
            ("FAKECLI_OUTFILE_TEXT", Clean),
            ("FAKECLI_EXIT", "0"),
            ("FAKECLI_RECORD_DIR", _record.Path),
        ]);

    private static async Task<JsonElement> OpenAsync(System.Diagnostics.Process server, TempGitRepo repo, string branch = Branch)
    {
        await StdioServer.InitializeAsync(server);

        return await StdioServer.Call(server, 2, "open", $$"""{"repoPath":{{JsonSerializer.Serialize(repo.Path)}},"branch":"{{branch}}"}""");
    }

    private static Task<JsonElement> ReviewAsync(System.Diagnostics.Process server, TempGitRepo repo, string branch = Branch, int id = 3) =>
        StdioServer.Call(server, id, "review_plan", $$"""{"repoPath":{{JsonSerializer.Serialize(repo.Path)}},"branch":"{{branch}}","featureFolder":"plan"}""", timeoutSeconds: 120);

    private string[] Prompts() => [.. Directory.GetFiles(_record.Path, "*.argv").Select(file => LaunchRecords.Read(file).Split('\0')[^1])];

    private long RoundRows()
    {
        var file = Path.Combine(_data.Path, Store.RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return 0;
        }

        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM rounds";

        return (long)read.ExecuteScalar()!;
    }

    private bool AnyWorktree() => Directory.Exists(Path.Combine(_data.Path, "worktrees")) && Directory.EnumerateFileSystemEntries(Path.Combine(_data.Path, "worktrees")).Any();

    // ---------- d1 ----------

    [Fact]
    public async Task D1_TheToolNamesFeatureFolderAndTheScopeByWorkItem_AndTakesNothingNewAsRequired()
    {
        await using var repo = await RepoAsync();
        var server = Server();
        try
        {
            await StdioServer.InitializeAsync(server);
            using var list = await StdioServer.RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var tool = list.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Single(one => one.GetProperty("name").GetString() == "review_plan");

            var schema = tool.GetProperty("inputSchema");
            schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Should().Contain(["planText", "committedPlan", "featureFolder", "expectedCommit", "workItem"]);
            schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).Should().BeEquivalentTo(["repoPath", "branch"], "featureFolder is optional like the other two inputs: a caller that sends the old arguments is unchanged");
            tool.GetProperty("description").GetString().Should().ContainAll(
                "featureFolder", "workItem", "scope", "validates", "topological order", "approval", "Not sent", "coder");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task D1_AValidFolder_LaunchesTheReviewerWithThePackAndItsSourceLine()
    {
        await using var repo = await RepoAsync();
        var sha = await repo.HeadAsync();
        var server = Server();
        try
        {
            await OpenAsync(server, repo);

            var answer = await ReviewAsync(server, repo);

            answer.TryGetProperty("error", out var error).Should().BeFalse($"the round ran, got: {error}");
            answer.GetProperty("verdict").GetString().Should().Be("proceed");
            var prompts = Prompts();
            prompts.Should().ContainSingle("one reviewer row, one launch");
            prompts[0].Should().MatchRegex($@"\(packed by the server from plan at commit {sha[..12]}, approval [0-9a-f]{{64}}, \d+ files, \d+ bytes; validate_work: valid, limits default\)");
            prompts[0].Should().ContainAll("**Not sent**", "## plan/feature.yaml", "## plan/PLAN.md", "# The plan", $"## {Task}", "BEGIN OF PLAN", "END OF PLAN");
            prompts[0].IndexOf("## plan/feature.yaml", StringComparison.Ordinal).Should().BeLessThan(prompts[0].IndexOf($"## {Task}", StringComparison.Ordinal), "the pack is in order");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task D1_TheSameCommit_GivesTheSamePackBytes_InTwoRounds()
    {
        await using var repo = await RepoAsync();
        await repo.GitAsync("branch", "copy");
        var server = Server();
        try
        {
            await OpenAsync(server, repo);
            await StdioServer.Call(server, 4, "open", $$"""{"repoPath":{{JsonSerializer.Serialize(repo.Path)}},"branch":"copy"}""");
            (await ReviewAsync(server, repo)).TryGetProperty("error", out _).Should().BeFalse();
            (await ReviewAsync(server, repo, "copy", 5)).TryGetProperty("error", out _).Should().BeFalse();

            var prompts = Prompts();
            prompts.Should().HaveCount(2);
            Pack(prompts[0]).Should().Be(Pack(prompts[1]));
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    /// <summary>From the first section of the pack to the end marker — what the reviewers read of the plan.</summary>
    private static string Pack(string prompt)
    {
        var from = prompt.IndexOf("**Not sent**", StringComparison.Ordinal);

        return prompt[from..prompt.IndexOf("END OF PLAN", from, StringComparison.Ordinal)];
    }

    // ---------- d2: P2 ----------

    [Fact]
    public async Task P2_AnInvalidFolder_GivesARefusal_ZeroLaunches_NoRoundRow_TheSameStage_AndNoWorktree()
    {
        var broken = WorkPlan.Base();
        broken[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", dependsOn: "\n  - t9-nobody");
        await using var repo = await RepoAsync(broken);
        var server = Server();
        try
        {
            await OpenAsync(server, repo);
            var before = await StdioServer.Call(server, 5, "status", $$"""{"repoPath":{{JsonSerializer.Serialize(repo.Path)}},"branch":"{{Branch}}"}""");

            var answer = await ReviewAsync(server, repo);

            answer.GetProperty("error").GetString().Should().ContainAll("unknown-dependency", "t9-nobody", "1 error", "limits source default", "No reviewer was launched");
            Prompts().Should().BeEmpty("zero reviewer launches");
            var after = await StdioServer.Call(server, 6, "status", $$"""{"repoPath":{{JsonSerializer.Serialize(repo.Path)}},"branch":"{{Branch}}"}""");
            after.GetRawText().Should().Be(before.GetRawText(), "the same stage and round count");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }

        RoundRows().Should().Be(0, "no round row was written");
        AnyWorktree().Should().BeFalse("no worktree was made");
    }

    // ---------- d3: P4 ----------

    private async Task<string> RefusedAsync(TempGitRepo repo)
    {
        var server = Server();
        try
        {
            await OpenAsync(server, repo);
            var answer = await ReviewAsync(server, repo);
            answer.TryGetProperty("error", out var error).Should().BeTrue($"the working tree differs from the commit, got: {answer}");
            Prompts().Should().BeEmpty("no reviewer was launched");

            return error.GetString()!;
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task P4_AnEditedPlanFileUnderTheScope_IsRefusedAndNamed_WhenHeadIsTheCommitOfTheRound()
    {
        await using var repo = await RepoAsync();
        Lay(repo, new() { [Task] = File.ReadAllText(Path.Combine(repo.Path, Task)) + "# an edit nobody committed\n" });

        var refused = await RefusedAsync(repo);

        refused.Should().ContainAll($"'{Task}'", "differ in the working tree", "the reviewers read the commit", "commit them");
    }

    [Fact]
    public async Task P4_AStagedPlanFile_IsRefusedAndNamed()
    {
        await using var repo = await RepoAsync();
        Lay(repo, new() { ["plan/PLAN.md"] = "# The plan, staged but not committed\n" });
        await repo.GitAsync("add", "plan/PLAN.md");

        (await RefusedAsync(repo)).Should().ContainAll("'plan/PLAN.md'", "differ in the working tree");
    }

    [Fact]
    public async Task P4_AnUntrackedPlanFileUnderTheScope_IsRefusedAndNamed()
    {
        await using var repo = await RepoAsync();
        Lay(repo, new() { ["plan/epoch-1/e1-core/stories/s1-first/tasks/t2-new/task.yaml"] = WorkPlan.Task("t2-new") });

        (await RefusedAsync(repo)).Should().ContainAll("'plan/epoch-1/e1-core/stories/s1-first/tasks/t2-new/task.yaml'", "differ in the working tree");
    }

    [Fact]
    public async Task P4_AFileTheReviewersDoNotRead_IsNoReasonToRefuse()
    {
        await using var repo = await RepoAsync();
        Lay(repo, new() { ["plan/epoch-1/e1-core/RESULTS_e1.md"] = "# results\n", ["notes.txt"] = "an untracked note\n", ["app.cs"] = "v2 - an edit of code, not of the plan\n" });
        var server = Server();
        try
        {
            await OpenAsync(server, repo);

            var answer = await ReviewAsync(server, repo);

            answer.TryGetProperty("error", out var error).Should().BeFalse($"nothing the reviewers read differs, got: {error}");
            Prompts().Should().ContainSingle();
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }

    [Fact]
    public async Task P4_WhenHeadIsAnotherCommit_TheRoundRuns_AndTheReviewerLineSaysTheWorkingTreeWasNotCompared()
    {
        await using var repo = await RepoAsync();
        var tip = await repo.HeadAsync();
        await repo.GitAsync("checkout", "-b", "side");
        Lay(repo, new() { ["side.txt"] = "another line of work\n", [Task] = "an edit that the commit does not hold\n" });
        await repo.CommitAsync("another commit");
        var server = Server();
        try
        {
            await OpenAsync(server, repo);

            var answer = await ReviewAsync(server, repo);

            answer.TryGetProperty("error", out var error).Should().BeFalse($"HEAD is another commit, so the working tree says nothing about this one; got: {error}");
            answer.GetProperty("reviewers").GetString().Should().ContainAll("not compared", "HEAD is", tip[..12]);
            Prompts().Should().ContainSingle().Which.Should().Contain("# The plan").And.NotContain("an edit that the commit does not hold");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }
}
