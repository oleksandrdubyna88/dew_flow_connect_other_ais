using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A live code round measures itself (E2.S4 of <c>research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E2.md</c>): its
/// <see cref="RoundSize"/> is computed from the diff the round built and shaped, rides out of <c>MakeWork</c> beside the resolved
/// base, and finishes on the record — the session file and <c>round_changes</c> — while a size that cannot be measured never
/// touches the round.
/// </summary>
/// <remarks>
/// <para>Driven through the real <see cref="PanelService"/> with FakeCli and real git, as
/// <see cref="ARoundIsMintedAKeyTests"/> is, because the size is assembled in the call site, carried by the work, set on the
/// record by the engine and written by the projection — no unit of the four can prove the four agree. The failure paths drive
/// <see cref="RoundSizer"/> directly with a launcher that sabotages ONE git call, and then the whole round with the same one.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ACodeRoundRecordsItsSizeTests : IAsyncLifetime
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

    private const string OneMajor = """
        {"findings": [
          {"severity": "major", "category": "reliability", "file": "app.cs", "line": 10,
           "title": "the retry is missing", "why": "the call has no retry at all", "fix": "retry once"}
        ]}
        """;

    private const string Branch = "feature";
    private const string Code = nameof(Stage.CodeReview);

    private readonly ProcessLauncher _launcher = new();
    private readonly List<string> _scratch = [];
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-size-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-size-data-").FullName;
        await Git("init", "-b", "main");
        await Write("app.cs", "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", Branch);
        await Write("app.cs", string.Concat(Enumerable.Repeat("line\n", 100)));
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

        SqliteConnection.ClearAllPools();
        foreach (var dir in (string[])[_repo, _data, .. _scratch])
        {
            Remove(dir);
        }

        return ValueTask.CompletedTask;
    }

    private static void Remove(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not a failing test (git objects are read-only on Windows).
        }
    }

    // ---------- the scaffolding: git, files, the service ----------

    private static void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    private async Task<string> GitIn(string dir, params string[] args)
    {
        var result = await _launcher.RunAsync(
            new ProcessRequest(
                "git",
                ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "-c", "protocol.file.allow=always", .. args],
                dir),
            Ct);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

        return result.StdOut.Trim();
    }

    private Task<string> Git(params string[] args) => GitIn(_repo, args);

    private Task Write(string path, string text) => File.WriteAllTextAsync(Path.Combine(_repo, path), text, Ct);

    private async Task CommitAll(string message)
    {
        await Git("add", ".");
        await Git("commit", "-m", message);
    }

    private PanelService Service(IProcessLauncher? launcher = null, string? data = null) =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = data ?? _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            launcher ?? _launcher,
            Logger.None, Noticing.None);

    private async Task<PanelService> Opened(string branch = Branch, IProcessLauncher? launcher = null)
    {
        var service = Service(launcher);
        await service.OpenAsync(_repo, branch);
        Script(Clean);
        await service.ReviewPlanAsync(_repo, branch, "a plan nobody objects to");
        await service.ResolveAsync(_repo, branch, "[]");

        return service;
    }

    private async Task<JsonElement> CodeRound(PanelService service, string branch = Branch, bool again = false, string answer = Clean)
    {
        Script(answer);
        var reply = JsonDocument.Parse(await service.ReviewCodeAsync(_repo, branch, "main", Scope, again)).RootElement;
        reply.TryGetProperty("error", out var error).Should().BeFalse($"the round ran: {error}");

        return reply;
    }

    /// <summary>The newest code round in the session file — the record the engine finished.</summary>
    private RoundRecord Latest(string branch = Branch) =>
        new SessionStore(_data).Load(_repo, branch)!.Rounds.Last(r => r.Stage == Code);

    private string Database(string? data = null) => Path.Combine(data ?? _data, Store.RoundsDb.FileName);

    private List<string> Query(string sql, string? data = null)
    {
        using var db = new SqliteConnection($"Data Source={Database(data)};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var rows = read.ExecuteReader();
        var found = new List<string>();
        while (rows.Read())
        {
            found.Add(string.Join('|', Enumerable.Range(0, rows.FieldCount).Select(i => rows.IsDBNull(i) ? "NULL" : Convert.ToString(rows.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return found;
    }

    private const string CellsOfTheLatestCodeRound = """
        SELECT c.scope, c.coverage, c.kind, c.files, c.added, c.removed
        FROM round_changes c JOIN rounds r ON r.id = c.round_id
        WHERE r.stage = 'CodeReview' AND r.number = (SELECT MAX(number) FROM rounds WHERE stage = 'CodeReview')
        ORDER BY c.rowid
        """;

    private const string StatesOfTheLatestCodeRound = """
        SELECT size_state, delta_state, delta_from, size_origin, size_gitlinks
        FROM rounds WHERE stage = 'CodeReview' ORDER BY number DESC LIMIT 1
        """;

    private async Task<(CollectedDiff Collected, ShapedDiff Shaped, string Sha)> TheDiffOfHead()
    {
        var sha = await Git("rev-parse", "HEAD");
        var collected = await new ContextAssembler(_launcher).CollectAsync(_repo, "main", sha, ct: Ct);

        return (collected, DiffShaper.Shape(collected.Files), sha);
    }

    private static RoundSize Sized(RoundSize? size)
    {
        size.Should().NotBeNull("the round measured the diff it built — the record must carry it");

        return size!;
    }

    private static SizeCell Cell(string coverage, string kind, int files, int? added, int? removed) =>
        new(coverage, kind, files, added, removed);

    // ---------- the size of a round, end to end ----------

    [Fact]
    public async Task TheFirstCodeRound_RecordsItsWholeSize_AndNoDelta()
    {
        var service = await Opened();
        var mergeBase = await Git("merge-base", "main", "HEAD");

        await CodeRound(service);

        var size = Latest().Size;
        size.Should().NotBeNull("the round measured the diff it built — the record must carry it");
        size!.Whole.State.Should().Be(ScopeState.Recorded);
        size.Whole.From.Should().Be(mergeBase, "the whole scope starts at the resolved base the diff was taken against");
        size.Whole.Cells.Should().Equal([Cell(ChangeCoverage.Shown, ChangeKind.Code, 1, 100, 1)], "the feature commit: 100 lines in, the one base line out");
        size.Delta.State.Should().Be(ScopeState.FirstRound);
        size.Delta.Cells.Should().BeEmpty();
        Latest().Gitlinks.Should().Be(0, "no submodule: a count of zero is a measurement");
        Query(CellsOfTheLatestCodeRound).Should().Equal(["whole|shown|code|1|100|1"], "the database holds what the file holds");
        Query(StatesOfTheLatestCodeRound).Should().Equal([$"recorded|{ScopeState.FirstRound}||live|0"]);
    }

    [Fact]
    public async Task ASecondCodeRoundAfterOneCommit_RecordsTheHeadToHeadDelta()
    {
        var service = await Opened();
        await CodeRound(service);
        var first = await Git("rev-parse", "HEAD");
        await service.ResolveAsync(_repo, Branch, "[]");
        await Write("app.cs", string.Concat(Enumerable.Repeat("line\n", 100)) + "the fix\n");
        await CommitAll("the fixer's commit");

        await CodeRound(service, again: true);

        var size = Sized(Latest().Size);
        size.Delta.State.Should().Be(ScopeState.Recorded);
        size.Delta.From.Should().Be(first, "the delta starts at the round that was read last");
        size.Delta.Cells.Should().Equal([Cell(ChangeCoverage.Shown, ChangeKind.Code, 1, 1, 0)], "the fix commit's own numstat — one line in");
        size.Whole.Cells.Should().Equal([Cell(ChangeCoverage.Shown, ChangeKind.Code, 1, 101, 1)], "the whole is still the cumulative diff");
        Query(CellsOfTheLatestCodeRound).Should().Equal(["whole|shown|code|1|101|1", "delta|shown|code|1|1|0"]);
        Query(StatesOfTheLatestCodeRound).Should().Equal([$"recorded|recorded|{first}|live|0"]);
    }

    [Fact]
    public async Task ASkippedRoundIsNeverThePredecessor()
    {
        var service = await Opened();
        await CodeRound(service);
        var ran = await Git("rev-parse", "HEAD");
        await service.ResolveAsync(_repo, Branch, "[]");
        await Write("app.cs", string.Concat(Enumerable.Repeat("line\n", 100)) + "the unreviewed commit\n");
        await CommitAll("a commit only a skipped round saw");
        var skippedAt = await Git("rev-parse", "HEAD");
        TheSkippedRound(skippedAt);
        await Write("app.cs", string.Concat(Enumerable.Repeat("line\n", 100)) + "the unreviewed commit\nand one more\n");
        await CommitAll("the commit this round reads");

        await CodeRound(service, again: true);

        Sized(Latest().Size).Delta.From.Should().Be(ran, "a skipped round is written done with a sha but reviewed nothing — the delta starts at the round that ran");
        Sized(Latest().Size).Delta.Cells.Should().Equal([Cell(ChangeCoverage.Shown, ChangeKind.Code, 1, 2, 0)], "both later commits are the fixers' work");
    }

    /// <summary>A code round written the way the engine writes a skip (<c>RoundSkips</c>): done, <c>skipped</c>, and WITH a sha.</summary>
    private void TheSkippedRound(string sha)
    {
        var store = new SessionStore(_data);
        var loaded = store.Load(_repo, Branch)!;
        var now = DateTime.UtcNow;
        loaded.Rounds.Add(new RoundRecord(Code, loaded.Rounds.Count + 1, RoundRecord.Skipped, 0, "nobody ran", now)
        {
            Status = RoundRecord.Done,
            StartedUtc = now,
            Sha = sha,
            Note = "no vendor is ticked",
        });
        store.Save(loaded);
    }

    [Fact]
    public async Task AMergeOfMainBetweenRounds_IsBaseMoved_NotFixerWork()
    {
        var service = await Opened();
        await CodeRound(service);
        var first = await Git("rev-parse", "HEAD");
        await service.ResolveAsync(_repo, Branch, "[]");
        await Git("checkout", "main");
        await Write("main-only.cs", string.Concat(Enumerable.Repeat("main's own line\n", 30)));
        await CommitAll("main moves on");
        await Git("checkout", Branch);
        await Git("merge", "main", "--no-edit", "-m", "merge main into the branch");

        await CodeRound(service, again: true);

        var size = Sized(Latest().Size);
        size.Delta.State.Should().Be(ScopeState.BaseMoved, "main's 30 lines would be counted as fixer work");
        size.Delta.From.Should().Be(first);
        size.Delta.Cells.Should().BeEmpty();
        size.Whole.State.Should().Be(ScopeState.Recorded);
        size.Whole.Cells.Should().Equal([Cell(ChangeCoverage.Shown, ChangeKind.Code, 1, 100, 1)], "the whole is measured from the moved merge-base — main's commit is not in it");
        Query(StatesOfTheLatestCodeRound).Should().Equal([$"recorded|{ScopeState.BaseMoved}|{first}|live|0"]);
        Query(CellsOfTheLatestCodeRound).Should().Equal(["whole|shown|code|1|100|1"], "no delta rows");
    }

    [Fact]
    public async Task ARoundWithNoCommonAncestor_IsUnavailableInBothScopes()
    {
        await Git("checkout", "--orphan", "lone");
        await Git("rm", "-rf", ".");
        await Write("solo.cs", string.Concat(Enumerable.Repeat("a line of an unrelated history\n", 20)));
        await CommitAll("an unrelated root");
        var control = await RunTheFirstRound("lone", OneMajor);
        var service = await Opened("lone");

        var reply = await CodeRound(service, "lone", answer: OneMajor);

        var size = Sized(Latest("lone").Size);
        size.Whole.State.Should().Be(ScopeState.NoMergeBase, "the base's tip is not a merge-base: its commits would be counted as this branch's deletions");
        size.Delta.State.Should().Be(ScopeState.NoMergeBase);
        size.Whole.Cells.Should().BeEmpty();
        size.Delta.Cells.Should().BeEmpty();
        Query(StatesOfTheLatestCodeRound).Should().Equal([$"{ScopeState.NoMergeBase}|{ScopeState.NoMergeBase}||live|-1"]);
        Query("SELECT COUNT(*) FROM round_changes").Should().Equal(["0"], "an unavailable size writes no rows — never a zero-line change");
        reply.GetProperty("verdict").GetString().Should().Be(control.Verdict, "the size is a view of the round, never a part of its outcome");
        reply.GetProperty("findings").GetRawText().Should().Be(control.Findings);
    }

    [Fact]
    public async Task ALockFileAndAnImage_AreCountedApart()
    {
        await Write("package-lock.json", string.Concat(Enumerable.Repeat("{\"locked\": true}\n", 7)));
        await File.WriteAllBytesAsync(Path.Combine(_repo, "logo.png"), [0, 1, 2, 0, 255, 0, 7], Ct);
        await CommitAll("a lock file and an image");
        var service = await Opened();

        await CodeRound(service);

        var cells = Sized(Latest().Size).Whole.Cells;
        cells.Should().ContainSingle(c => c.Coverage == ChangeCoverage.Shown).Which
            .Should().Be(Cell(ChangeCoverage.Shown, ChangeKind.Code, 1, 100, 1), "neither the lock file nor the image is in what the reviewers read");
        cells.Should().ContainSingle(c => c.Coverage == ChangeCoverage.Excluded).Which
            .Should().Be(Cell(ChangeCoverage.Excluded, ChangeKind.Of("package-lock.json"), 1, 7, 0), "the lock file's lines are counted, apart");
        cells.Should().ContainSingle(c => c.Coverage == ChangeCoverage.Binary).Which
            .Should().Be(Cell(ChangeCoverage.Binary, ChangeKind.Of("logo.png"), 1, null, null), "an image has no line count — null, never 0");
        Query(CellsOfTheLatestCodeRound).Should().Contain(row => row.EndsWith("|NULL|NULL", StringComparison.Ordinal), "and the table holds NULL for it");
    }

    [Fact]
    public async Task AnElidedFile_IsRecordedElided()
    {
        await Write("big.cs", string.Concat(Enumerable.Repeat("int padding = 1234567; // fills the diff budget one line at a time\n", 4000)));
        await CommitAll("a file past the diff budget");
        var service = await Opened();

        await CodeRound(service);

        var cells = Sized(Latest().Size).Whole.Cells;
        cells.Should().Equal(
            [
                Cell(ChangeCoverage.Shown, ChangeKind.Code, 1, 100, 1),
                Cell(ChangeCoverage.Elided, ChangeKind.Code, 1, 4000, 0),
            ], "the reviewers were told the big file was over budget — elided, with its lines still counted");
    }

    [Fact]
    public async Task APlanRound_HasNoSize()
    {
        var service = Service();
        await service.OpenAsync(_repo, Branch);
        Script(Clean);
        await service.ReviewPlanAsync(_repo, Branch, "a plan nobody objects to");

        new SessionStore(_data).Load(_repo, Branch)!.Rounds.Should().ContainSingle().Which.Size.Should().BeNull("a plan round compares nothing");
        Query("SELECT COUNT(*) FROM round_changes").Should().Equal(["0"]);
        Query("SELECT size_state || '|' || size_gitlinks FROM rounds").Should().Equal(["|-1"]);
    }

    // ---------- a size that cannot be measured never touches the round ----------

    [Fact]
    public async Task ASizeThatCannotBeMeasured_DoesNotTouchTheRound()
    {
        var (collected, shaped, sha) = await TheDiffOfHead();

        var failed = await Measure(Sabotage(Unfiltered, () => new ProcessResult(128, string.Empty, "fatal: bad object", TimedOut: false)), collected, shaped, sha);
        var thrown = await Measure(Sabotage(Unfiltered, () => throw new InvalidOperationException("the process could not start")), collected, shaped, sha);

        var failedSize = Sized(failed.Size);
        failedSize.Whole.State.Should().Be(ScopeState.GitFailed, "the unfiltered numstat exited 128");
        failedSize.Whole.From.Should().Be(collected.ComparedAgainst, "the base was chosen before the failure");
        failedSize.Whole.Cells.Should().BeEmpty();
        failedSize.Delta.State.Should().Be(ScopeState.FirstRound, "the other scope is measured on its own");
        Sized(thrown.Size).Whole.State.Should().Be(ScopeState.GitFailed, "an exception from a size call becomes a state — it never reaches the round");

        var control = await RunTheFirstRound(Branch, OneMajor);
        var sabotaged = await RunTheFirstRound(Branch, OneMajor, Sabotage(Unfiltered, () => throw new InvalidOperationException("the process could not start")));
        sabotaged.Verdict.Should().Be(control.Verdict, "a size that could not be taken is not a reason for a different verdict");
        sabotaged.Findings.Should().Be(control.Findings);
        Sized(sabotaged.Latest.Size).Whole.State.Should().Be(ScopeState.GitFailed, "and the record says why there is no size");
    }

    [Fact]
    public async Task ASizingThatOutlastsItsDeadline_DoesNotTouchTheRound()
    {
        var control = await RunTheFirstRound(Branch, OneMajor);
        var stuck = new Delaying(_launcher, args => Unfiltered(args) || Raw(args), RoundSizer.SizeTotalTimeout * 3);

        var stalled = await RunTheFirstRound(Branch, OneMajor, stuck);

        var size = Sized(stalled.Latest.Size);
        size.Whole.State.Should().Be(ScopeState.GitFailed, "the whole sizing has one deadline of its own, and the numstat was still running");
        stalled.Latest.Gitlinks.Should().BeNull("and so was the gitlink count");
        stalled.Verdict.Should().Be(control.Verdict, "a size that ran out of time is not a reason for a different verdict");
        stalled.Findings.Should().Be(control.Findings);
    }

    [Fact]
    public async Task ANumstatDisagreement_IsAStateAndAWarning()
    {
        var (collected, shaped, sha) = await TheDiffOfHead();
        var sink = new ListSink();
        var log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        var lacksTheReviewableFile = Sabotage(Unfiltered, () => new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));

        var measured = await Measure(lacksTheReviewableFile, collected, shaped, sha, log);

        Sized(measured.Size).Whole.State.Should().Be(ScopeState.NumstatsDisagree);
        var warnings = sink.Events.Where(e => e.Level == LogEventLevel.Warning).Select(e => e.RenderMessage()).ToList();
        warnings.Should().ContainSingle().Which.Should().Contain("a reviewable file is not in the unfiltered numstat", "the fixed sentence");
        warnings.Single().Should().NotContain("app.cs", "no path in a log line");

        var control = await RunTheFirstRound(Branch, OneMajor);
        var disagreeing = await RunTheFirstRound(Branch, OneMajor, lacksTheReviewableFile);
        disagreeing.Verdict.Should().Be(control.Verdict);
        disagreeing.Findings.Should().Be(control.Findings, "the round's findings are unchanged by a size that disagrees with itself");
        Sized(disagreeing.Latest.Size).Whole.State.Should().Be(ScopeState.NumstatsDisagree);
    }

    // ---------- submodule pointers ----------

    [Fact]
    public async Task AChangedSubmodulePointer_IsCountedAsAGitlink()
    {
        await TheBranchMovesASubmodule();
        var service = await Opened();

        await CodeRound(service);

        Latest().Gitlinks.Should().Be(1, "one submodule pointer moved");
        Sized(Latest().Size).Whole.Cells.Should().ContainSingle(c => c.Kind == ChangeKind.Code).Which
            .Added.Should().Be(100, "the pointer's own line is in no code cell");
        Query(StatesOfTheLatestCodeRound).Should().Equal([$"recorded|{ScopeState.FirstRound}||live|1"]);

        var (collected, shaped, sha) = await TheDiffOfHead();
        var rawFails = await Measure(Sabotage(Raw, () => new ProcessResult(128, string.Empty, "fatal", TimedOut: false)), collected, shaped, sha);
        rawFails.Gitlinks.Should().BeNull("a failed --raw leaves the count unrecorded");
        Sized(rawFails.Size).Whole.State.Should().Be(ScopeState.Recorded, "and the size as measured");
    }

    private async Task TheBranchMovesASubmodule()
    {
        var sub = Directory.CreateTempSubdirectory("coai-size-sub-").FullName;
        _scratch.Add(sub);
        await GitIn(sub, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(sub, "lib.cs"), "one\n", Ct);
        await GitIn(sub, "add", ".");
        await GitIn(sub, "commit", "-m", "c1");
        var c1 = await GitIn(sub, "rev-parse", "HEAD");
        await File.WriteAllTextAsync(Path.Combine(sub, "lib.cs"), "one\ntwo\n", Ct);
        await GitIn(sub, "commit", "-am", "c2");
        var c2 = await GitIn(sub, "rev-parse", "HEAD");

        await Git("checkout", "main");
        await Git("submodule", "add", sub, "libs/sub");
        await GitIn(Path.Combine(_repo, "libs", "sub"), "checkout", c1);
        await CommitAll("add the submodule at c1");
        await Git("checkout", Branch);
        await Git("rebase", "main");
        await GitIn(Path.Combine(_repo, "libs", "sub"), "checkout", c2);
        await CommitAll("move the submodule to c2");
    }

    // ---------- helpers for the failure paths ----------

    private static bool Unfiltered(IReadOnlyList<string> args) => args.Contains("--numstat") && !args.Contains("--");

    private static bool Raw(IReadOnlyList<string> args) => args.Contains("--raw");

    private Sabotaged Sabotage(Func<IReadOnlyList<string>, bool> hits, Func<ProcessResult> answer) => new(_launcher, hits, answer);

    private Task<MeasuredSize> Measure(
        IProcessLauncher launcher, CollectedDiff collected, ShapedDiff shaped, string sha, Serilog.ILogger? log = null) =>
        RoundSizer.MeasureAsync(new ContextAssembler(launcher), _repo, sha, collected, shaped, [], log ?? Logger.None, Ct);

    /// <summary>What a first code round answers and records, under a launcher (or the real one), in a data directory of its own.</summary>
    private async Task<(string Verdict, string Findings, RoundRecord Latest)> RunTheFirstRound(
        string branch, string answer, IProcessLauncher? launcher = null)
    {
        var data = Directory.CreateTempSubdirectory("coai-size-run-").FullName;
        _scratch.Add(data);
        var service = Service(launcher, data);
        await service.OpenAsync(_repo, branch);
        Script(Clean);
        await service.ReviewPlanAsync(_repo, branch, "a plan nobody objects to");
        await service.ResolveAsync(_repo, branch, "[]");
        Script(answer);
        var reply = JsonDocument.Parse(await service.ReviewCodeAsync(_repo, branch, "main", Scope)).RootElement;
        SqliteConnection.ClearAllPools();

        return (
            reply.GetProperty("verdict").GetString()!,
            reply.GetProperty("findings").GetRawText(),
            new SessionStore(data).Load(_repo, branch)!.Rounds.Last(r => r.Stage == Code));
    }

    /// <summary>The real launcher, except that the git call <paramref name="hits"/> picks is held for <paramref name="hold"/> first (honouring the token).</summary>
    private sealed class Delaying(IProcessLauncher inner, Func<IReadOnlyList<string>, bool> hits, TimeSpan hold) : IProcessLauncher
    {
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (request.Executable == "git" && hits(request.Arguments))
            {
                await Task.Delay(hold, ct);
            }

            return await inner.RunAsync(request, ct);
        }
    }

    /// <summary>The real launcher, except that the git call <paramref name="hits"/> picks is answered by <paramref name="answer"/>.</summary>
    private sealed class Sabotaged(IProcessLauncher inner, Func<IReadOnlyList<string>, bool> hits, Func<ProcessResult> answer) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            request.Executable == "git" && hits(request.Arguments)
                ? Task.FromResult(answer())
                : inner.RunAsync(request, ct);
    }
}
