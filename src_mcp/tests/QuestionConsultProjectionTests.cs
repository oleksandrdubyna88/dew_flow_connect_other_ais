using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Schema step 17 (PLAN_question_consultant.md, S2 acceptance 4): a database at the step before gains the two
/// tables on open; a question reaches them and every state updates the same rows; the startup reprojection
/// rebuilds them from the files. Real SQLite over a temp directory, no fakes — the point is that the SQL runs.
/// </summary>
public sealed class QuestionConsultProjectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-qproj-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private QuestionConsultStore Store() => new(
        _dir,
        warn: null,
        projected: record => new Projection(_dir, _log)
            .Write(db => QuestionConsultTable.Record(db, QuestionConsultRows.Head(record), QuestionConsultRows.Entries(record)), "the question"));

    private static QuestionConsultRecord Record() => new(
        "b8f1c2d3e4a5b6c7d8e9f0a1b2c3d4e5", "claude:session-1", "claude", "no-session", "D:/repo", "feat/x", "abc1234",
        "Which retry shape?", DateTime.UtcNow.AddMinutes(-3).ToString("O"))
    {
        Context = "I tried a fixed wait; the options are a ladder or a breaker.",
        RunnerPid = 4242,
        Rows =
        [
            new QuestionRowRecord("astra-web", "codex", "gpt-6-astra", "codex", "question-web", "The internet", "web", "unconfined"),
            new QuestionRowRecord("grok", "grok-openrouter", "x-ai/grok-4.7", "api", "question-opinion", "The best developer's opinion", "none", ""),
        ],
    };

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    private long Count(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = sql;

        return (long)ask.ExecuteScalar()!;
    }

    private string Text(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = sql;

        return ask.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    [Fact]
    public void TheStepIsAppendedLast_AndADatabaseAtTheStepBefore_GainsTheTwoTablesOnOpen()
    {
        // By POSITION, not as the last step: the security lane appended 18 after it on 2026-10-03.
        Schema.Steps[16].Should().Be(Schema.TheQuestionsAsked, "appended after WhyARoundDidNotRun, never inserted");
        Directory.CreateDirectory(_dir);
        using (var db = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            // Every step BEFORE the one that creates the tables, found by asking which step that is.
            var creates = Array.FindIndex(Schema.Steps, step => step.Contains("question_consults", StringComparison.Ordinal));
            creates.Should().Be(16);
            var before = Schema.Steps[..creates];
            make.CommandText = string.Join(";\n", before) + $"; PRAGMA user_version={before.Length}";
            make.ExecuteNonQuery();
        }

        Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('question_consults', 'question_consult_rows')").Should().Be(0);

        // Opening is what steps it.
        using (var stepped = RoundsDb.Open(_dir, _log))
        {
            stepped.Should().NotBeNull();
        }

        Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('question_consults', 'question_consult_rows')").Should().Be(2);
        Text("PRAGMA user_version").Should().Be(Schema.Steps.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AQuestionIsProjectedTheMomentItIsAsked_AndEveryStateUpdatesTheSameRows()
    {
        var store = Store();
        var record = Record();
        store.Write(record);

        Count("SELECT COUNT(*) FROM question_consults").Should().Be(1);
        Count("SELECT COUNT(*) FROM question_consult_rows").Should().Be(2);
        Text("SELECT status FROM question_consults").Should().Be(QuestionConsultStatuses.Consulting);
        Text("SELECT flag FROM question_consult_rows WHERE row_id = 'astra-web'").Should().Be("unconfined", "D13's flag reaches the log");

        var settledRows = record
            .WithRow(record.Rows[0] with { Status = RowOutcomes.Answered, Advice = "Use a ladder.", Seconds = 12.5, TokensIn = 1000, TokensOut = 200, CostUsd = 0.02 })
            .WithRow(record.Rows[1] with { Status = RowOutcomes.TimedOut, Reason = "the row ran past its budget", Seconds = 300 });
        var answered = settledRows with { Status = QuestionConsultStatuses.Partial, Outcome = QuestionOutcomes.AnsweredByConsultants, EndedUtc = DateTime.UtcNow.ToString("O") };
        store.Write(answered);

        Count("SELECT COUNT(*) FROM question_consults").Should().Be(1, "one row per question that ADVANCES");
        Count("SELECT COUNT(*) FROM question_consult_rows").Should().Be(2);
        Text("SELECT status FROM question_consults").Should().Be(QuestionConsultStatuses.Partial);
        Text("SELECT answered FROM question_consults").Should().Be("1");
        Text("SELECT seconds FROM question_consults").Should().Be("312.5");
        Text("SELECT cost_usd FROM question_consults").Should().Be("0.02", "the sum of what is known");
        Text("SELECT advice FROM question_consult_rows WHERE row_id = 'astra-web'").Should().Be("Use a ladder.");
        Text("SELECT status FROM question_consult_rows WHERE row_id = 'grok'").Should().Be(RowOutcomes.TimedOut);
    }

    [Fact]
    public void AdviceIsCutAtSixteenKilobytesInTheTable_AndWholeInTheFile()
    {
        var store = Store();
        var record = Record();
        var advice = new string('a', 20_000);
        store.Write(record.WithRow(record.Rows[0] with { Status = RowOutcomes.Answered, Advice = advice }));

        var stored = Text("SELECT advice FROM question_consult_rows WHERE row_id = 'astra-web'");
        stored.Length.Should().BeLessThan(advice.Length).And.BeGreaterThan(QuestionConsultRowEntry.AdviceLimit);
        stored.Should().EndWith("[cut at 16 KB — the record file holds the rest]");
        store.Read(record.Id)!.Rows[0].Advice.Should().HaveLength(20_000);
    }

    [Fact]
    public void TheStartupReprojection_RebuildsTheTablesFromTheFiles()
    {
        var store = Store();
        var record = Record() with { Status = QuestionConsultStatuses.Answered, Outcome = QuestionOutcomes.AnsweredByConsultants };
        store.Write(record);
        using (var db = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            db.Open();
            using var wipe = db.CreateCommand();
            wipe.CommandText = "DELETE FROM question_consult_rows; DELETE FROM question_consults";
            wipe.ExecuteNonQuery();
        }

        Count("SELECT COUNT(*) FROM question_consults").Should().Be(0, "the view was lost — the file is the truth");

        var reprojected = Reproject(store);

        reprojected.Should().Be(1);
        Count("SELECT COUNT(*) FROM question_consults").Should().Be(1);
        Count("SELECT COUNT(*) FROM question_consult_rows").Should().Be(2);
        Text("SELECT outcome FROM question_consults").Should().Be(QuestionOutcomes.AnsweredByConsultants);
    }

    /// <summary>What <c>QuestionConsultService.Reproject</c> does, over the store alone — one open for the whole pass.</summary>
    private int Reproject(QuestionConsultStore store)
    {
        var records = store.All();
        new Projection(_dir, _log).Write(db =>
        {
            foreach (var record in records)
            {
                QuestionConsultTable.Record(db, QuestionConsultRows.Head(record), QuestionConsultRows.Entries(record));
            }
        }, "the questions");

        return records.Count;
    }

    // ---------- S4: what --log hands the Logs page's Questions tab ----------

    [Fact]
    public void TheLog_ListsEachQuestionOnce_WithEveryModelRowUnderIt_AndTheOutcome()
    {
        var record = Record();
        var store = Store();
        var answered = record.WithRow(record.Rows[0] with { Status = RowOutcomes.Answered, Advice = "Use a ladder.", Seconds = 12.5, CostUsd = 0.02 });
        store.Write(answered with { Status = QuestionConsultStatuses.Partial, Outcome = QuestionOutcomes.PersonAsked, EscalationId = "esc-1", EndedUtc = DateTime.UtcNow.ToString("O") });

        var listed = RoundsQuery.Read(_dir).QuestionConsults;

        var question = listed.Should().ContainSingle("one row per question, never one per state it passed through").Subject;
        question.Asked.Question.Should().Be("Which retry shape?");
        question.Asked.Outcome.Should().Be(QuestionOutcomes.PersonAsked, "the tab's what-happened-next column");
        question.Asked.EscalationId.Should().Be("esc-1");
        question.Answers.Select(a => a.RowId).Should().Equal("astra-web", "grok");
        var web = question.Answers[0];
        web.Advice.Should().Be("Use a ladder.");
        web.Flag.Should().Be("unconfined", "D13's flag reaches the log beside the answer it produced");
        web.CostUsd.Should().Be(0.02);
        question.Answers[1].CostUsd.Should().BeNull("an unpriced row is a blank, never a zero");
    }

    [Fact]
    public void TheLogCutsEachRowsAdvice_SoAHundredQuestionsAreNotNineteenMegabytes()
    {
        var record = Record();
        Store().Write(record.WithRow(record.Rows[0] with { Status = RowOutcomes.Answered, Advice = new string('a', QuestionConsultLog.AdviceShown * 3) }));

        RoundsQuery.Read(_dir).QuestionConsults.Single().Answers[0].Advice.Length.Should().Be(QuestionConsultLog.AdviceShown);
    }

    [Fact]
    public void ADatabaseWithoutTheQuestionTables_ReadsAsNoQuestions_AndTheRestOfTheLogStillAnswers()
    {
        Directory.CreateDirectory(_dir);
        using (var db = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            var creates = Array.FindIndex(Schema.Steps, step => step.Contains("question_consults", StringComparison.Ordinal));
            make.CommandText = string.Join("\n", Schema.Steps.Take(creates));
            make.ExecuteNonQuery();
        }

        var log = RoundsQuery.Read(_dir);

        log.QuestionConsults.Should().BeEmpty("an older binary's database has no question tables — an empty tab, not an error");
        log.Totals.Should().NotBeNull();
    }

    [Fact]
    public void TheLogsWireCarriesTheQuestions_UnderTheKeyTheExtensionReads()
    {
        var record = Record();
        Store().Write(record);

        var json = System.Text.Json.JsonSerializer.Serialize(RoundsQuery.Read(_dir), ServerJsonContext.Default.LoggedLog);

        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var first = parsed.RootElement.GetProperty("questionConsults")[0];
        first.GetProperty("asked").GetProperty("id").GetString().Should().Be(record.Id);
        first.GetProperty("answers")[0].GetProperty("rowId").GetString().Should().Be("astra-web");
    }

    [Fact]
    public void AWriterThatThrows_IsSwallowedByTheProjection_SoAQuestionNeverFailsOverItsView()
    {
        var act = () => new Projection(_dir, _log).Write(_ => throw new InvalidOperationException("the disk said no"), "the test");

        act.Should().NotThrow();
    }
}
