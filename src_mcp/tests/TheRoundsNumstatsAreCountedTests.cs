using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E2.S2 — the numstats a round's size is made of, read from real git: the round's own counted, the unfiltered whole,
/// and the delta range between two heads. None of them may throw for a git failure, and none may run unbounded.
/// </summary>
public sealed class TheRoundsNumstatsAreCountedTests : IAsyncLifetime
{
    /// <summary>Enough lines that git's rename detection fires on a small edit (see <c>ContextAssemblerRenameTests</c>).</summary>
    private const int LinesEnoughToBeRecognisedAsARename = 40;

    private readonly RecordingLauncher _launcher = new(new ProcessLauncher());
    private readonly ContextAssembler _assembler;
    private string _repo = string.Empty;

    public TheRoundsNumstatsAreCountedTests() => _assembler = new ContextAssembler(_launcher);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-numstat-").FullName;
        await Git("init", "-b", "main");
        await Write("app.cs", "v1\n");
        await Write(".gitignore", "ignored/\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");

        // The branch changes real code, a lock file, a dist artefact and a binary.
        await Write("app.cs", "v2 with a real change\n");
        await Write("package-lock.json", "{\"locked\": true}\n");
        await Write("dist/bundle.js", "minified noise\n");
        await File.WriteAllBytesAsync(Path.Combine(_repo, "logo.png"), [0, 1, 2, 0, 255, 0, 7], Ct);
        await Git("add", ".");
        await Git("commit", "-m", "feature work");
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_repo, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }

    private async Task<string> Git(params string[] args)
    {
        var result = await new ProcessLauncher().RunAsync(
            new ProcessRequest("git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo), Ct);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

        return result.StdOut.Trim();
    }

    private Task Write(string path, string text)
    {
        var full = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        return File.WriteAllTextAsync(full, text, Ct);
    }

    /// <summary>Stage everything, commit, and answer the new commit's sha.</summary>
    private async Task<string> Commit(string message)
    {
        await Git("add", ".");
        await Git("commit", "-m", message);

        return await Git("rev-parse", "HEAD");
    }

    private static string Lines(string prefix, int from, int to) =>
        string.Concat(Enumerable.Range(from, to - from + 1).Select(i => $"{prefix}{i}\n"));

    private static string Shown(CountedChange c) => $"{c.Change.Path} +{c.Added} -{c.Deleted}";

    [Fact]
    public async Task TheRoundsOwnNumstat_KeepsItsCounts_AndTheFilesAreUnchanged()
    {
        var collected = await _assembler.CollectAsync(_repo, "main", "feature", ct: Ct);

        collected.Files.Select(f => f.Path).Should().Equal("app.cs", "logo.png");
        collected.Counted.Select(c => c.Change.Path).Should().Equal(collected.Files.Select(f => f.Path), "the counts ride the same files in the same order");
        collected.Counted.Select(Shown).Should().Equal(["app.cs +1 -1", "logo.png +0 -0"], "a binary has no count, and is read as 0 here");
        collected.Counted[1].Change.IsBinary.Should().BeTrue();
        collected.Files[0].Text.Should().Contain("v2 with a real change", "every file's diff is still collected as before");
    }

    [Fact]
    public async Task TheUnfilteredNumstat_NamesWhatTheExclusionsHid()
    {
        var collected = await _assembler.CollectAsync(_repo, "main", "feature", ct: Ct);
        var sha = await Git("rev-parse", "feature");

        var all = await _assembler.AllChangesAsync(_repo, collected.ComparedAgainst, sha, Ct);

        all.Failed.Should().BeFalse();
        all.Changes.Select(c => c.Change.Path).Should().BeEquivalentTo(["app.cs", "dist/bundle.js", "logo.png", "package-lock.json"]);
        collected.Counted.Select(c => c.Change.Path).Should().NotContain(["dist/bundle.js", "package-lock.json"]);
        all.Changes.Single(c => c.Change.Path == "logo.png").Change.IsBinary.Should().BeTrue("a binary is a binary in both numstats");
        collected.Counted.Single(c => c.Change.Path == "logo.png").Change.IsBinary.Should().BeTrue();
        all.Changes.Single(c => c.Change.Path == "dist/bundle.js").Added.Should().Be(1);
    }

    [Fact]
    public async Task ARename_ArrivesOnceUnderItsNewPathWithTheEditsCounts()
    {
        await Git("checkout", "main");
        await Write("src/old.cs", Lines("    var line", 0, LinesEnoughToBeRecognisedAsARename - 1));
        await Commit("second base");
        await Git("checkout", "-b", "renamer");
        await Git("mv", "src/old.cs", "src/new.cs");
        await File.AppendAllTextAsync(Path.Combine(_repo, "src", "new.cs"), "    var addedByTheBranch = 1;\n", Ct);
        var sha = await Commit("rename with an edit");

        var collected = await _assembler.CollectAsync(_repo, "main", sha, ct: Ct);
        var all = await _assembler.AllChangesAsync(_repo, collected.ComparedAgainst, sha, Ct);

        foreach (var counted in (IReadOnlyList<CountedChange>[])[collected.Counted, all.Changes])
        {
            var renamed = counted.Should().ContainSingle(c => c.Change.RenamedFrom.Length > 0).Subject;
            renamed.Change.Path.Should().Be("src/new.cs");
            renamed.Change.RenamedFrom.Should().Be("src/old.cs");
            (renamed.Added, renamed.Deleted).Should().Be((1, 0), "the edit's lines, not the whole file as added and deleted");
            counted.Should().NotContain(c => c.Change.Path == "src/old.cs", "the old name does not arrive as a second file");
        }
    }

    [Fact]
    public async Task ARenameAcrossTheExclusionBoundary_IsClassifiedBothWays()
    {
        await Git("checkout", "main");
        await Write("src/a.js", Lines("  var a", 0, LinesEnoughToBeRecognisedAsARename - 1));
        await Write("dist/b.js", Lines("  var b", 0, LinesEnoughToBeRecognisedAsARename - 1));
        await Commit("second base");
        await Git("checkout", "-b", "boundary");
        await Git("mv", "src/a.js", "dist/a.js");
        await Git("mv", "dist/b.js", "src/b.js");
        await File.AppendAllTextAsync(Path.Combine(_repo, "dist", "a.js"), "  var moved = 1;\n", Ct);
        await File.AppendAllTextAsync(Path.Combine(_repo, "src", "b.js"), "  var moved = 2;\n", Ct);
        var sha = await Commit("renames across the exclusion boundary");

        var collected = await _assembler.CollectAsync(_repo, "main", sha, ct: Ct);
        var all = await _assembler.AllChangesAsync(_repo, collected.ComparedAgainst, sha, Ct);

        // What git measured: INTO an excluded path the filtered numstat sees a plain deletion of the old name,
        // OUT of one a plain addition of the new — and the unfiltered numstat pairs both as renames.
        collected.Counted.Select(Shown).Should().BeEquivalentTo(
            [$"src/a.js +0 -{LinesEnoughToBeRecognisedAsARename}", $"src/b.js +{LinesEnoughToBeRecognisedAsARename + 1} -0"]);
        all.Changes.Should().OnlyContain(c => c.Change.RenamedFrom.Length > 0, "unfiltered, git pairs both moves");

        var size = ChangePartition.Of(collected.Counted, all.Changes, [], collected.ComparedAgainst);

        size.State.Should().Be(ScopeState.Recorded, "never numstats disagree: a rename across the boundary is one file seen two ways");
        size.Cells.Should().ContainSingle().Which.Should().Be(
            new SizeCell(ChangeCoverage.Shown, ChangeKind.Of("src/a.js"), 2, LinesEnoughToBeRecognisedAsARename + 1, LinesEnoughToBeRecognisedAsARename),
            "both files are shown, never excluded");
    }

    [Fact]
    public async Task AMergeOfTheBaseBetweenTwoHeads_KeepsTheAncestor()
    {
        await Write("branch.cs", "the branch's own work\n");
        var first = await Commit("A");
        await Git("checkout", "main");
        await Write("mains.cs", "main moved on\n");
        await Commit("main gained a commit");
        await Git("checkout", "feature");
        await Git("merge", "--no-ff", "-m", "B: merge main", "main");
        var second = await Git("rev-parse", "HEAD");

        var delta = await _assembler.DeltaAsync(_repo, first, second, null, Ct);

        // The git layer cannot see the problem — which is why the bases are compared BEFORE git (S1.5 rows 3-4).
        delta.State.Should().Be(ScopeState.Recorded);
        delta.Filtered.Select(Shown).Should().Equal(["mains.cs +1 -0"], "main's own lines arrive in the head-to-head range");
    }

    [Fact]
    public async Task TheDelta_IsTheDiffBetweenTheTwoHeads()
    {
        var against = await Git("rev-parse", "main");
        await Write("f.cs", Lines("L", 1, 10));
        var first = await Commit("A: +10 lines");
        await Write("f.cs", Lines("L", 5, 10) + "X1\nX2\n");
        var second = await Commit("B: reverts 4 of them, adds 2");

        var delta = await _assembler.DeltaAsync(_repo, first, second, null, Ct);

        delta.State.Should().Be(ScopeState.Recorded);
        delta.Filtered.Select(Shown).Should().Equal(["f.cs +2 -4"], "B's edit, not the cumulative +8 against the base");
        delta.Unfiltered.Select(Shown).Should().Equal(["f.cs +2 -4"]);
        (await _assembler.AllChangesAsync(_repo, against, second, Ct)).Changes.Select(Shown)
            .Should().Contain("f.cs +8 -0", "the whole range IS the cumulative one — the two scopes differ");
    }

    [Fact]
    public async Task TheDeltaCarriesTheExclusionsAndSeesWhatTheyHid()
    {
        var first = await Git("rev-parse", "HEAD");
        await Write("dist/more.js", "noise\n");
        await Write("kept.cs", "kept\n");
        var second = await Commit("one excluded, one kept");

        var delta = await _assembler.DeltaAsync(_repo, first, second, null, Ct);

        delta.Filtered.Select(c => c.Change.Path).Should().Equal("kept.cs");
        delta.Unfiltered.Select(c => c.Change.Path).Should().BeEquivalentTo(["kept.cs", "dist/more.js"]);
    }

    [Fact]
    public async Task ARebasedBranch_IsNotAnAncestor()
    {
        await Write("one.cs", "recorded as the predecessor\n");
        var first = await Commit("A");
        await Git("reset", "--hard", "main");
        await Write("two.cs", "a different change after the rebase\n");
        var second = await Commit("B");
        _launcher.Requests.Clear();

        var delta = await _assembler.DeltaAsync(_repo, first, second, null, Ct);

        delta.State.Should().Be(ScopeState.NotAnAncestor);
        delta.Filtered.Should().BeEmpty();
        delta.Unfiltered.Should().BeEmpty();
        _launcher.Requests.Should().NotContain(r => r.Contains("numstat"), "an unreachable predecessor is refused before any numstat runs");
    }

    [Fact]
    public async Task AnUnknownCommit_IsGitFailed_NeverAThrow()
    {
        var head = await Git("rev-parse", "HEAD");
        var unknown = new string('d', 40);

        (await _assembler.DeltaAsync(_repo, unknown, head, null, Ct)).State
            .Should().Be(ScopeState.GitFailed, "a well-formed sha that is not in this repository");
        (await _assembler.DeltaAsync(_repo, head, unknown, null, Ct)).State.Should().Be(ScopeState.GitFailed);
        (await _assembler.AllChangesAsync(_repo, unknown, head, Ct)).Failed.Should().BeTrue();

        _launcher.Requests.Clear();
        (await _assembler.DeltaAsync(_repo, "--output=owned", head, null, Ct)).State
            .Should().Be(ScopeState.GitFailed, "a value that is not an object id never reaches a command line");
        (await _assembler.AllChangesAsync(_repo, head, "--output=owned", Ct)).Failed.Should().BeTrue();
        _launcher.Requests.Should().BeEmpty("refused before any process");
    }

    [Fact]
    public async Task AnAncestryCheckThatOutlivesItsDeadline_IsGitFailed()
    {
        var (head, parent) = (await Git("rev-parse", "HEAD"), await Git("rev-parse", "HEAD~1"));
        var launcher = new ScriptedLauncher(new ProcessLauncher(), Stalls("merge-base"));

        (await new ContextAssembler(launcher).DeltaAsync(_repo, parent, head, null, Ct, OneMillisecond)).State
            .Should().Be(ScopeState.GitFailed, "a git call that outlives its deadline is a failed call, not a verdict on the ancestry");
        launcher.Launches.Select(l => l.Request.Timeout).Should().OnlyContain(t => t == OneMillisecond, "the size call carries the caller's deadline");
        launcher.Launches.Should().ContainSingle(l => l.Request.Arguments.Contains("merge-base"), "the stalled call was really asked");
    }

    [Fact]
    public async Task ANumstatThatOutlivesItsDeadline_IsGitFailed_NeverAnEmptySuccess()
    {
        var (head, parent) = (await Git("rev-parse", "HEAD"), await Git("rev-parse", "HEAD~1"));
        var launcher = new ScriptedLauncher(new ProcessLauncher(), Stalls("diff", answersAncestor: true));
        var assembler = new ContextAssembler(launcher);

        (await assembler.DeltaAsync(_repo, parent, head, null, Ct, OneMillisecond)).State
            .Should().Be(ScopeState.GitFailed, "the ancestry was answered; it is the numstat that ran out its deadline");
        // Asked by the delta alone (AllChangesAsync has not run yet): a numstat is only asked once the scripted ancestry said 'ancestor'.
        launcher.Launches.Should().Contain(l => l.Request.Arguments.Contains("--numstat"), "the delta really reached its numstat");

        (await assembler.AllChangesAsync(_repo, parent, head, Ct, OneMillisecond)).Failed
            .Should().BeTrue("and not an empty, successful numstat");
        launcher.Launches.Select(l => l.Request.Timeout).Should().OnlyContain(t => t == OneMillisecond, "every size call carries the caller's deadline");
    }

    private static readonly TimeSpan OneMillisecond = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// A script that holds the git call <paramref name="subcommand"/> for an hour and runs the rest for real. The
    /// <see cref="ScriptedLauncher"/> cuts that wait at the request's own deadline and answers <c>TimedOut</c> the way the
    /// real launcher answers a killed tree. A real 1 ms deadline over real git is a race against the clock instead: on a
    /// fast runner git finishes inside the millisecond and the call simply succeeds (CI ubuntu, 2026-10-16).
    /// </summary>
    /// <param name="subcommand">The one call that is stalled past its deadline.</param>
    /// <param name="answersAncestor">
    /// Answer <c>merge-base --is-ancestor</c> with exit 0 (an ancestor) instead of running it for real under the 1 ms deadline:
    /// real, it could itself time out, and the delta would fail on the ancestry before any numstat ran.
    /// </param>
    private static Func<ScriptedLaunch, Task<ScriptedAnswer?>> Stalls(string subcommand, bool answersAncestor = false) =>
        launch => Task.FromResult<ScriptedAnswer?>(
            launch.Request.Arguments.Contains(subcommand) ? new ScriptedAnswer(Wait: TimeSpan.FromHours(1))
            : answersAncestor && launch.Request.Arguments.Contains("merge-base") ? new ScriptedAnswer()
            : null);

    [Fact]
    public async Task TheSameHeadTwice_IsAnObservedZero()
    {
        var head = await Git("rev-parse", "HEAD");
        _launcher.Requests.Clear();

        var delta = await _assembler.DeltaAsync(_repo, head, head.ToUpperInvariant(), null, Ct);

        delta.State.Should().Be(ScopeState.Recorded);
        delta.Filtered.Should().BeEmpty();
        delta.Unfiltered.Should().BeEmpty();
        _launcher.Requests.Should().BeEmpty("two identical heads need no process");
    }

    /// <summary>A launcher that remembers the arguments of every git call, so a test can say none was made.</summary>
    private sealed class RecordingLauncher(IProcessLauncher inner) : IProcessLauncher
    {
        public List<string> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Requests.Add(string.Join(' ', request.Arguments));

            return inner.RunAsync(request, ct);
        }
    }
}
