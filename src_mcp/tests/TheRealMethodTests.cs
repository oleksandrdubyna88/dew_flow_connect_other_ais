using System.Reflection;
using System.Text.Json;
using CoaiMcp.Collecting;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The un-anonymised view: one pair's method read back out of real git, at both of its commits,
/// with its class — and nothing written, nothing sent, nothing widened.
/// </summary>
/// <remarks>
/// <para><b>Real git and real SQLite</b>, for the reasons <c>CollectorTests</c> and
/// <c>ThePairsThemselvesTests</c> each give: the failures this guards against are git's (a commit
/// pruned, a file renamed, a branch deleted) and the guarantee it protects is the database's (the
/// send still reads what it read). A fake of either would assert a belief.</para>
/// <para><b>The one test here whose failure is a privacy regression rather than a bug</b> is
/// <see cref="AReadWritesNothing_AndTheSendStillTransmitsTheSkeleton"/>: with real text on the
/// screen, the serialised upload is byte-identical to what it was before the screen showed it. The
/// plan says so in those words — "whitespace, field order, null against absent, all of it" — and a
/// test that compared the page or the record would pass over a serialisation difference, which is
/// the only kind that could actually leak.</para>
/// </remarks>
[Collection("console-out")]
public sealed class TheRealMethodTests : IAsyncLifetime
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-realmethod-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string? _was = Environment.GetEnvironmentVariable("COAI_DATA_DIR");
    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _git = null!;

    /// <summary>The defective method, and the same method fixed — one lock apart. `CollectorTests`' own pair.</summary>
    private const string Racy = """
        using System.Collections.Generic;

        public sealed class Totals
        {
            private readonly Dictionary<string, int> _items = new();

            public int GetOrAdd(string key, int value)
            {
                if (!_items.ContainsKey(key))
                {
                    _items.Add(key, value);
                }

                return _items[key];
            }
        }
        """;

    private const string Fixed = """
        using System.Collections.Generic;

        public sealed class Totals
        {
            private readonly Dictionary<string, int> _items = new();

            public int GetOrAdd(string key, int value)
            {
                lock (_items)
                {
                    if (!_items.ContainsKey(key))
                    {
                        _items.Add(key, value);
                    }

                    return _items[key];
                }
            }
        }
        """;

    /// <summary>What the collector stores for that pair — the two skeletons, and the name.</summary>
    private static readonly CollectedPair Skeletons = new(
        "GetOrAdd", "CSharp",
        "public int method_1(string var_1, int var_2)\n{\n    if (!var_3.ContainsKey(var_1))\n    {\n        var_3.Add(var_1, var_2);\n    }\n\n    return var_3[var_1];\n}",
        "public int method_1(string var_1, int var_2)\n{\n    lock (var_3)\n    {\n        if (!var_3.ContainsKey(var_1))\n        {\n            var_3.Add(var_1, var_2);\n        }\n\n        return var_3[var_1];\n    }\n}");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dir);
        _git = await TempGitRepo.InitAsync(_launcher, "coai-realmethod-git-");
        await _git.WriteAsync("Totals.cs", Racy);
        await _git.CommitAsync("the defect");
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _was);
        SqliteConnection.ClearAllPools();
        await _git.DisposeAsync();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>One collected pair, pointing at these two commits in this checkout.</summary>
    private long Seed(string headSha, string fixSha, string file = "Totals.cs", int line = 10, string? repoPath = null)
    {
        using var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!;
        var session = new SessionState("s1", repoPath ?? _git.Path, "main", new PanelConfig()) { Stage = Stage.CodeReview };
        var found = new Finding(
            Severity.Major, Category.Reliability, file, line, "a race", "it races", "hold the lock", ["codex"]);
        db.RecordRound(
            session,
            new RoundRecord("CodeReview", 1, "revise", 1, "all answered", new DateTime(2026, 9, 18)),
            [found],
            new RoundContext("SCOPE", headSha, "claude-code"));
        db.RecordDecisions("s1", "CodeReview", 1, [Decisions.Accept([found], 0)]);

        using var read = new SqliteConnection($"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        read.Open();
        using var one = read.CreateCommand();
        one.CommandText = "SELECT id FROM findings";
        var id = (long)one.ExecuteScalar()!;

        db.RecordCollect(id, "", "collected", "", fixSha, "run-1", Skeletons);

        return id;
    }

    /// <summary>Whatever the mode wrote to stdout.</summary>
    private static async Task<(string Out, int Code)> SpokenAsync(Func<Task<int>> mode)
    {
        var stdout = new StringWriter();
        var was = Console.Out;
        try
        {
            Console.SetOut(stdout);
            var code = await mode();

            return (stdout.ToString(), code);
        }
        finally
        {
            Console.SetOut(was);
        }
    }

    /// <summary>The mode's answer for one pair, typed.</summary>
    private static async Task<RealMethod> AnswerAsync(long findingId)
    {
        var (json, code) = await SpokenAsync(() => Program.RealMethodAsync(["--real-method", "--id", findingId.ToString()]));
        code.Should().Be(0, "a domain outcome is data on stdout, never an exit code");

        return JsonSerializer.Deserialize(json, ServerJsonContext.Default.RealMethod)!;
    }

    /// <summary>The fix, committed, and its sha.</summary>
    private async Task<string> FixAsync(string text = Fixed, string file = "Totals.cs")
    {
        await _git.WriteAsync(file, text);
        await _git.CommitAsync("hold the lock");

        return await _git.HeadAsync();
    }

    // --------------------------------------------------------------------------------------------
    // The method, un-anonymised, at both commits, with its class.
    // --------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheMethodIsReadAtBothCommits_UnAnonymised_WithItsClass()
    {
        var broken = await _git.HeadAsync();
        var fix = await FixAsync();
        var id = Seed(broken, fix);

        var real = await AnswerAsync(id);

        real.Reason.Should().BeEmpty();
        real.FindingId.Should().Be(id);
        real.Language.Should().Be("CSharp");
        real.Name.Should().Be("GetOrAdd");

        real.Before.Reason.Should().BeEmpty();
        real.Before.Source.Should().StartWith("public int GetOrAdd(string key, int value)")
            .And.Contain("_items.ContainsKey(key)", "the REAL names, not the skeleton's")
            .And.NotContain("var_1").And.NotContain("method_1");
        real.Before.ClassName.Should().Be("Totals");
        real.Before.Kind.Should().Be("method_declaration");
        real.Before.StartLine.Should().Be(7);
        real.Before.EndLine.Should().Be(15);

        real.After.Reason.Should().BeEmpty();
        real.After.Source.Should().Contain("lock (_items)", "the method at the FIX commit, not the broken one again");
        real.After.ClassName.Should().Be("Totals");
        real.After.StartLine.Should().Be(7);
        real.After.EndLine.Should().Be(18, "the lock adds a line above and a brace below");
    }

    /// <summary>
    /// The answer is read by property NAME, as the extension's reader reads it.
    /// </summary>
    /// <remarks>
    /// Through <c>JsonDocument</c> rather than the typed record, deliberately: these names are the
    /// contract <c>roundsDbRead.realOf</c> reads, and a test through the typed accessor would compile
    /// against whatever the record happened to be called.
    /// </remarks>
    [Fact]
    public async Task TheAnswerNamesItsFieldsAsTheExtensionReadsThem()
    {
        var broken = await _git.HeadAsync();
        var fix = await FixAsync();
        var id = Seed(broken, fix);

        var (json, _) = await SpokenAsync(() => Program.RealMethodAsync(["--real-method", "--id", id.ToString()]));

        using var answer = JsonDocument.Parse(json);
        var root = answer.RootElement;
        root.GetProperty("findingId").GetInt64().Should().Be(id);
        root.GetProperty("language").GetString().Should().Be("CSharp");
        root.GetProperty("name").GetString().Should().Be("GetOrAdd");
        root.GetProperty("reason").GetString().Should().BeEmpty();
        var before = root.GetProperty("before");
        before.GetProperty("reason").GetString().Should().BeEmpty();
        before.GetProperty("source").GetString().Should().Contain("_items");
        before.GetProperty("className").GetString().Should().Be("Totals");
        before.GetProperty("kind").GetString().Should().Be("method_declaration");
        before.GetProperty("startLine").GetInt32().Should().Be(7);
        before.GetProperty("endLine").GetInt32().Should().Be(15);
        root.GetProperty("after").GetProperty("source").GetString().Should().Contain("lock");
    }

    // --------------------------------------------------------------------------------------------
    // The guard that matters: a view, never a payload.
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// With the real method on the screen, the database is byte-identical and the send transmits
    /// exactly what it transmitted before — the skeleton, and nothing of the real text.
    /// </summary>
    /// <remarks>
    /// <para>Two assertions, and they are not the same test. The FILE being identical proves the
    /// read wrote nothing. The serialised <c>UploadRequest</c> being identical — through the run's
    /// own <c>Wire</c>, reached by reflection as <c>OnlyThreeFieldsLeaveTests</c> reaches it — is the
    /// plan's byte-level property: view on and view off, the bytes a send would put on the wire are
    /// the same bytes. And the third line is the leak test proper: the answer this test just read
    /// CONTAINS the real names, and the wire does not.</para>
    /// </remarks>
    [Fact]
    public async Task AReadWritesNothing_AndTheSendStillTransmitsTheSkeleton()
    {
        var broken = await _git.HeadAsync();
        var fix = await FixAsync();
        var id = Seed(broken, fix);
        using (var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!)
        {
            db.RecordKeep([new KeepDecision(id, Keep.Kept)]).Should().Be(1, "a kept pair is what a send offers");
        }

        var wireBefore = WireOfEveryKeptPair();
        var fileBefore = await File.ReadAllBytesAsync(Path.Combine(_dir, RoundsDb.FileName));

        var real = await AnswerAsync(id);

        real.Before.Source.Should().Contain("_items", "the fixture must really have put real text on the screen");
        var wireAfter = WireOfEveryKeptPair();
        var fileAfter = await File.ReadAllBytesAsync(Path.Combine(_dir, RoundsDb.FileName));

        fileAfter.Should().Equal(fileBefore, "a read must write nothing — not a row, not a column, not a stamp");
        wireAfter.Should().Be(wireBefore, "view on and view off, the serialised upload is the same bytes");
        wireAfter.Should().Contain("method_1").And.Contain("var_3", "the skeleton is what leaves");
        wireAfter.Should().NotContain("_items").And.NotContain("GetOrAdd").And.NotContain("Totals",
            "nothing the un-anonymised view showed may reach the wire");
    }

    /// <summary>The bytes a send would transmit for every kept pair, through the run's own mapping.</summary>
    private string WireOfEveryKeptPair()
    {
        using var db = RoundsDb.Open(_dir, Serilog.Core.Logger.None)!;
        var wire = typeof(UploadRun).GetMethod("Wire", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pairs = db.Sendable(UploadRun.PerBatch).Select(pair => (UploadedPair)wire.Invoke(null, [pair])!);

        return JsonSerializer.Serialize(new UploadRequest([.. pairs]), ServerJsonContext.Default.UploadRequest);
    }

    /// <summary>
    /// The real text lives on the mode's record and on nothing the send touches — pinned on the types.
    /// </summary>
    /// <remarks>
    /// The same promise <c>TheWhereAndTheWhyAreOnThePagesRecord_AndNeverOnTheSends</c> makes for the
    /// page's row, seen from the view's side: <c>StoredPair</c>, <c>UploadedPair</c> and the
    /// signature of <c>UploadRun.Wire</c> are exactly what they were, and none of them can carry a
    /// <c>Source</c> or a <c>ClassName</c>.
    /// </remarks>
    [Fact]
    public void TheRealTextIsOnTheModesRecord_AndOnNothingTheSendTouches()
    {
        string[] shown = ["Source", "ClassName", "Kind", "StartLine", "EndLine"];
        typeof(MethodSide).GetProperties().Select(one => one.Name).Should().Contain(shown);
        typeof(RealMethod).GetProperties().Select(one => one.Name).Should().Contain(["Before", "After", "Name"]);

        typeof(StoredPair).GetProperties().Select(one => one.Name).Should().NotContain(shown);
        typeof(UploadedPair).GetProperties().Select(one => one.Name).Should().NotContain(shown);
        typeof(UploadRun).GetMethod("Wire", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetParameters().Select(one => one.ParameterType)
            .Should().Equal([typeof(StoredPair)], "the send still projects the narrow record and nothing else");
        typeof(RoundsDb).GetMethod(nameof(RoundsDb.Sendable))!.ReturnType
            .Should().Be(typeof(IReadOnlyList<StoredPair>));
    }

    // --------------------------------------------------------------------------------------------
    // The repository has changed since: each way is an answer naming which.
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// A head commit the repository never had says so — and the AFTER side still answers, because it
    /// is found by the NAME the pair stores, not by anything the before side read.
    /// </summary>
    [Fact]
    public async Task AHeadCommitTheRepositoryNeverHad_SaysSo_AndTheAfterSideStillAnswers()
    {
        var fix = await FixAsync();
        var id = Seed("0123456789abcdef0123456789abcdef01234567", fix);

        var real = await AnswerAsync(id);

        real.Reason.Should().BeEmpty("the checkout is there and the language is read; the sides speak for themselves");
        real.Before.Reason.Should().Be(RealMethodReason.CommitUnreachable);
        real.Before.Source.Should().BeEmpty();
        real.After.Reason.Should().BeEmpty("one side being gone does not take the other with it");
        real.After.Source.Should().Contain("lock (_items)");
    }

    /// <summary>
    /// An orphaned head — squash-merged, branch deleted, no ref reaches it — still reads, because its
    /// object survives. 55.7 % of recorded heads are orphaned and 99.6 % of those still read.
    /// </summary>
    [Fact]
    public async Task AnOrphanedHeadWhoseObjectSurvives_StillReads()
    {
        await _git.GitAsync("checkout", "-b", "feature");
        await _git.WriteAsync("Totals.cs", Racy.Replace("int value)", "int value) // round one", StringComparison.Ordinal));
        await _git.CommitAsync("the round the reviewers read");
        var broken = await _git.HeadAsync();
        var fix = await FixAsync();
        await _git.GitAsync("checkout", "main");
        await _git.GitAsync("merge", "--squash", "feature");
        await _git.GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "squashed");
        await _git.GitAsync("branch", "-D", "feature");
        (await _git.RunAsync("for-each-ref", "--format=%(refname)", "--contains", broken))
            .StdOut.Trim().Should().BeEmpty("the fixture must actually produce an orphan");
        var id = Seed(broken, fix);

        var real = await AnswerAsync(id);

        real.Before.Reason.Should().BeEmpty("the object survives, so the method at it still reads");
        real.Before.Source.Should().Contain("// round one", "and it is the method at THAT commit");
        real.After.Source.Should().Contain("lock (_items)");
    }

    /// <summary>The ambiguous guard: two functions of one name at the fix are refused rather than guessed between.</summary>
    [Fact]
    public async Task TwoFunctionsOfOneNameAtTheFix_AreRefusedRatherThanGuessed()
    {
        var broken = await _git.HeadAsync();
        var fix = await FixAsync(Fixed.Replace(
            "    public int GetOrAdd(string key, int value)",
            "    public int GetOrAdd(string key) => _items[key];\n\n    public int GetOrAdd(string key, int value)",
            StringComparison.Ordinal));
        var id = Seed(broken, fix);

        var real = await AnswerAsync(id);

        real.Before.Reason.Should().BeEmpty("the broken commit has one GetOrAdd");
        real.After.Reason.Should().Be(RealMethodReason.SymbolAmbiguous);
        real.After.Source.Should().BeEmpty("showing the wrong overload would put somebody else's method under this heading");
    }

    /// <summary>The no-match guard: a function renamed away at the fix is gone, not the nearest thing.</summary>
    [Fact]
    public async Task AFunctionRenamedAwayAtTheFix_IsGone()
    {
        var broken = await _git.HeadAsync();
        var fix = await FixAsync(Fixed.Replace("GetOrAdd", "GetOrInsert", StringComparison.Ordinal));
        var id = Seed(broken, fix);

        var real = await AnswerAsync(id);

        real.After.Reason.Should().Be(RealMethodReason.SymbolGone);
        real.After.Source.Should().BeEmpty();
    }

    /// <summary>
    /// The nameless guard: a line inside a lambda resolves to a function with no name, and a function
    /// with no name cannot be found again — so it is refused before anything is compared.
    /// </summary>
    /// <remarks>
    /// Through the reader rather than the mode, because a nameless function was never collected and
    /// so has no row to seed; the guard is the reader's and this is where it lives.
    /// </remarks>
    [Fact]
    public async Task ALineInsideANamelessFunction_IsNotResolved()
    {
        await _git.WriteAsync("total.ts", "export const total = (entries: number[]) => {\n  return entries.length;\n};\n");
        await _git.CommitAsync("a lambda");
        var sha = await _git.HeadAsync();
        var reader = new RealMethodReader(new GitHistory(_launcher), new TreeSitterNormalizer());

        var real = await reader.ReadAsync(1, new MethodPlace(_git.Path, sha, sha, "total.ts", 2, ""));

        real.Before.Reason.Should().Be(RealMethodReason.SymbolNotResolved);
        real.Before.Source.Should().BeEmpty("a lambda's text must not be shown as if it were a named method");
    }

    /// <summary>
    /// A fix commit where the file is not under the finding's path any more says so — and that is a
    /// row the collector cannot produce, which is worth knowing rather than guarding against.
    /// </summary>
    /// <remarks>
    /// <para>Measured while this story was built, on real git: <c>git log --follow head..fix -- Totals.cs</c>
    /// lists the RENAME commit under the OLD name and nothing after it — following works backwards
    /// from the newest name, and the walk is handed the oldest. So <c>Collector.WalkAsync</c> reads
    /// <c>fix:Totals.cs</c>, fails, continues, and records <c>symbol_gone</c>; a pair whose fix
    /// commit renamed the file is never stored. This reader therefore does NOT follow renames: the
    /// only rows that reach it have the file under the finding's path at both commits, and a
    /// recovery for the other shape would be code no stored pair reaches. (The first draft had one,
    /// and this fixture is what showed it to be unreachable.)</para>
    /// <para>What the answer IS for such a row, should one ever be seeded by hand, is
    /// <c>file_not_in_commit</c> — true of the path a person can see.</para>
    /// </remarks>
    [Fact]
    public async Task AFixThatRenamedTheFile_SaysTheFileIsNotInTheCommit()
    {
        var broken = await _git.HeadAsync();
        await _git.GitAsync("mv", "Totals.cs", "Sums.cs");
        var fix = await FixAsync(file: "Sums.cs");
        var id = Seed(broken, fix);

        var real = await AnswerAsync(id);

        real.Before.Source.Should().Contain("_items.ContainsKey(key)", "the before side is untouched by the rename");
        real.After.Reason.Should().Be(RealMethodReason.FileNotInCommit);
    }

    [Fact]
    public async Task ACheckoutThatIsGone_SaysSo_OnBothSides()
    {
        var broken = await _git.HeadAsync();
        var fix = await FixAsync();
        var id = Seed(broken, fix, repoPath: Path.Combine(Path.GetTempPath(), $"gone-{Guid.NewGuid():N}"));

        var real = await AnswerAsync(id);

        real.Reason.Should().Be(RealMethodReason.RepoPathMissing);
        real.Before.Reason.Should().Be(RealMethodReason.RepoPathMissing);
        real.After.Reason.Should().Be(RealMethodReason.RepoPathMissing);
        real.Name.Should().Be("GetOrAdd", "what the pair stores is still said, so the page can name the row");
    }

    [Fact]
    public async Task AFileNotInTheCommit_SaysSo()
    {
        var broken = await _git.HeadAsync();
        var fix = await FixAsync();
        var id = Seed(broken, fix, file: "Elsewhere.cs");

        var real = await AnswerAsync(id);

        real.Reason.Should().BeEmpty();
        real.Before.Reason.Should().Be(RealMethodReason.FileNotInCommit);
        real.After.Reason.Should().Be(RealMethodReason.FileNotInCommit);
    }

    [Fact]
    public async Task ALanguageWeDoNotRead_SaysSo()
    {
        var broken = await _git.HeadAsync();
        var id = Seed(broken, broken, file: "notes.md", line: 1);

        var real = await AnswerAsync(id);

        real.Reason.Should().Be(RealMethodReason.LanguageUnsupported);
        real.Before.Reason.Should().Be(RealMethodReason.LanguageUnsupported);
    }

    /// <summary>A pair the database does not have is an ANSWER, exit 0 — recollected under the page, or never there.</summary>
    [Fact]
    public async Task APairTheDatabaseDoesNotHave_IsAnAnswer_NotAnExitCode()
    {
        var real = await AnswerAsync(999_999);

        real.FindingId.Should().Be(999_999);
        real.Reason.Should().Be(RealMethodReason.PairNotFound);
        real.Before.Reason.Should().Be(RealMethodReason.PairNotFound);
        real.After.Reason.Should().Be(RealMethodReason.PairNotFound);
    }

    // --------------------------------------------------------------------------------------------
    // The exit-code contract: 64 means "never heard of the mode", and this binary has heard of it.
    // --------------------------------------------------------------------------------------------

    /// <summary>A request fault is 65 — never 64, which is how the extension detects a server too old for the view.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("--id")]
    [InlineData("--id abc")]
    [InlineData("--id -1")]
    [InlineData("--id 1.5")]
    public async Task ABadIdIs65_Never64(string rest)
    {
        string[] args = ["--real-method", .. rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

        var (_, code) = await SpokenAsync(() => Program.RealMethodAsync(args));

        code.Should().Be(65, "64 is reserved for a mode this binary does not have, and it has this one");
    }

    [Fact]
    public void TheModeHasItsOwnArgument()
    {
        Program.Classify(["--real-method"]).Should().Be(Program.Startup.RealMethod);
        Program.Classify(["--real-methods"]).Should().Be(Program.Startup.Usage, "a near miss is refused, not guessed");
    }
}
