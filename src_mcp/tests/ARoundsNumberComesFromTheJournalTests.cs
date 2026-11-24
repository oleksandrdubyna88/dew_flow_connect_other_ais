using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A round's number is the next one in this session's journal for its stage — never the budget
/// counter plus one.
/// </summary>
/// <remarks>
/// <para>§9.6 of the feature-review plan. <c>LiveRound</c> numbered a round
/// <c>RoundsRunThisStage + 1</c>; <c>BeginCodeRoundAgain</c> resets that counter to zero, and so do
/// the escalation ladder and a person's <c>Continue</c>/<c>Fix</c>; the database upserts on
/// <c>(session_id, stage, number)</c>. So: finish code round 1, resolve, commit, run <c>again</c> —
/// and the second round REPLACED the first round's row. The log showed one round where two had run,
/// and the first one's findings and cost were gone.</para>
/// <para>The budget counter keeps counting rounds for the budget; the NUMBER a round is written under
/// comes from the journal, allocated under the session claim so two rounds cannot compute the same
/// one. Driven end to end because the defect lives between three classes — the state machine that
/// resets the counter, the live record that read it, and the projection that upserted — and no unit
/// of the three is wrong on its own.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ARoundsNumberComesFromTheJournalTests : IAsyncLifetime
{
    private const string Scope = """
        # SCOPE

        The reviewer's own words must survive a failure. A reviewer that falls over is recorded with
        an outcome and nothing else, so the round summary says "unparseable" without the text that
        would not parse, and the same answer replayed by hand goes through cleanly.

        When it is done: the raw answer is kept beside the session, the refusal names the file, and
        a failed reviewer still reports what it consumed. Constraint: no new dependency, and the
        launcher stays the one in v2.Shared.
        """;

    private const string Clean = """{"findings": []}""";

    private const string ThreeMajors = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"},
          {"severity": "major", "category": "reliability", "file": "app.cs", "line": 40,
           "title": "no timeout on the outbound call", "why": "a hung peer hangs the request", "fix": "add a timeout"},
          {"severity": "major", "category": "architecture", "file": "app.cs", "line": 70,
           "title": "the parser reaches into the transport", "why": "layers cross", "fix": "invert it"}
        ]}
        """;

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = TempDir.For("coai-journal-repo-").Path;
        _data = TempDir.For("coai-journal-data-").Path;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), string.Concat(Enumerable.Repeat("line\n", 100)));
        await Git("add", ".");
        await Git("commit", "-m", "the feature");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var dir in (string[])[_repo, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private static void Script(string answer, int exit = 0, string stderr = "")
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", exit.ToString());
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", stderr);
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service(int maxRounds = 3) =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(maxRounds, 2),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None, Noticing.None);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string AcceptAll(JsonElement answer) =>
        JsonSerializer.Serialize(
            Enumerable.Range(0, answer.GetProperty("findings").GetArrayLength())
                .Select(i => new { finding = i, action = "accept" }));

    /// <summary>
    /// The numbers one stage's rounds were written under, straight from the table — because the
    /// question is which ROWS exist, and a reader that hides a replaced row would hide the defect.
    /// </summary>
    private IReadOnlyList<(long Number, string HeadSha)> RowsOf(string stage)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_data, Store.RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT number, head_sha FROM rounds WHERE stage = $stage ORDER BY number";
        read.Parameters.AddWithValue("$stage", stage);
        using var rows = read.ExecuteReader();
        var found = new List<(long, string)>();
        while (rows.Read())
        {
            found.Add((rows.GetInt64(0), rows.GetString(1)));
        }

        return found;
    }

    /// <summary>The same stage's numbers as the session file remembers them.</summary>
    private async Task<IReadOnlyList<int>> TrailOf(PanelService service, string branch, string stage) =>
        [.. Parse(await service.StatusAsync(_repo, branch)).GetProperty("rounds").EnumerateArray()
            .Where(r => r.GetProperty("stage").GetString() == stage)
            .Select(r => r.GetProperty("number").GetInt32())];

    /// <summary>A session on "feature" whose code round has passed and been resolved: Done.</summary>
    private async Task<PanelService> CodeDoneOnFeature()
    {
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        Script(Clean);
        await service.ReviewPlanAsync(_repo, "feature", "a plan nobody objects to");
        await service.ResolveAsync(_repo, "feature", "[]");
        Script(Clean);
        Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope)).GetProperty("verdict").GetString()
            .Should().Be("proceed");
        await service.ResolveAsync(_repo, "feature", "[]");
        Parse(await service.StatusAsync(_repo, "feature")).GetProperty("stage").GetString().Should().Be("Done");
        return service;
    }

    /// <summary>The §9.6 sequence, exactly: round 1, resolve, a new commit, <c>again</c>.</summary>
    [Fact]
    public async Task ACodeRoundRunAgain_IsASecondRowInTheLog_NotTheFirstRowRewritten()
    {
        var service = await CodeDoneOnFeature();
        await File.AppendAllTextAsync(Path.Combine(_repo, "app.cs"), "the checkpoint's follow-up\n");
        await Git("add", ".");
        await Git("commit", "-m", "after the checkpoint");

        Script(Clean);
        var again = Parse(await service.ReviewCodeAsync(_repo, "feature", "main", Scope, again: true));
        again.GetProperty("verdict").GetString().Should().Be("proceed", $"the new commits are reviewed: {again}");

        var rows = RowsOf("CodeReview");
        rows.Select(r => r.Number).Should().Equal([1, 2],
            "two code rounds ran, and the log must hold both — the second used to REPLACE the first "
            + "through the upsert on (session_id, stage, number)");
        rows.Select(r => r.HeadSha).Distinct().Should().HaveCount(2, "each row names the commit IT reviewed");
        (await TrailOf(service, "feature", "CodeReview")).Should().Equal([1, 2],
            "and the session file numbers them the same way the database does");
    }
}
