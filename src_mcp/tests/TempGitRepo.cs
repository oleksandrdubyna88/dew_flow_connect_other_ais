using CoaiMcp.Runners.Processes;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A real git repository in a temporary directory, for the suites that drive real git.
/// </summary>
/// <remarks>
/// <para>Real git rather than a fake, for the reason <c>CollectorTests</c> gives: every failure those
/// suites guard against is git's — a commit no ref reaches, a file that moved, a history rewritten
/// under the finding — and a fake would assert what we BELIEVE about those, which is exactly what
/// measurement kept correcting.</para>
/// <para>It was four private helpers inside <c>CollectorTests</c>; the un-anonymised view (story 2.3
/// of the review-page plan) needed the same four, and the reuse rule's second move — extract the
/// shared half — is cheaper than a second copy that drifts.</para>
/// </remarks>
internal sealed class TempGitRepo : IAsyncDisposable
{
    private readonly IProcessLauncher _launcher;

    private TempGitRepo(IProcessLauncher launcher, string path)
    {
        _launcher = launcher;
        Path = path;
    }

    /// <summary>The working tree, which is also what a session would record as its `repo_path`.</summary>
    public string Path { get; }

    /// <summary>An empty repository on a branch called <c>main</c>.</summary>
    public static async Task<TempGitRepo> InitAsync(IProcessLauncher launcher, string prefix = "coai-git-")
    {
        var repo = new TempGitRepo(launcher, Directory.CreateTempSubdirectory(prefix).FullName);
        await repo.GitAsync("init", "-b", "main");

        return repo;
    }

    /// <summary>The full sha of HEAD.</summary>
    public async Task<string> HeadAsync()
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], Path));
        result.ExitCode.Should().Be(0, result.StdErr);

        return result.StdOut.Trim();
    }

    /// <summary>Writes a file into the working tree; nothing is staged or committed by this.</summary>
    public Task WriteAsync(string name, string text) =>
        File.WriteAllTextAsync(System.IO.Path.Combine(Path, name), text);

    /// <summary>Stages everything and commits it, with an identity and no signing so it runs anywhere.</summary>
    public async Task CommitAsync(string message)
    {
        await GitAsync("add", "-A");
        await GitAsync(
            "-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-m", message);
    }

    /// <summary>Runs git and insists it succeeded.</summary>
    public async Task GitAsync(params string[] args)
    {
        var result = await RunAsync(args);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>Runs git and answers whatever it said — for the assertions that read its output.</summary>
    public Task<ProcessResult> RunAsync(params string[] args) =>
        _launcher.RunAsync(new ProcessRequest("git", args, Path));

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }
}
