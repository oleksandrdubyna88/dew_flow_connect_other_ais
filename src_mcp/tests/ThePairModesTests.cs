using System.Text.Json;
using CoaiMcp.Collecting;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The two one-shot modes the review page reaches the pairs through.
/// </summary>
/// <remarks>
/// <para>The panel owns no SQLite. `PROJECT.md` sanctions exactly one shape for crossing that gap: a
/// mode chosen from `args[0]` before any transport opens, whose stdout is its entire interface — and
/// says that adding one means adding it to that list, which this change does.</para>
/// <para><b>Neither may exit 64.</b> 64 is how a caller detects a binary too old for a mode and falls
/// back; a request fault wearing that code would send it down the fallback and hide behind a
/// successful-looking answer. A bad decisions file is 65, as `--findings-many` answers an unreadable
/// keys file.</para>
/// </remarks>
[Collection("console-out")]
public sealed class ThePairModesTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-pairmode-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string? _was = Environment.GetEnvironmentVariable("COAI_DATA_DIR");

    private static readonly SessionState Session =
        new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    public ThePairModesTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dir);
    }

    private long Seed()
    {
        using var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!;
        var found = new Finding(
            Severity.Major, Category.Reliability, "src/Totals.cs", 5, "a race", "it races",
            "hold the lock", ["codex"]);
        db.RecordRound(
            Session,
            new RoundRecord("CodeReview", 1, "revise", 1, "all answered", new DateTime(2026, 9, 16)),
            [found],
            new RoundContext("SCOPE", "aaaa111", "claude-code"));
        db.RecordDecisions("s1", "CodeReview", 1, [Decisions.Accept([found], 0)]);

        using var read = new SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        read.Open();
        using var one = read.CreateCommand();
        one.CommandText = "SELECT id FROM findings";
        var id = (long)one.ExecuteScalar()!;

        db.RecordCollect(
            id, "", "collected", "", "bbbb222", "run-1",
            new CollectedPair("GetOrAdd", "CSharp", "method_1() { }", "method_1() { lock { } }"));

        return id;
    }

    /// <summary>Whatever the mode wrote to stdout — through the one capture, never a private copy.</summary>
    private static string Spoken(Func<int> mode, out int code)
    {
        var (spoken, exit) = Stdout.Of(mode);
        code = exit;

        return spoken;
    }

    [Fact]
    public void PairsJsonAnswersWhatWasCollected()
    {
        var id = Seed();

        var json = Spoken(() => Program.PairsJson(["--pairs-json"]), out var code);

        code.Should().Be(0);
        var answer = JsonSerializer.Deserialize(json, ServerJsonContext.Default.PairsAnswer);
        var pair = answer!.Items.Should().ContainSingle().Subject;
        pair.FindingId.Should().Be(id);
        pair.SymbolName.Should().Be("GetOrAdd");
        pair.Keep.Should().Be(Keep.Undecided);
        pair.Title.Should().Be("a race", "a person reviewing needs what the reviewers said it was");
    }

    /// <summary>
    /// The mode says where each pair WAS and what the reviewers SAID, beside the two skeletons.
    /// </summary>
    /// <remarks>
    /// <para>Read through <c>JsonDocument</c> rather than the typed answer, deliberately: this is the
    /// contract the extension's <c>pairOf</c> reads by property NAME, so the names are what is
    /// pinned here — a test through the typed accessor would compile against whatever the record
    /// happened to be called. It is also what let this test go red for the real symptom before the
    /// record existed: a property the answer did not carry, not a compile error.</para>
    /// <para><b>Two shas, named apart.</b> The BEFORE skeleton is the method at the commit the
    /// reviewers read (<c>headSha</c>); the AFTER skeleton is the method at the commit the collector
    /// found the fix in (<c>fixSha</c>, `Collector.LocateThenWalkAsync`). A page that labelled both
    /// with one sha would be confidently wrong about one of them.</para>
    /// </remarks>
    [Fact]
    public void PairsJsonSaysWhereEachPairWas_AndWhatTheReviewersSaid()
    {
        Seed();

        var json = Spoken(() => Program.PairsJson(["--pairs-json"]), out var code);

        code.Should().Be(0);
        using var answer = JsonDocument.Parse(json);
        var item = answer.RootElement.GetProperty("items")[0];
        item.GetProperty("repoPath").GetString().Should().Be("D:/repo");
        item.GetProperty("headSha").GetString().Should().Be("aaaa111", "the before skeleton is the method at this commit");
        item.GetProperty("fixSha").GetString().Should().Be("bbbb222", "and the after skeleton is the method at this one");
        item.GetProperty("file").GetString().Should().Be("src/Totals.cs");
        item.GetProperty("line").GetInt32().Should().Be(5);
        item.GetProperty("why").GetString().Should().Be("it races");
        item.GetProperty("fix").GetString().Should().Be("hold the lock");
    }

    [Fact]
    public void PairsKeepWritesABatch()
    {
        var id = Seed();
        var file = Path.Combine(_dir, "decisions.json");
        File.WriteAllText(file, JsonSerializer.Serialize(
            new KeepRequest([new KeepAsk(id, Keep.Kept)]), ServerJsonContext.Default.KeepRequest));

        var json = Spoken(() => Program.PairsKeep(["--pairs-keep", "--in", file]), out var code);

        code.Should().Be(0);
        JsonSerializer.Deserialize(json, ServerJsonContext.Default.KeepAnswer)!
            .Decided.Should().Be(1);

        using var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!;
        db.Pairs(50)[0].Keep.Should().Be(Keep.Kept, "the decision must be readable back");
    }

    /// <summary>A request fault is 65 — never 64, which means an old binary.</summary>
    /// <remarks>
    /// <para>The rule this repository states in both directions: a mode the binary does NOT have exits
    /// 64 so the caller falls back, and a mode it DOES have never does, whatever is wrong with the
    /// request. A fault wearing 64 hides behind the fallback.</para>
    /// <para>The cases are DATA rather than a switch, because the complexity limit is a rule about
    /// every method in this tree and a test is a method. (CodeRabbit.)</para>
    /// </remarks>
    public static TheoryData<string, string> BadRequests => new()
    {
        // A file that is not there at all — no `--in`.
        { "", "" },
        // `--in "   "` reaches File.ReadAllText as an ArgumentException the old filter did not catch.
        { "   ", "" },
        { "file", "{ this is not json" },
        { "file", """{"items":[{"findingId":1,"keep":7}]}""" },
        // `{}` deserialized to a null list, became an empty batch and exited 0 — so a misspelled
        // field looked successfully processed.
        { "file", "{}" },
    };

    [Theory]
    [MemberData(nameof(BadRequests))]
    public void ABadRequestIs65_Never64(string how, string content)
    {
        Seed();
        var file = Path.Combine(_dir, "decisions.json");
        if (content.Length > 0)
        {
            File.WriteAllText(file, content);
        }

        string[] args = how switch
        {
            "file" => ["--pairs-keep", "--in", file],
            "" => ["--pairs-keep"],
            _ => ["--pairs-keep", "--in", how],
        };

        Spoken(() => Program.PairsKeep(args), out var code);

        code.Should().Be(65, "64 is reserved for a mode this binary does not have");
    }
    /// <summary>An explicitly empty batch is a legitimate no-op, and says so.</summary>
    /// <remarks>
    /// The other half of the `no-items` case above: `{"items":[]}` is somebody deciding about nothing,
    /// which is fine, while `{}` is a document that forgot to say. One is 0 and the other is 65.
    /// </remarks>
    [Fact]
    public void AnEmptyBatchIsAllowed()
    {
        Seed();
        var file = Path.Combine(_dir, "decisions.json");
        File.WriteAllText(file, """{"items":[]}""");

        Spoken(() => Program.PairsKeep(["--pairs-keep", "--in", file]), out var code);

        code.Should().Be(0, "deciding about nothing is not a malformed request");
    }

    [Fact]
    public void AKeepThatIsNotADecisionIsRefused()
    {
        var id = Seed();
        var file = Path.Combine(_dir, "decisions.json");
        File.WriteAllText(file, $$"""{"items":[{"findingId":{{id}},"keep":42}]}""");

        Spoken(() => Program.PairsKeep(["--pairs-keep", "--in", file]), out _);

        using var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!;
        db.Pairs(50)[0].Keep.Should().Be(
            Keep.Undecided, "a value the column may not hold must not reach it");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _was);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
