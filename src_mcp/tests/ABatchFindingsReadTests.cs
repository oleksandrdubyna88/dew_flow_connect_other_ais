using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Reading MANY rounds' findings in one process — the read behind a bulk export.
/// </summary>
/// <remarks>
/// <para>The extension used to spawn this binary once per round. Five hundred selected rounds were
/// five hundred processes, four at a time; they are one now.</para>
/// <para><b>Why <c>--findings-many</c> is its own <c>args[0]</c> and not a flag on
/// <c>--findings</c>.</b> A server that predates the mode must REFUSE it legibly. Given
/// <c>--findings --keys-file …</c> an older binary accepts <c>--findings</c>, finds no
/// <c>--session</c>, and exits 69 — "no such round, its findings were never recorded" — which a
/// client renders faithfully as a round that recorded nothing, and a whole export would have
/// declared every selected round empty. An unknown <c>args[0]</c> exits 64, which is the only code
/// meaning "this binary is too old" and the only one the client falls back on.</para>
/// </remarks>
[Collection("console-out")]
public sealed class ABatchFindingsReadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-many-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private static SessionState Session(string id) =>
        new(id, "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static Finding Found(string title) =>
        new(Severity.Major, Category.Reliability, "src/Panel.cs", 40, title, title + " — because", "do this", ["codex"])
        {
            Role = "SecurityReliability",
        };

    private static RoundRecord Round(string stage, int number) =>
        new(stage, number, "proceed", 2, "all 3 reviewers answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow.AddMinutes(-4),
            Subject = "SCOPE — something",
        };

    [Fact]
    public void EveryRoundAskedAbout_ComesBackInOrder_CarryingItsOwnKey()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session("s1"), Round("PlanReview", 1), [Found("the plan skips a stage")]);
            db.RecordRound(Session("s1"), Round("CodeReview", 2), [Found("the retry never gives up"), Found("a cast where a check belongs")]);
            db.RecordRound(Session("s2"), Round("CodeReview", 1), []);
        }

        var answer = RoundsQuery.FindingsOfMany(_dir, [
            new RoundKeyAsked("s2", "CodeReview", 1),
            new RoundKeyAsked("s1", "CodeReview", 2),
            new RoundKeyAsked("s1", "PlanReview", 1),
        ]);

        answer.Rounds.Should().HaveCount(3, "one entry per round asked, whatever was found");
        answer.Rounds.Select(one => (one.SessionId, one.Stage, one.Number)).Should().Equal([
            ("s2", "CodeReview", 1), ("s1", "CodeReview", 2), ("s1", "PlanReview", 1),
        ], "the order asked, and each entry says which round it is — a client must never have to pair by index");
        answer.Rounds.Should().OnlyContain(one => one.Known);
        answer.Rounds[0].Findings.Should().BeEmpty("a round that produced nothing is KNOWN and empty");
        answer.Rounds[1].Findings.Should().HaveCount(2);
        answer.Rounds[1].Findings[0].Title.Should().Be("the retry never gives up");
        answer.Rounds[2].Findings.Should().ContainSingle().Which.Title.Should().Be("the plan skips a stage");
    }

    [Fact]
    public void ARoundTheDatabaseHasNeverHeardOf_KeepsItsSlotAndIsNotKnown()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session("s1"), Round("CodeReview", 1), [Found("a real one")]);
        }

        var answer = RoundsQuery.FindingsOfMany(_dir, [
            new RoundKeyAsked("s1", "CodeReview", 1),
            new RoundKeyAsked("nobody", "CodeReview", 9),
        ]);

        answer.Rounds.Should().HaveCount(2);
        answer.Rounds[0].Known.Should().BeTrue();
        answer.Rounds[1].Known.Should().BeFalse(
            "'never heard of it' and 'it found nothing' are different answers, and only one of them is a clean round");
        answer.Rounds[1].SessionId.Should().Be("nobody");
    }

    /// <summary>
    /// No database is a read FAILURE, and the query says so by throwing rather than by answering.
    /// </summary>
    /// <remarks>
    /// It used to answer "not known" for every round asked about, which is a statement about content
    /// nobody could read — and the mode in front of it now says the opposite. Two answers to one
    /// question is what the second code round objected to, so there is one: the open throws, and
    /// every caller turns that into EX_IOERR. (Code round, gemini.)
    /// </remarks>
    [Fact]
    public void NoDatabaseAtAll_IsARaisedFailure_NotAListOfRoundsNobodyRecorded()
    {
        var asking = () => RoundsQuery.FindingsOfMany(_dir, [
            new RoundKeyAsked("s1", "CodeReview", 1),
            new RoundKeyAsked("s2", "PlanReview", 1),
        ]);

        asking.Should().Throw<Exception>("a database that is not there cannot answer about any round");
    }

    [Fact]
    public void AKeyMissingItsNumberOrItsSession_IsABadRequest_NotARoundThatWasNeverRecorded()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dir);
        try
        {
            using (var db = RoundsDb.Open(_dir, _log)!)
            {
                db.RecordRound(Session("s1"), Round("CodeReview", 1), [Found("a real one")]);
            }
            var asked = Path.Combine(_dir, "partial.json");

            // No number: it used to deserialise to the default and come back `known: false`, so a
            // malformed request became a false statement about somebody's round. (Code round, codex.)
            File.WriteAllText(asked, "[{\"sessionId\":\"s1\",\"stage\":\"CodeReview\"}]");
            Program.FindingsManyJson(["--findings-many", "--keys-file", asked]).Should().Be(65);

            File.WriteAllText(asked, "[{\"stage\":\"CodeReview\",\"number\":1}]");
            Program.FindingsManyJson(["--findings-many", "--keys-file", asked]).Should().Be(65,
                "a key with no session names no round at all");

            File.WriteAllText(asked, "[{\"sessionId\":\"s1\",\"number\":1}]");
            Program.FindingsManyJson(["--findings-many", "--keys-file", asked]).Should().Be(65,
                "and a round number means nothing without the stage it belongs to");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COAI_DATA_DIR", null);
        }
    }

    [Fact]
    public void AskingAboutNothing_AnswersNothing()
    {
        RoundsQuery.FindingsOfMany(_dir, []).Rounds.Should().BeEmpty();
    }

    [Fact]
    public void TheModeHasItsOwnArgument_SoAnOlderBinaryRefusesItWith64()
    {
        Program.Classify(["--findings-many"]).Should().Be(Program.Startup.FindingsMany);
        Program.Classify(["--findings"]).Should().Be(Program.Startup.Findings, "and the single-round read is untouched");
    }

    /// <summary>
    /// A server that KNOWS the mode never exits 64, whatever is wrong with the request.
    /// </summary>
    /// <remarks>
    /// <para>The plan round found this, twice and independently. 64 is the client's signal to fall
    /// back to one spawn per round, because that is what an OLD binary answers to an argument it has
    /// never heard of. If this binary also answered 64 to a keys file it could not read, the client
    /// would read a bad request as an old server, quietly start five hundred processes and report a
    /// successful export — hiding the corruption behind the very path the fallback exists for.</para>
    /// <para>So a malformed request is <b>65, EX_DATAERR</b>: the input was wrong, and the client
    /// must fail the export rather than retry it a different way.</para>
    /// </remarks>
    [Fact]
    public void WithoutAKeysFile_ItIsADataError_NotTheSignalThatMeansOldBinary()
    {
        Program.FindingsManyJson(["--findings-many"]).Should().Be(65);
    }

    [Fact]
    public void AKeysFileThatCannotBeReadOrParsed_IsADataError()
    {
        Program.FindingsManyJson(["--findings-many", "--keys-file", Path.Combine(_dir, "not-there.json")])
            .Should().Be(65, "a missing file is the caller's mistake, and not a reason to retry per round");

        Directory.CreateDirectory(_dir);
        var broken = Path.Combine(_dir, "broken.json");
        File.WriteAllText(broken, "[{\"sessionId\":");
        Program.FindingsManyJson(["--findings-many", "--keys-file", broken]).Should().Be(65);
    }

    [Fact]
    public void AnEmptyKeysFile_IsADataError_BecauseNobodyAskedAboutAnything()
    {
        Directory.CreateDirectory(_dir);
        var empty = Path.Combine(_dir, "empty.json");
        File.WriteAllText(empty, "[]");

        Program.FindingsManyJson(["--findings-many", "--keys-file", empty]).Should().Be(65);
    }

    [Fact]
    public void MoreRoundsThanThePageCanHold_IsRefusedRatherThanAttempted()
    {
        Directory.CreateDirectory(_dir);
        var many = Path.Combine(_dir, "many.json");
        var keys = Enumerable.Range(0, RoundsQuery.MaxLimit + 1)
            .Select(at => $"{{\"sessionId\":\"s\",\"stage\":\"CodeReview\",\"number\":{at}}}");
        File.WriteAllText(many, "[" + string.Join(",", keys) + "]");

        Program.FindingsManyJson(["--findings-many", "--keys-file", many]).Should().Be(65,
            $"the window itself holds at most {RoundsQuery.MaxLimit} rounds, so a bigger ask is about rounds nobody is holding");
    }

    /// <summary>
    /// The happy path THROUGH the one-shot mode, so the JSON contract is exercised end to end.
    /// </summary>
    /// <remarks>
    /// The server is Native-AOT: a DTO missing from <c>ServerJsonContext</c> is a RUNTIME failure,
    /// not a compile error, and every other test here calls <c>FindingsOfMany</c> directly and would
    /// stay green while the mode itself threw on serialization. The plan round's local reviewer
    /// named exactly that gap. This reads what the mode actually writes to stdout.
    /// </remarks>
    [Fact]
    public void TheModeWritesTheAnswerAsJson_WhichIsWhatTheAotContextMustCarry()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dir);
        try
        {
            using (var db = RoundsDb.Open(_dir, _log)!)
            {
                db.RecordRound(Session("s1"), Round("CodeReview", 1), [Found("the retry never gives up")]);
            }
            var asked = Path.Combine(_dir, "asked.json");
            File.WriteAllText(asked, "[{\"sessionId\":\"s1\",\"stage\":\"CodeReview\",\"number\":1},{\"sessionId\":\"s9\",\"stage\":\"PlanReview\",\"number\":3}]");

            var (written, code) = Stdout.Of(
                () => Program.FindingsManyJson(["--findings-many", "--keys-file", asked]));

            code.Should().Be(0);
            using var answer = System.Text.Json.JsonDocument.Parse(written);
            var rounds = answer.RootElement.GetProperty("rounds");
            rounds.GetArrayLength().Should().Be(2, "one entry per round asked, in the order asked");
            rounds[0].GetProperty("sessionId").GetString().Should().Be("s1");
            rounds[0].GetProperty("known").GetBoolean().Should().BeTrue();
            rounds[0].GetProperty("findings").GetArrayLength().Should().Be(1);
            rounds[0].GetProperty("findings")[0].GetProperty("title").GetString().Should().Be("the retry never gives up");
            rounds[1].GetProperty("sessionId").GetString().Should().Be("s9");
            rounds[1].GetProperty("known").GetBoolean().Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("COAI_DATA_DIR", null);
        }
    }

    /// <summary>
    /// No database at all is a FAILED read, not a list of rounds that were never recorded.
    /// </summary>
    /// <remarks>
    /// The code round's finding, and it is about what the file then CLAIMS. A caller asking about
    /// these rounds has just listed them out of this database; if the file has gone between the
    /// listing and the export, "never recorded" is a statement about content nobody could read.
    /// 74 (EX_IOERR) says the database, not the rounds — and the client marks every row failed.
    /// </remarks>
    [Fact]
    public void NoDatabaseAtAll_IsAReadFailure_NotFiveHundredRoundsThatWereNeverRecorded()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dir);
        try
        {
            Directory.CreateDirectory(_dir);
            var asked = Path.Combine(_dir, "asked.json");
            File.WriteAllText(asked, "[{\"sessionId\":\"s1\",\"stage\":\"CodeReview\",\"number\":1}]");

            Program.FindingsManyJson(["--findings-many", "--keys-file", asked]).Should().Be(74,
                "a database that is not there says nothing about the rounds that were asked about");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COAI_DATA_DIR", null);
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written — the no-database test.
        }
    }
}
