using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The checks of a coder job come from <c>.coai/toolchain.yaml</c> AT THE BASE SHA, never from the worktree (epoch 3 E2 s1 t2, the plan gate's amendment): a coder that edits the file changes nothing for its own
/// job, and the change is reported. Real git, a real repository, the fake CLI as the step.
/// </summary>
[Collection("fakecli-env")]
public sealed class AChecksAtTheBaseShaTests : IAsyncLifetime
{
    private const string FilePath = ".coai/toolchain.yaml";

    private readonly TempDir _logs = TempDir.For("coai-checks-base-logs-");

    private TempGitRepo _repo = null!;

    private string _base = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var launcher = new ProcessLauncher();
        _repo = await TempGitRepo.InitAsync(launcher, "coai-checks-base-");
        await _repo.GitAsync("config", "core.autocrlf", "false");
    }

    public async ValueTask DisposeAsync()
    {
        _logs.Dispose();
        await _repo.DisposeAsync();
    }

    private Task WriteToolchainAsync(string text)
    {
        Directory.CreateDirectory(Path.Combine(_repo.Path, ".coai"));

        return _repo.WriteAsync(FilePath, text);
    }

    /// <summary>A toolchain whose build says <paramref name="said"/> — the log shows which file ran.</summary>
    private static string Says(string said) => ToolchainFixture.File(["emit", said], ["emit", $"{said}-test"]);

    private async Task CommitBaseAsync()
    {
        await _repo.WriteAsync("README.md", "a repository\n");
        await _repo.CommitAsync("the base");
        _base = await _repo.HeadAsync();
    }

    private Task<CheckResult> RunAsync(string baseSha = "")
    {
        var launcher = new ProcessLauncher();
        var checks = new ChecksAtBase(new GitHistory(launcher), new CheckRunner(launcher));

        return checks.RunAsync(new DiskWorkFolder(_repo.Path), baseSha.Length > 0 ? baseSha : _base, new CheckRequest(_repo.Path, _logs.At("run"), 4));
    }

    private static string LogOf(StepRecord step) => File.ReadAllText(step.LogPath);

    [Fact]
    public async Task TheChecksRunTheFileAtTheBase_WhenTheCoderCommittedAnotherOne_AndTheChangeIsReported()
    {
        await WriteToolchainAsync(Says("base-build"));
        await CommitBaseAsync();
        await WriteToolchainAsync(Says("coder-build"));
        await _repo.CommitAsync("the coder edits the toolchain");

        var result = await RunAsync();

        result.Green.Should().BeTrue(result.Note);
        LogOf(result.Steps[0]).Should().Be("base-build", "the file of the base ran");
        LogOf(result.Steps[1]).Should().Be("base-build-test");
        result.ToolchainChange.Should().Be(ToolchainChange.Changed);
        result.Note.Should().Contain("changed").And.Contain(FilePath).And.Contain(_base[..8]).And.Contain("did not run");
    }

    [Fact]
    public async Task AnUncommittedEdit_OfTheToolchainFile_IsReportedAndDoesNotRunEither()
    {
        await WriteToolchainAsync(Says("base-build"));
        await CommitBaseAsync();
        await WriteToolchainAsync(Says("skip-everything"));

        var result = await RunAsync();

        LogOf(result.Steps[0]).Should().Be("base-build");
        result.ToolchainChange.Should().Be(ToolchainChange.Changed);
    }

    [Fact]
    public async Task AToolchainTheCoderDeletesToMakeTheChecksVanish_StillRunsTheBasesAndIsReported()
    {
        await WriteToolchainAsync(Says("base-build"));
        await CommitBaseAsync();
        File.Delete(Path.Combine(_repo.Path, FilePath));

        var result = await RunAsync();

        result.Green.Should().BeTrue(result.Note);
        LogOf(result.Steps[0]).Should().Be("base-build");
        result.ToolchainChange.Should().Be(ToolchainChange.Removed);
    }

    [Fact]
    public async Task AToolchainTheCoderAdds_WhereTheBaseHasNone_IsReported_AndTheJobStaysNotConfigured()
    {
        await CommitBaseAsync();
        await WriteToolchainAsync(Says("coder-build"));

        var result = await RunAsync();

        result.State.Should().Be(CheckState.NotConfigured, "the base has no file, and the coder's own file is not trusted to say what its checks are");
        result.Green.Should().BeFalse();
        result.Steps.Should().BeEmpty();
        result.ToolchainChange.Should().Be(ToolchainChange.Added);
        result.Note.Should().Contain("added");
    }

    [Fact]
    public async Task AnUntouchedToolchain_IsUnchanged_AndAWindowsLineEndingIsNotAChange()
    {
        await WriteToolchainAsync(Says("base-build"));
        await CommitBaseAsync();

        (await RunAsync()).ToolchainChange.Should().Be(ToolchainChange.Unchanged);

        await WriteToolchainAsync(Says("base-build").ReplaceLineEndings("\r\n"));
        var crlf = await RunAsync();

        crlf.ToolchainChange.Should().Be(ToolchainChange.Unchanged, "a checkout that turned LF into CRLF is not an edit by the coder");
        crlf.Note.Should().NotContain("did not run");
    }

    [Fact]
    public async Task ABaseWithNoToolchainAndNoCoderChange_IsNotConfigured()
    {
        await CommitBaseAsync();

        var result = await RunAsync();

        result.State.Should().Be(CheckState.NotConfigured);
        result.ToolchainChange.Should().Be(ToolchainChange.Unchanged);
    }

    [Fact]
    public async Task ABaseThatIsNotInTheRepository_FailsClosed_NeverFallsBackToTheWorktreesFile()
    {
        await WriteToolchainAsync(Says("worktree-build"));
        await CommitBaseAsync();

        var result = await RunAsync(new string('a', 40));

        result.State.Should().Be(CheckState.ToolchainRefused);
        result.Green.Should().BeFalse();
        result.Steps.Should().BeEmpty("the worktree's own file must not stand in for a base nobody could read");
        result.Note.Should().Contain("not a commit of this repository");
    }

    [Fact]
    public async Task ABaseThatIsNotACommitId_IsRefusedWithoutAGitCall()
    {
        await CommitBaseAsync();

        var result = await RunAsync("HEAD~1; rm -rf /");

        result.State.Should().Be(CheckState.ToolchainRefused);
        result.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task AToolchainFileThatIsWrongAtTheBase_IsRefused_AndNothingRuns()
    {
        await WriteToolchainAsync("version: 2\nprojects:\n  - path: app\n    adapter: dotnet\n");
        await CommitBaseAsync();

        var result = await RunAsync();

        result.State.Should().Be(CheckState.ToolchainRefused);
        result.Note.Should().Contain("'version'");
        result.Steps.Should().BeEmpty();
    }
}
