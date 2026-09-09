using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The one-shot log modes, run as the PROCESS the panel runs.
/// </summary>
/// <remarks>
/// <para>`common/scenario-tests.md`: a new flow ships with its scenario. `--log` and `--findings` are
/// the whole interface between the extension and this database — the panel spawns this binary,
/// reads stdout and believes the exit code — so a unit test of `RoundsQuery` proves the query and
/// nothing about the flow. What is exercised here is argument parsing, the JSON contract and the
/// exit codes, through a real process against a real database file.</para>
/// <para>The exit codes are the part that cannot be checked any other way: <b>0</b> is an answer,
/// <b>69</b> is a round the database has never heard of, and <b>74</b> is the database itself being
/// unreadable. The page draws a different thing for each, and the middle one used to be the same
/// number as the last.</para>
/// </remarks>
public sealed class LogCliScenarioTests : IDisposable
{
    private static string ServerExe
    {
        get
        {
            if (Environment.GetEnvironmentVariable("COAI_CONTRACT_EXE") is { Length: > 0 } published)
            {
                return published;
            }

            var configuration = AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug";

            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "src", "bin", configuration, "net10.0",
                OperatingSystem.IsWindows() ? "coai-mcp.exe" : "coai-mcp"));
        }
    }

    private readonly string _data = Directory.CreateTempSubdirectory("coai-logcli-").FullName;
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a failing test.
        }
    }

    private (int Code, string Out) Run(params string[] args)
    {
        var info = new ProcessStartInfo(ServerExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment["COAI_DATA_DIR"] = _data;
        using var process = Process.Start(info)!;

        // BOTH pipes are drained, and concurrently. Reading stdout to the end while stderr fills its
        // buffer is the classic deadlock: the child blocks writing, this blocks reading, and the
        // timeout below never gets a chance because nothing is timing the READ. And a process that
        // outlives its deadline is KILLED rather than left behind holding the temp directory this
        // test is about to delete. (CodeRabbit, on the pull request.)
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            throw new TimeoutException("a one-shot mode did not answer and exit within a minute");
        }

        Task.WaitAll([stdout, stderr], 10_000).Should().BeTrue("the pipes close when the child does");

        return (process.ExitCode, stdout.Result);
    }

    private void Record()
    {
        var session = new SessionState("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };
        var started = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        using var db = RoundsDb.Open(_data, _log)!;
        db.RecordRound(
            session,
            new RoundRecord("CodeReview", 1, "proceed", 1, "all 3 reviewers answered", started)
            {
                StartedUtc = started,
                Subject = "SCOPE — something",
                ReviewerStates = [new ReviewerState("codex", "Architecture", ReviewerState.Done, 1, "", 4.2)],
            },
            [new Finding(
                Severity.Major, Category.Reliability, "src/Panel.cs", 40, "the fan rebuilt its buffer",
                "quadratic", "append", ["codex"]) { Role = "SecurityReliability" }]);
    }

    [Fact]
    public void ThePagedLog_IsJsonOnStdout_WithTotalsAndNoFindings()
    {
        Record();

        var (code, text) = Run("--log", "--paged", "--limit", "200");

        code.Should().Be(0);
        text.Should().NotContain("the fan rebuilt its buffer", "the list stopped carrying findings");
        var log = JsonSerializer.Deserialize<LoggedLog>(text, ServerJsonContext.Default.LoggedLog)!;
        log.Rounds.Should().ContainSingle();
        log.Rounds[0].FoundCount.Should().Be(1, "how many, without what");
        log.Rounds[0].Cursor.Should().Contain("|", "the cursor is the pair the list is ordered by");
        log.Totals.Rounds.Should().Be(1);
        log.Totals.Findings.Should().Be(1);
    }

    [Fact]
    public void TheUnpagedLogsDefault_IsStillThreeHundred_NotThePageSize()
    {
        // An extension too old to send `--paged` is also too old to ask for a second page, so
        // shrinking ITS default to 200 would simply take a hundred rounds off the only list it can
        // show. Two hundred and one rounds, no `--limit`: it must still see more than a page.
        // (CodeRabbit, on the pull request.)
        var start = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var session = new SessionState("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };
        using (var db = RoundsDb.Open(_data, _log)!)
        {
            for (var n = 1; n <= 201; n++)
            {
                var started = start.AddSeconds(n);
                db.RecordRound(
                    session,
                    new RoundRecord("CodeReview", n, "proceed", 0, "all 3 reviewers answered", started)
                    {
                        StartedUtc = started,
                        Subject = "SCOPE",
                    },
                    []);
            }
        }

        var (paged, pagedText) = Run("--log", "--paged");
        var (legacy, legacyText) = Run("--log");

        paged.Should().Be(0);
        legacy.Should().Be(0);
        Rounds(pagedText).Should().Be(200, "a page is two hundred");
        Rounds(legacyText).Should().Be(201, "and the old shape's default is three hundred");
    }

    private static int Rounds(string json) =>
        JsonSerializer.Deserialize<LoggedLog>(json, ServerJsonContext.Default.LoggedLog)!.Rounds.Count;

    [Fact]
    public void TheUnpagedLog_StillAnswersWhatItAnsweredYesterday()
    {
        // The pairing an old extension is in with a new binary. Nothing about it may change.
        Record();

        var (code, text) = Run("--log", "--limit", "300");

        code.Should().Be(0);
        text.Should().Contain("the fan rebuilt its buffer", "the old shape carries them inline");
    }

    [Fact]
    public void OneRoundsFindings_AreTheirOwnMode_AndExitZero()
    {
        Record();

        var (code, text) = Run("--findings", "--session", "s1", "--stage", "CodeReview", "--number", "1");

        code.Should().Be(0);
        var answer = JsonSerializer.Deserialize<LoggedRoundFindings>(
            text, ServerJsonContext.Default.LoggedRoundFindings)!;
        answer.Known.Should().BeTrue();
        answer.Findings.Should().ContainSingle().Which.Title.Should().Be("the fan rebuilt its buffer");
    }

    [Fact]
    public void ARoundTheDatabaseDoesNotHold_ExitsSixtyNine_AndPrintsNoJson()
    {
        Record();

        var (code, text) = Run("--findings", "--session", "s1", "--stage", "CodeReview", "--number", "99");

        code.Should().Be(69, "EX_UNAVAILABLE — never recorded, which is not 'it found nothing'");
        text.Should().BeEmpty("stdout is the interface, and there is no answer to put on it");
    }

    [Fact]
    public void AnUnknownMode_IsStillRefused_RatherThanGuessedAt()
    {
        // What makes the extension's fallback work: a server too old for a flag says exactly this.
        var (code, _) = Run("--paged");

        code.Should().Be(64, "EX_USAGE, which is what the older binary answers for --paged");
    }
}
