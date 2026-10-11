using System.Security.Cryptography;
using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--round-counts</c>, run as the PROCESS the extension runs (story 2.1 of
/// <c>todo/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para>`common/scenario-tests.md`: a new flow ships with its scenario. The extension spawns this binary,
/// reads stdout and believes the exit code — so a unit test of <see cref="RoundCounts"/> proves the query
/// and nothing about the flow. What is exercised here is the argument parsing, the JSON contract and the
/// exit codes, through a real process against a real database file.</para>
/// <para>The exit codes are the part nothing else can check: <b>0</b> is an answer — an empty one when
/// no database exists yet, which the extension tells from "unavailable" by the exit code and the
/// parse; <b>65</b> is a bad argument; <b>74</b> a database that will not read; and <b>64</b> is the one
/// code this mode must NEVER answer, because 64 means "this binary has never heard of that mode" and
/// sends the extension down its fallback (<c>.agents/PROJECT.md</c>).</para>
/// </remarks>
public sealed class RoundCountsCliScenarioTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _data = Directory.CreateTempSubdirectory("coai-counts-cli-").FullName;
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_data, recursive: true); }
        catch (IOException) { }
    }

    private string Database => Path.Combine(_data, RoundsDb.FileName);

    private (int Code, string Out, string Err) Run(params string[] args) => OneShotProcess.Run(_data, args);

    private void Record()
    {
        var state = new SessionState("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };
        using var db = RoundsDb.Open(_data, _log)!;
        db.RecordRound(
            state,
            new RoundRecord("CodeReview", 1, "proceed", 0, "answered", Now.AddHours(-1))
            {
                StartedUtc = Now.AddHours(-1),
                ReviewerStates =
                [
                    new ReviewerState("codex", "Architecture", ReviewerState.Done, 0, "", 1.0, "gpt-6-luna"),
                    new ReviewerState("codex", "SecurityReliability", ReviewerState.Done, 0, "", 1.0, "gpt-6-luna"),
                ],
            },
            []);
    }

    private static RoundCountsAnswer Parsed(string text) =>
        JsonSerializer.Deserialize(text, ServerJsonContext.Default.RoundCountsAnswer)!;

    [Fact]
    public void TheCounts_AreJsonOnStdout_ExitZero_ForTheClockItWasGiven()
    {
        Record();

        var (code, text, _) = Run("--round-counts", "--now", Now.ToString("O"));

        code.Should().Be(0);
        var answer = Parsed(text);
        answer.NowUtc.Should().Be(Now.ToString("O"), "the clock is the one the caller fixed");
        answer.Windows.Select(w => w.Name).Should().Equal(["5h", "24h", "7d"]);
        var codex = answer.Windows[0].Rows.Single();
        codex.Provider.Should().Be("codex");
        codex.Rounds.Should().Be(new RoundsByStage(Plan: 0, Code: 1, Document: 0, Feature: 0));
        codex.Reviewers.Should().Be(2, "two reviewer rows, one round");
        codex.Models.Should().Equal([new ModelRounds("gpt-6-luna", 1)]);
        answer.ModelsKnownSince.Should().Be(Now.AddHours(-1).ToString("O"));
    }

    [Fact]
    public void WithoutNow_TheRealClockIsUsed_AndARoundJustRecordedIsInEveryWindow()
    {
        var state = new SessionState("s2", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };
        var started = DateTime.UtcNow.AddMinutes(-1);
        using (var db = RoundsDb.Open(_data, _log)!)
        {
            db.RecordRound(state, new RoundRecord("PlanReview", 1, "proceed", 0, "answered", started)
            {
                StartedUtc = started,
                ReviewerStates = [new ReviewerState("gemini", "PlanCritique", ReviewerState.Done, 0, "", 1.0, "gemini-3.8-flash-low")],
            }, []);
        }

        var (code, text, _) = Run("--round-counts");

        code.Should().Be(0);
        Parsed(text).Windows.Should().AllSatisfy(w => w.Rows.Single().Rounds.Plan.Should().Be(1));
    }

    [Fact]
    public void ABadArgument_IsAUsageErrorNot64()
    {
        Record();

        var unparseable = Run("--round-counts", "--now", "yesterday");
        var unknownFlag = Run("--round-counts", "--since", Now.ToString("O"));
        var twice = Run("--round-counts", "--now", Now.ToString("O"), "--now", Now.ToString("O"));
        var dangling = Run("--round-counts", "--now");

        foreach (var (name, run) in new[] { ("an unparseable --now", unparseable), ("an unknown flag", unknownFlag), ("--now twice", twice), ("a --now with no value", dangling) })
        {
            run.Code.Should().Be(65, $"{name} is a bad REQUEST to a mode this binary has; 64 would read as an old binary");
            run.Out.Should().BeEmpty($"{name}: a refused request answers nothing a caller could mistake for a count");
            run.Err.Should().Contain("--round-counts", $"{name}: the sentence names the mode and what it takes");
        }

        unparseable.Err.Should().Contain("yesterday", "the refused value is quoted back");
        Run("--round-countz").Code.Should().Be(64, "the control: a mode the binary does NOT have is 64, and only that");
    }

    [Fact]
    public void AMissingDatabase_IsAnEmptyAnswer_ExitZero_WithTheNullSaidOutLoud()
    {
        var (code, text, _) = Run("--round-counts", "--now", Now.ToString("O"));

        code.Should().Be(0, "a machine that has never run a round is asking a fair question");
        var answer = Parsed(text);
        answer.Windows.Should().HaveCount(3).And.AllSatisfy(w => w.Rows.Should().BeEmpty());
        answer.ModelsKnownSince.Should().BeNull();
        text.Should().Contain("\"modelsKnownSince\": null", "the key is present with null, not omitted — the contract says null");
        File.Exists(Database).Should().BeFalse("a count must not create the store");
    }

    [Fact]
    public void TheMode_OpensTheDatabaseReadOnly_TheFileIsUnchangedAndNotMigrated()
    {
        var stopBefore = Array.FindIndex(Schema.Steps, step => step.Contains("ALTER TABLE reviewers ADD COLUMN model", StringComparison.Ordinal));
        stopBefore.Should().BeGreaterThan(0, "the step that adds the column is what this fixture stops before");
        using (var make = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            make.Open();
            using var sql = make.CreateCommand();
            sql.CommandText = string.Join(";\n", Schema.Steps[..stopBefore]) + $"; PRAGMA user_version={stopBefore};"
                + "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
                + $"INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'CodeReview', 1, 'done', 'proceed', '{Now.AddHours(-1):O}', '{Now:O}');"
                + "INSERT INTO reviewers (round_id, provider, role, status) VALUES (1, 'codex', 'Architecture', 'done');";
            sql.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var bytes = SHA256.HashData(File.ReadAllBytes(Database));
        var written = File.GetLastWriteTimeUtc(Database);

        var (code, text, _) = Run("--round-counts", "--now", Now.ToString("O"));

        code.Should().Be(0);
        Parsed(text).Windows[0].Rows.Single().Rounds.Code.Should().Be(1, "an older file's rounds are still counted");
        SHA256.HashData(File.ReadAllBytes(Database)).Should().Equal(bytes, "the mode reads; a read that migrates is a write");
        File.GetLastWriteTimeUtc(Database).Should().Be(written);
        using var check = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        check.Open();
        using var ask = check.CreateCommand();
        ask.CommandText = "PRAGMA user_version";
        ask.ExecuteScalar().Should().Be((long)stopBefore, "the step count is what the server's own open advances, never a reader");
    }

    [Fact]
    public void TheModeIsKnown_InTheHelp_AndInTheOneShotList()
    {
        Program.Classify(["--round-counts"]).Should().NotBe(Program.Startup.Usage, "64 is how the extension recognises a binary that predates the counter");
        Program.HelpText.Should().Contain("`--round-counts");
        File.ReadAllText(Path.Combine(ProductionSources.RepositoryRoot(), ".agents", "PROJECT.md"))
            .Should().Contain("`--round-counts`", "adding a one-shot mode means adding it to PROJECT.md's list");
    }
}
