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

    [Fact]
    public void NoDatabaseAtAll_StillAnswersOnceForEveryRoundAsked()
    {
        var answer = RoundsQuery.FindingsOfMany(_dir, [
            new RoundKeyAsked("s1", "CodeReview", 1),
            new RoundKeyAsked("s2", "PlanReview", 1),
        ]);

        answer.Rounds.Should().HaveCount(2, "a caller lining answers up against its rows must not be handed a short list");
        answer.Rounds.Should().OnlyContain(one => !one.Known);
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

    [Fact]
    public void WithoutAKeysFile_ItIsAUsageError_NotAnEmptyAnswer()
    {
        Program.FindingsManyJson(["--findings-many"]).Should().Be(64);
    }

    [Fact]
    public void AKeysFileThatCannotBeReadOrParsed_IsAUsageError()
    {
        Program.FindingsManyJson(["--findings-many", "--keys-file", Path.Combine(_dir, "not-there.json")])
            .Should().Be(64, "a missing file is the caller's mistake, not a database fault");

        Directory.CreateDirectory(_dir);
        var broken = Path.Combine(_dir, "broken.json");
        File.WriteAllText(broken, "[{\"session\":");
        Program.FindingsManyJson(["--findings-many", "--keys-file", broken]).Should().Be(64);
    }

    [Fact]
    public void AnEmptyKeysFile_IsAUsageError_BecauseNobodyAskedAboutAnything()
    {
        Directory.CreateDirectory(_dir);
        var empty = Path.Combine(_dir, "empty.json");
        File.WriteAllText(empty, "[]");

        Program.FindingsManyJson(["--findings-many", "--keys-file", empty]).Should().Be(64);
    }

    [Fact]
    public void MoreRoundsThanThePageCanHold_IsRefusedRatherThanAttempted()
    {
        Directory.CreateDirectory(_dir);
        var many = Path.Combine(_dir, "many.json");
        var keys = Enumerable.Range(0, RoundsQuery.MaxLimit + 1)
            .Select(at => $"{{\"session\":\"s\",\"stage\":\"CodeReview\",\"number\":{at}}}");
        File.WriteAllText(many, "[" + string.Join(",", keys) + "]");

        Program.FindingsManyJson(["--findings-many", "--keys-file", many]).Should().Be(64,
            $"the window itself holds at most {RoundsQuery.MaxLimit} rounds, so a bigger ask is about rounds nobody is holding");
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
