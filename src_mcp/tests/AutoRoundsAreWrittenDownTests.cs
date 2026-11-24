using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every decision policy auto makes in a round that ran is written down three times: the session file's note, <c>coai.db</c>'s
/// <c>rounds.note</c> and a Serilog line — a Warning when nothing was reviewed (<c>research/PLAN_auto_never_asks_a_person_E1.md</c>, § 8.2).
/// </summary>
/// <remarks>Real plan rounds over the fake CLI, in a throwaway repository and data folder, with a sink that keeps what was logged.</remarks>
[Collection("fakecli-env")]
public sealed class AutoRoundsAreWrittenDownTests
{
    private const string Plan = "# PLAN — a thing\n\n## Build order\n\n1. one\n2. two\n";

    private readonly ListSink _sink = new();

    private ILogger Log => new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_sink).CreateLogger();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Every reviewer exits 1 with a message that is not a rate limit (that path retries), so nobody answers.</summary>
    private static void NobodyAnswers()
    {
        CodeRoundHarness.SetAnswer(string.Empty);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "1");
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "boom");
    }

    private static string NoteInTheSessionFile(CodeRoundHarness harness) =>
        new SessionStore(harness.Data).Load(harness.Repo, "feature")!.Rounds[^1].Note;

    private static string NoteInTheDatabase(CodeRoundHarness harness)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(harness.Data, Store.RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT note FROM rounds WHERE stage = 'PlanReview' ORDER BY number DESC LIMIT 1";
        return (string)read.ExecuteScalar()!;
    }

    /// <summary>The lines about an auto decision, as the log file renders them (unquoted), each with its level.</summary>
    private IReadOnlyList<(LogEventLevel Level, string Text)> AutoLines =>
        [.. _sink.Levels.Zip(_sink.Lines).Where(l => l.Second.Contains("under policy auto", StringComparison.Ordinal)).Select(l => (l.First, l.Second))];

    [Fact]
    public async Task APlanRoundNobodyAnswered_IsNotReviewed_AndWrittenDownThreeTimes()
    {
        await using var harness = new CodeRoundHarness();
        await harness.InitializeAsync();
        var service = harness.Service(log: Log);
        await service.OpenAsync(harness.Repo, "feature");
        NobodyAnswers();

        var reply = Parse(await service.ReviewPlanAsync(harness.Repo, "feature", Plan, TestContext.Current.CancellationToken));

        reply.GetProperty("verdict").GetString().Should().Be("not_reviewed", reply.ToString());
        reply.GetProperty("instruction").GetString().Should().Contain("WITHOUT a review");
        Directory.Exists(Path.Combine(harness.Data, "escalations")).Should().BeFalse("no person was called, so no notice was written");
        NoteInTheSessionFile(harness).Should().Contain("no reviewer answered");
        NoteInTheDatabase(harness).Should().Contain("no reviewer answered");
        var line = AutoLines.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogEventLevel.Warning, "a round nobody reviewed is said as a warning");
        line.Text.Should().Contain("not_reviewed under policy auto").And.Contain("no reviewer answered");

        // And the stage moves on: an empty resolve opens the code stage, which review_code then accepts.
        var resolved = Parse(await service.ResolveAsync(harness.Repo, "feature", "[]"));
        resolved.TryGetProperty("error", out _).Should().BeFalse(resolved.ToString());
        new SessionStore(harness.Data).Load(harness.Repo, "feature")!.State.Stage.Should().Be(Stage.CodeReview);
        var code = await service.ReviewCodeAsync(harness.Repo, "feature", "main", CodeRoundHarness.Scope);
        code.Should().NotContain("plan gate comes first", "the unattended plan stage reached the code stage");
    }

    [Fact]
    public async Task AnAutoGoodEnough_IsAnInformationLineAndANote()
    {
        await using var harness = new CodeRoundHarness();
        await harness.InitializeAsync();
        var service = harness.Service(threshold: 0, maxRounds: 1, log: Log);
        await service.OpenAsync(harness.Repo, "feature");
        CodeRoundHarness.SetAnswer(CodeRoundHarness.OneMajor);

        var reply = Parse(await service.ReviewPlanAsync(harness.Repo, "feature", Plan, TestContext.Current.CancellationToken));

        reply.GetProperty("verdict").GetString().Should().Be("good_enough", reply.ToString());
        NoteInTheSessionFile(harness).Should().Contain("still gate after 1 round(s)");
        NoteInTheDatabase(harness).Should().Contain("still gate after 1 round(s)");
        var line = AutoLines.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogEventLevel.Information);
        line.Text.Should().Contain("good_enough under policy auto").And.Contain("still gate after 1 round(s)");
    }
}
