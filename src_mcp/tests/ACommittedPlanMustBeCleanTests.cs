using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The guards around the proof: a path that cannot be a plan is refused before any process, a credential-shaped name is
/// refused before any process, and when the checkout is on the commit being read an edited or staged plan is refused —
/// because the reviewers would read the commit and the caller would believe they read the edit.
/// </summary>
/// <remarks>Real git, one fresh repository per test (these tests EDIT it), through a launcher that notes every command.</remarks>
public sealed class ACommittedPlanMustBeCleanTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private TempGitRepo _repo = null!;
    private string _sha = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-clean-");
        // Machine-level autocrlf would make "hello plan\n" twelve bytes; T4 turns it on itself, after the commit.
        await _repo.GitAsync("config", "core.autocrlf", "false");
        await _repo.WriteAsync("plan.md", "hello plan\n");
        Directory.CreateDirectory(Path.Combine(_repo.Path, "todo"));
        await _repo.WriteAsync("todo/PLAN.md", "a plan\n");
        await _repo.WriteAsync(".env", "SECRET=1\n");
        Directory.CreateDirectory(Path.Combine(_repo.Path, "secrets"));
        await _repo.WriteAsync("secrets/id_rsa", "key\n");
        await _repo.CommitAsync("the plans");
        _sha = await _repo.HeadAsync();
    }

    public async ValueTask DisposeAsync() => await _repo.DisposeAsync();

    private Task<PlanRead> ReadAsync(IProcessLauncher launcher, string path, string? sha = null) =>
        CommittedPlanReader.ReadGuardedAsync(new GitHistory(launcher), _repo.Path, sha ?? _sha, path, TestContext.Current.CancellationToken);

    private static string RefusedBy(PlanRead read) => read.Should().BeOfType<PlanRead.Refused>().Subject.Sentence;

    [Fact]
    public async Task ACleanPlanAtHead_IsAccepted_WithNoNote()
    {
        var read = await ReadAsync(_launcher, "plan.md");

        var accepted = read.Should().BeOfType<PlanRead.Accepted>().Subject;
        accepted.Text.Should().Be("hello plan\n");
        accepted.Note.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEditedPlan_IsRefusedAsUncommitted()
    {
        await _repo.WriteAsync("plan.md", "an edit nobody committed\n");

        var read = await ReadAsync(_launcher, "plan.md");

        RefusedBy(read).Should().Contain("uncommitted").And.Contain("'plan.md'").And.Contain("commit");
    }

    [Fact]
    public async Task AStagedPlan_IsRefusedAsStaged()
    {
        await _repo.WriteAsync("plan.md", "an edit that was staged\n");
        await _repo.GitAsync("add", "plan.md");

        var read = await ReadAsync(_launcher, "plan.md");

        RefusedBy(read).Should().Contain("staged").And.Contain("'plan.md'");
    }

    [Fact]
    public async Task ACrlfCheckoutOfAnLfBlob_UnderAutocrlf_IsStillClean()
    {
        // The eol case `git status` exists for here: with core.autocrlf=true a CHECKOUT writes CRLF and records that file's stat, and
        // status compares through the clean filter — so this is not an edit, and a byte comparison of the file would say it is.
        await _repo.GitAsync("config", "core.autocrlf", "true");
        File.Delete(Path.Combine(_repo.Path, "plan.md"));
        await _repo.GitAsync("checkout", "--", "plan.md");
        (await File.ReadAllBytesAsync(Path.Combine(_repo.Path, "plan.md"), TestContext.Current.CancellationToken))
            .Should().Equal("hello plan\r\n"u8.ToArray(), "the working copy really is CRLF — otherwise this proves nothing");

        var read = await ReadAsync(_launcher, "plan.md");

        var accepted = read.Should().BeOfType<PlanRead.Accepted>().Subject;
        accepted.Text.Should().Be("hello plan\n", "the blob is read, never the working file");
        accepted.Note.Should().BeEmpty();
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("/etc/x")]
    [InlineData("C:\\x")]
    [InlineData("a\0b")]
    [InlineData("todo/")]
    [InlineData("todo\\")]
    [InlineData("")]
    public async Task APathThatCannotBeAPlan_IsRefused_BeforeAnyProcess(string path)
    {
        var watching = new Watching(_launcher);

        var read = await ReadAsync(watching, path);

        RefusedBy(read).Should().Contain("todo/PLAN_x.md", "every shape refusal ends in the path that would have worked");
        watching.Ran.Should().BeEmpty();
    }

    [Fact]
    public async Task ABackslashPath_IsReadAsASlashPath()
    {
        var read = await ReadAsync(_launcher, "todo\\PLAN.md");

        var accepted = read.Should().BeOfType<PlanRead.Accepted>().Subject;
        accepted.Source.Path.Should().Be("todo/PLAN.md");
        accepted.Text.Should().Be("a plan\n");
    }

    [Theory]
    [InlineData(".env", ".env*")]
    [InlineData("secrets/id_rsa", "id_rsa*")]
    [InlineData("config\\SERVER.PEM", "*.pem")]
    public async Task ACredentialShapedPath_IsRefused_BeforeAnyProcess(string path, string shape)
    {
        var watching = new Watching(_launcher);

        var read = await ReadAsync(watching, path);

        RefusedBy(read).Should().Contain("credential file").And.Contain(shape);
        watching.Ran.Should().BeEmpty("a credential-shaped name is refused before git is asked anything");
    }

    [Fact]
    public async Task AHeadOnAnotherCommit_ReadsTheBlobAndSaysTheWorkingTreeWasNotCompared()
    {
        await _repo.WriteAsync("plan.md", "the second commit\n");
        await _repo.CommitAsync("a later plan");
        var head = await _repo.HeadAsync();
        await _repo.WriteAsync("plan.md", "dirty on top of the later commit\n");
        var watching = new Watching(_launcher);

        var read = await ReadAsync(watching, "plan.md");

        var accepted = read.Should().BeOfType<PlanRead.Accepted>().Subject;
        accepted.Text.Should().Be("hello plan\n", "the commit that was asked for, not the working copy and not HEAD's");
        accepted.Note.Should().Contain("not compared").And.Contain(_sha[..12]).And.Contain(head[..12]);
        watching.Ran.Should().NotContain(a => a.Contains(" status "), "a tree on another commit says nothing about this one");
    }

    [Fact]
    public async Task AStatusThatCannotRun_IsARefusal_NotAPass()
    {
        var watching = new Watching(
            _launcher,
            request => request.Arguments.Contains("status") ? new ProcessResult(128, string.Empty, "fatal: index file corrupt", TimedOut: false) : null);

        var read = await ReadAsync(watching, "plan.md");

        RefusedBy(read).Should().Contain("git fsck");
    }

    /// <summary>Real git (or a stand-in answer for one request), and a note of every command it was asked.</summary>
    private sealed class Watching(IProcessLauncher real, Func<ProcessRequest, ProcessResult?>? instead = null) : IProcessLauncher
    {
        public List<string> Ran { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Ran.Add($"{request.Executable} {string.Join(' ', request.Arguments)} ");

            return instead?.Invoke(request) is { } answer ? Task.FromResult(answer) : real.RunAsync(request, ct);
        }
    }
}
