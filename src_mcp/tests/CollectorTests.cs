using CoaiMcp.Core.Collecting;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Finding the commit that fixed a defect, against real git.
/// </summary>
/// <remarks>
/// <para><b>A real repository, not a fake.</b> Every failure this guards against is git's — a commit
/// no ref reaches, a file that moved, a history that was rewritten under the finding. A fake git
/// would assert what we BELIEVE about those, which is exactly the thing measurement kept correcting:
/// the guard was written as equality once and inverted the whole feature, and the orphan rate came
/// back 90.7 % against a branch that happened to be behind.</para>
/// <para>The fixture builds an orphan the way real ones are made — commit on a branch, squash it onto
/// main, delete the branch — rather than by asserting that some sha is unreachable.</para>
/// </remarks>
public sealed class CollectorTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _git = null!;
    private Collector _collector = null!;

    /// <summary>The working tree, which is what a candidate records as its repository path.</summary>
    private string _repo = string.Empty;

    /// <summary>The defective method, and the same method fixed — one lock apart.</summary>
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

    /// <summary>The same method, renamed variables and reformatted — and NOT fixed.</summary>
    private const string Renamed = """
        using System.Collections.Generic;

        public sealed class Totals
        {
            private readonly Dictionary<string, int> _totals = new();

            public int GetOrAdd(string invoice, int amount)
            {
                if (!_totals.ContainsKey(invoice))
                {
                    _totals.Add(invoice, amount);
                }

                return _totals[invoice];
            }
        }
        """;

    public async ValueTask InitializeAsync()
    {
        _git = await TempGitRepo.InitAsync(_launcher, "coai-collect-");
        _repo = _git.Path;
        _collector = new Collector(new GitHistory(_launcher), new TreeSitterNormalizer());
        await Write("Totals.cs", Racy);
        await Commit("the defect");
    }

    public ValueTask DisposeAsync() => _git.DisposeAsync();

    [Fact]
    public async Task TheCommitThatChangedTheMethod_IsTheFix()
    {
        var broken = await Head();
        await Write("Totals.cs", Fixed);
        await Commit("hold the lock");
        var fix = await Head();

        var outcome = await _collector.CollectAsync(new Candidate(_repo, "main", broken, "Totals.cs", 10));

        outcome.State.Should().Be(CollectState.Collected);
        outcome.FixSha.Should().Be(fix);
        outcome.SymbolName.Should().Be("GetOrAdd");
        outcome.SkeletonBefore.Should().NotBe(outcome.SkeletonAfter);
        outcome.SkeletonAfter.Should().Contain("lock");
    }

    /// <summary>
    /// A commit that touches the file without touching the method is walked PAST.
    /// </summary>
    /// <remarks>
    /// Raised independently by two reviewers on the plan round. Stopping at the first commit that
    /// changes the FILE files the candidate as unchanged while the real fix sits one commit later —
    /// and a file where one method is fixed and another is tidied in separate commits is the ordinary
    /// shape of a day's work, not a corner case.
    /// </remarks>
    [Fact]
    public async Task ACommitThatTouchesTheFileButNotTheMethod_IsNotTheFix()
    {
        var broken = await Head();
        await Write("Totals.cs", Racy.Replace("public sealed class Totals", "// a note\npublic sealed class Totals", StringComparison.Ordinal));
        await Commit("an unrelated edit in the same file");
        await Write("Totals.cs", Fixed);
        await Commit("hold the lock");
        var fix = await Head();

        var outcome = await _collector.CollectAsync(new Candidate(_repo, "main", broken, "Totals.cs", 10));

        outcome.State.Should().Be(CollectState.Collected);
        outcome.FixSha.Should().Be(fix, "the first commit moved the method but did not change it");
    }

    /// <summary>
    /// A rename and a reformat are not a fix, however different the text is.
    /// </summary>
    /// <remarks>
    /// The skeletons are compared rather than the source, so a commit that renames every local and
    /// re-indents the body reads as unchanged — which it is. Treating the first textual difference as
    /// the fix would file a variable rename as a defect's cure, with a commit sha to prove it.
    /// (Plan round, codex.)
    /// </remarks>
    [Fact]
    public async Task ARenameIsNotAFix()
    {
        var broken = await Head();
        await Write("Totals.cs", Renamed);
        await Commit("tidy the names");

        var outcome = await _collector.CollectAsync(new Candidate(_repo, "main", broken, "Totals.cs", 10));

        outcome.State.Should().Be(CollectState.Skipped);
        outcome.Reason.Should().Be(SkipReason.MethodUnchanged);
    }

    /// <summary>
    /// A squash-merged, branch-deleted commit is still collectable when its session has a later round.
    /// </summary>
    /// <remarks>
    /// <para>THE finding of the plan round, and it decides the shape of the pipeline: 55.7 % of real
    /// candidates are orphaned this way, and 99.6 % of their objects survive. Guarding on ref
    /// reachability before looking for a bounded interval would discard every one of them without
    /// trying — most of the corpus, thrown away by the order of two checks.</para>
    /// <para>The orphan is made the way real ones are: a branch, a squash onto main, a delete.</para>
    /// </remarks>
    [Fact]
    public async Task AnOrphanedCommitIsStillWalked_WhenTheSessionHasALaterRound()
    {
        await Git("checkout", "-b", "feature");
        // A commit OF ITS OWN first, or `broken` is the commit main already has and nothing is
        // orphaned by deleting the branch — which is what the fixture assertion below caught.
        await Write("Totals.cs", Racy.Replace("int value)", "int value) // round one", StringComparison.Ordinal));
        await Commit("the round the reviewers read");
        var broken = await Head();
        await Write("Totals.cs", Fixed);
        await Commit("hold the lock");
        var later = await Head();

        await Git("checkout", "main");
        await Git("merge", "--squash", "feature");
        await Git("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "squashed");
        await Git("branch", "-D", "feature");

        // The branch is gone and nothing reaches either commit any more.
        var reachable = await _launcher.RunAsync(new ProcessRequest(
            "git", ["for-each-ref", "--format=%(refname)", "--contains", broken], _repo));
        reachable.StdOut.Trim().Should().BeEmpty("the fixture must actually produce an orphan");

        var outcome = await _collector.CollectAsync(
            new Candidate(_repo, "feature", broken, "Totals.cs", 10, LaterSha: later));

        outcome.State.Should().Be(CollectState.Collected, "the objects survive, so the interval is walkable");
        outcome.FixSha.Should().Be(later);
    }

    /// <summary>With no later round and nothing reaching it, an orphan is a skip that says so.</summary>
    [Fact]
    public async Task AnOrphanWithNoLaterRound_IsSkippedAsOrphaned()
    {
        await Git("checkout", "-b", "feature");
        await Write("Totals.cs", Racy.Replace("int value)", "int value) // round one", StringComparison.Ordinal));
        await Commit("the round the reviewers read");
        var broken = await Head();
        await Write("Totals.cs", Fixed);
        await Commit("hold the lock");
        await Git("checkout", "main");
        await Git("branch", "-D", "feature");

        var outcome = await _collector.CollectAsync(new Candidate(_repo, "feature", broken, "Totals.cs", 10));

        outcome.State.Should().Be(CollectState.Skipped);
        outcome.Reason.Should().Be(SkipReason.HeadShaOrphaned);
    }

    /// <summary>
    /// The guard is reachability, and a test that would fail if it were written as equality.
    /// </summary>
    /// <remarks>
    /// `head_sha` is the BROKEN state, so by collect time HEAD has necessarily moved past it. A guard
    /// written as `HEAD == head_sha` skips precisely the findings that were fixed — it inverts the
    /// feature — and it was written that way once. HEAD is three commits ahead here, and the
    /// candidate must still collect.
    /// </remarks>
    [Fact]
    public async Task TheGuardIsReachability_NotEquality()
    {
        var broken = await Head();
        await Write("Totals.cs", Fixed);
        await Commit("hold the lock");
        await Write("unrelated.md", "# later work");
        await Commit("something else");
        await Write("unrelated.md", "# later work, again");
        await Commit("something else again");

        (await Head()).Should().NotBe(broken, "HEAD has moved on, which is the normal case");

        var outcome = await _collector.CollectAsync(new Candidate(_repo, "main", broken, "Totals.cs", 10));

        outcome.State.Should().Be(CollectState.Collected, "an equality guard would have skipped this");
    }

    [Fact]
    public async Task ACommitThisRepositoryHasNeverHeardOf_IsUnreachable()
    {
        var outcome = await _collector.CollectAsync(
            new Candidate(_repo, "main", "0123456789abcdef0123456789abcdef01234567", "Totals.cs", 10));

        outcome.State.Should().Be(CollectState.Skipped);
        outcome.Reason.Should().Be(SkipReason.HeadShaUnreachable);
    }

    [Fact]
    public async Task ALanguageWeDoNotRead_IsSkippedBeforeAnyWalk()
    {
        var broken = await Head();
        await Write("notes.md", "# hello");
        await Commit("a note");

        var outcome = await _collector.CollectAsync(new Candidate(_repo, "main", broken, "notes.md", 1));

        outcome.State.Should().Be(CollectState.Skipped);
        outcome.Reason.Should().Be(SkipReason.LanguageUnsupported);
    }

    [Fact]
    public async Task ALineInsideNoFunction_IsSkipped()
    {
        var broken = await Head();
        await Write("Totals.cs", Fixed);
        await Commit("hold the lock");

        var outcome = await _collector.CollectAsync(new Candidate(_repo, "main", broken, "Totals.cs", 1));

        outcome.State.Should().Be(CollectState.Skipped);
        outcome.Reason.Should().Be(SkipReason.SymbolNotResolved);
    }

    [Fact]
    public async Task AScratchDirectoryIsRefusedBeforeGitIsAskedAnything()
    {
        var outcome = await _collector.CollectAsync(new Candidate(
            @"C:\Users\someone\AppData\Local\Temp\claude\d--rsd-Thing\abc\scratchpad\wt",
            "main",
            "0123456789abcdef0123456789abcdef01234567",
            "Totals.cs",
            10));

        outcome.State.Should().Be(CollectState.Skipped);
        outcome.Reason.Should().Be(SkipReason.RepoPathTransient);
    }

    [Fact]
    public async Task APathThatIsNotARepository_IsSkipped()
    {
        var outcome = await _collector.CollectAsync(new Candidate(
            Path.Combine(Path.GetTempPath(), $"not-a-repo-{Guid.NewGuid():N}"),
            "main",
            "0123456789abcdef0123456789abcdef01234567",
            "Totals.cs",
            10));

        outcome.State.Should().Be(CollectState.Skipped);
        outcome.Reason.Should().Be(SkipReason.RepoPathMissing);
    }

    /// <summary>A sha that could be read as a git OPTION is refused, not run.</summary>
    /// <remarks>
    /// These values come out of a database whose rows were written by reviewers' answers, so "it is
    /// always a hex id" is an assumption. A revision beginning with `--` is an option to git, and no
    /// amount of quoting changes that because there is no shell to quote for. (Code round, gemini.)
    /// </remarks>
    [Theory]
    [InlineData("--upload-pack=touch pwned")]
    [InlineData("--help")]
    [InlineData("-n1")]
    [InlineData("not a sha at all")]
    public async Task AShaThatCouldBeAnOption_IsRefused(string sha)
    {
        var outcome = await _collector.CollectAsync(new Candidate(_repo, "main", sha, "Totals.cs", 10));

        outcome.State.Should().Be(CollectState.Skipped);
        outcome.Reason.Should().Be(SkipReason.HeadShaUnreachable, "it never reached git at all");
    }

    /// <summary>An ABBREVIATED sha is refused rather than disambiguated.</summary>
    /// <remarks>
    /// `rounds.head_sha` is written from `%H`, so it is forty characters or the row is malformed.
    /// The validator accepted four to sixty-four, which let a truncated value reach git — and git
    /// resolves an abbreviation to whatever object it happens to disambiguate to, so a corrupt row
    /// would be collected against a real commit and the fix sha would look convincing. Refusing is
    /// the only answer that cannot be silently wrong. (Code round, codex.)
    /// </remarks>
    [Fact]
    public async Task AnAbbreviatedShaIsRefused_RatherThanDisambiguated()
    {
        var broken = await Head();
        await Write("Totals.cs", Fixed);
        await Commit("hold the lock");

        var outcome = await _collector.CollectAsync(
            new Candidate(_repo, "main", broken[..8], "Totals.cs", 10));

        outcome.State.Should().Be(CollectState.Skipped);
        outcome.Reason.Should().Be(
            SkipReason.HeadShaUnreachable, "a short id is a malformed row, not an abbreviation");
    }

    /// <summary>Two methods of one name are not one method.</summary>
    /// <remarks>
    /// An overload set shares a name, so taking the first match would record an unrelated overload's
    /// change as this defect's fix — with a commit sha attached, which is worse than not collecting
    /// it. Raised twice, independently, on the code round. (codex.)
    /// </remarks>
    [Fact]
    public async Task AnOverloadedNameIsRefused_RatherThanGuessed()
    {
        const string twoOverloads = """
            public sealed class Totals
            {
                public int GetOrAdd(string key)
                {
                    return _items[key];
                }

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

        await Write("Totals.cs", twoOverloads);
        await Commit("two overloads, one defective");
        var broken = await Head();
        // Any later commit touching the file is enough: the walk must refuse before it compares.
        await Write("Totals.cs", twoOverloads + Environment.NewLine + "// touched");
        await Commit("touch the file");

        var outcome = await _collector.CollectAsync(new Candidate(_repo, "main", broken, "Totals.cs", 13));

        outcome.State.Should().Be(CollectState.Skipped);
        outcome.Reason.Should().Be(SkipReason.SymbolAmbiguous);
    }

    /// <summary>
    /// A git failure judging the interval is a FAILURE, not a quiet switch to a different search.
    /// </summary>
    /// <remarks>
    /// <para>When the ancestry check could not be answered, the code fell through to the
    /// open-ended path and walked whichever ref happened to contain the commit — a DIFFERENT
    /// search, reported as though it were the bounded one, and a fix sha attributed from a branch
    /// nobody asked about. Every other git failure in the collector is a `failed`; this one was
    /// silently not. (Code round, gemini.)</para>
    /// <para>The fixture times out exactly one subcommand, because that is the shape of the real
    /// thing: git answers everything else perfectly well while one call does not come back.</para>
    /// </remarks>
    [Fact]
    public async Task AGitFailureJudgingTheInterval_IsAFailureNotAnOpenEndedWalk()
    {
        var broken = await Head();
        await Write("Totals.cs", Fixed);
        await Commit("hold the lock");
        var later = await Head();
        var collector = new Collector(
            new GitHistory(new FlakyGit(_launcher, "merge-base")), new TreeSitterNormalizer());

        var outcome = await collector.CollectAsync(
            new Candidate(_repo, "main", broken, "Totals.cs", 10, LaterSha: later));

        outcome.State.Should().Be(
            CollectState.Failed, "we learned nothing about the interval, so nothing may be claimed");
        outcome.Reason.Should().Be(SkipReason.GitFailed);
        outcome.FixSha.Should().BeEmpty("a failure attributes no commit");
    }

    // The four git helpers this class had are `TempGitRepo`'s now — the un-anonymised view's suite
    // needed the same four, and a second copy is the thing the reuse rule names. These keep the
    // call sites above reading as they did.
    private Task<string> Head() => _git.HeadAsync();

    private Task Write(string name, string text) => _git.WriteAsync(name, text);

    private Task Commit(string message) => _git.CommitAsync(message);

    private Task Git(params string[] args) => _git.GitAsync(args);
}

/// <summary>Real git, except for one subcommand that never comes back.</summary>
/// <remarks>
/// The only failure shape worth fixturing: git answers everything else, so a test that asserts on
/// the ONE call cannot pass by accident because the whole repository was unreadable.
/// </remarks>
internal sealed class FlakyGit(IProcessLauncher real, string subcommand) : IProcessLauncher
{
    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
        request.Arguments.Contains(subcommand, StringComparer.Ordinal)
            ? Task.FromResult(new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true))
            : real.RunAsync(request, ct);
}
