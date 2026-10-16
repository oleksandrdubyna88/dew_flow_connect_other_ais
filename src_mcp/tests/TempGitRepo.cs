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

    /// <summary>An empty repository on a branch called <c>main</c>, SHA-1 unless <paramref name="objectFormat"/> says <c>sha256</c>.</summary>
    public static async Task<TempGitRepo> InitAsync(IProcessLauncher launcher, string prefix = "coai-git-", string objectFormat = "sha1")
    {
        var repo = new TempGitRepo(launcher, Directory.CreateTempSubdirectory(prefix).FullName);
        await repo.GitAsync("init", "-b", "main", $"--object-format={objectFormat}");

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
        await CommitIndexAsync(message);
    }

    /// <summary>Commits what is staged and nothing more — <c>add -A</c> would stage the DELETION of an entry that has no working file.</summary>
    public async Task CommitIndexAsync(string message)
    {
        await GitAsync(
            "-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-m", message);
    }

    /// <summary>
    /// Writes these exact bytes into the working tree and stages them as they are — a BOM, invalid UTF-8, a pointer file.
    /// <c>--no-filters</c> so <c>core.autocrlf</c> cannot change what the blob holds; <c>WriteAsync</c> is text-only and a
    /// string on stdin cannot carry invalid bytes. Answers the blob's object id.
    /// </summary>
    public async Task<string> StageBlobAsync(string name, byte[] bytes, string mode = "100644")
    {
        var file = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, bytes);
        var hashed = await RunAsync("hash-object", "-w", "--no-filters", "--", name);
        hashed.ExitCode.Should().Be(0, hashed.StdErr);
        var oid = hashed.StdOut.Trim();
        await StageEntryAsync(name, mode, oid);

        return oid;
    }

    /// <summary>Stages an index entry with no working file: <c>120000</c> is a symlink and <c>160000</c> a submodule, neither needing a privilege.</summary>
    public Task StageEntryAsync(string name, string mode, string oid) =>
        GitAsync("update-index", "--add", "--cacheinfo", $"{mode},{oid},{name}");

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
