using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The four git reads the committed-plan reader stands on — an entry, a blob, the root and HEAD, a path's status — and
/// the flag that says a capture was cut short.
/// </summary>
/// <remarks>
/// Real git through <see cref="TempGitRepo"/>: every fact asserted here is git's own output shape, and a fake would
/// assert a belief about it. The two places a fake launcher IS used are the ones a real git cannot produce — a capture
/// the launcher cut (exit 0, <c>Truncated</c> set) and "no process was started at all".
/// </remarks>
public sealed class GitHistoryReadsByEntryTests : IAsyncLifetime
{
    private const string PlanOid = "707279fd268816a6390d22d1db277a907635e45f";

    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _repo = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-entry-");
        // The machine's autocrlf would turn "hello plan\n" into 12 bytes and move the id these tests pin.
        await _repo.GitAsync("config", "core.autocrlf", "false");
        await _repo.WriteAsync("plan.md", "hello plan\n");
        await _repo.CommitAsync("a plan");
    }

    public async ValueTask DisposeAsync() => await _repo.DisposeAsync();

    [Fact]
    public async Task AnEntry_IsOneZTerminatedLine_WithModeTypeOidAndPaddedSize()
    {
        var sha = await _repo.HeadAsync();

        var answer = await new GitHistory(_launcher).EntryAtAsync(_repo.Path, sha, "plan.md", TestContext.Current.CancellationToken);

        answer.Ran.Should().BeTrue();
        answer.Ok.Should().BeTrue();
        answer.Truncated.Should().BeFalse();
        answer.Out.Should().Be($"100644 blob {PlanOid}      11\tplan.md\0");
    }

    [Fact]
    public async Task AMissingPath_IsRanOkWithNothing()
    {
        var sha = await _repo.HeadAsync();

        var answer = await new GitHistory(_launcher).EntryAtAsync(_repo.Path, sha, "missing.md", TestContext.Current.CancellationToken);

        answer.Ran.Should().BeTrue();
        answer.Ok.Should().BeTrue("git answers exit 0 and nothing for a path that is not in the tree");
        answer.Out.Should().BeEmpty();
    }

    [Fact]
    public async Task ABlob_ComesBackByItsOid()
    {
        var answer = await new GitHistory(_launcher).BlobAsync(_repo.Path, PlanOid, TestContext.Current.CancellationToken);

        answer.Ran.Should().BeTrue();
        answer.Ok.Should().BeTrue();
        answer.Out.Should().Be("hello plan\n");
    }

    [Fact]
    public async Task RootAndHead_AreTwoLines()
    {
        var sha = await _repo.HeadAsync();

        var answer = await new GitHistory(_launcher).RootAndHeadAsync(_repo.Path, TestContext.Current.CancellationToken);

        answer.Ok.Should().BeTrue();
        answer.Lines.Should().HaveCount(2);
        answer.Lines[1].Should().Be(sha);
        // Git answers the REAL path. A temp directory behind a link (macOS: /var is /private/var) is spelled differently by
        // GetFullPath, which follows no links — so the expectation is the repo's path with its links resolved, the way the
        // worktree manager already compares what git lists against what it holds.
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(answer.Lines[0]))
            .Should().BeEquivalentTo(Path.TrimEndingDirectorySeparator(CoaiMcp.Runners.Worktrees.WorktreeManager.Resolved(_repo.Path)));
    }

    [Fact]
    public async Task ARepositoryReachedThroughALink_IsAnsweredByItsRealPath()
    {
        // The shape of macOS's /var → /private/var, made on every platform (a junction on Windows, a symlink elsewhere): git
        // answers the real path, and a comparison with the spelling the caller used is a claim about link-free paths.
        var holder = Directory.CreateTempSubdirectory("coai-link-holder-").FullName;
        var link = Path.Combine(holder, "repo-link");
        await DirectoryLink.MakeAsync(_launcher, link, _repo.Path);
        try
        {
            var answer = await new GitHistory(_launcher).RootAndHeadAsync(link, TestContext.Current.CancellationToken);

            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(answer.Lines[0]));
            root.Should().BeEquivalentTo(Path.TrimEndingDirectorySeparator(CoaiMcp.Runners.Worktrees.WorktreeManager.Resolved(link)));
            root.Should().NotBeEquivalentTo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(link)), "git does not answer the spelling it was given");
        }
        finally
        {
            DirectoryLink.Remove(link);
            Directory.Delete(holder, recursive: true);
        }
    }

    [Fact]
    public async Task AStagedEdit_ShowsInThePathsStatus()
    {
        var git = new GitHistory(_launcher);
        var clean = await git.PathStatusAsync(_repo.Path, "plan.md", TestContext.Current.CancellationToken);
        await _repo.WriteAsync("plan.md", "an edit\n");
        await _repo.GitAsync("add", "plan.md");

        var staged = await git.PathStatusAsync(_repo.Path, "plan.md", TestContext.Current.CancellationToken);

        clean.Ok.Should().BeTrue();
        clean.Out.Should().BeEmpty("a committed, untouched path has no status line");
        staged.Ok.Should().BeTrue();
        staged.Out.Should().Be("M  plan.md\0");
    }

    [Fact]
    public async Task AValueThatIsNotAnOid_StartsNoProcess()
    {
        var watching = new Counting();
        var git = new GitHistory(watching);
        var ct = TestContext.Current.CancellationToken;

        var answers = new[]
        {
            await git.EntryAtAsync(_repo.Path, "--upload-pack=x", "plan.md", ct),
            await git.EntryAtAsync(_repo.Path, new string('a', 40), "../outside.md", ct),
            await git.BlobAsync(_repo.Path, "--help", ct),
            await git.BlobAsync(_repo.Path, new string('a', 64), ct),
            await git.PathStatusAsync(_repo.Path, "/etc/x", ct),
        };

        watching.Launched.Should().Be(0, "a value that could be an option never reaches git");
        answers.Should().AllSatisfy(a =>
        {
            a.Ran.Should().BeTrue("nothing broke — the guard answered");
            a.Ok.Should().BeFalse();
            a.Out.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task ACutCapture_SaysTruncated()
    {
        // Exit 0 and the WHOLE text, with the launcher's flag set: the case where nothing in the text itself says "cut".
        var cut = new Counting(new ProcessResult(0, "hello plan\n", string.Empty, TimedOut: false, Truncated: true));
        var git = new GitHistory(cut);
        var ct = TestContext.Current.CancellationToken;

        var entry = await git.EntryAtAsync(_repo.Path, new string('a', 40), "plan.md", ct);
        var blob = await git.BlobAsync(_repo.Path, PlanOid, ct);
        var show = await git.FileAtAsync(_repo.Path, new string('a', 40), "plan.md", ct);
        var whole = await new GitHistory(new Counting(new ProcessResult(0, "x", string.Empty, TimedOut: false)))
            .BlobAsync(_repo.Path, PlanOid, ct);

        new[] { entry, blob, show }.Should().AllSatisfy(a =>
        {
            a.Ran.Should().BeTrue();
            a.Ok.Should().BeTrue();
            a.Truncated.Should().BeTrue("the launcher said the capture was cut");
        });
        whole.Truncated.Should().BeFalse();
    }

    /// <summary>Counts launches and answers a fixed result — or exit 0 with nothing.</summary>
    private sealed class Counting(ProcessResult? answer = null) : IProcessLauncher
    {
        public int Launched { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Launched++;

            return Task.FromResult(answer ?? new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
        }
    }
}
