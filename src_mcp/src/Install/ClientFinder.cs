using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Install;

/// <summary>One CLI looked for: whether a file was found, where, and what it said to <c>--version</c> (empty when it said nothing usable).</summary>
internal sealed record FoundClient(Client Cli, bool Found, string Path, string Version, string Reason)
{
    internal static FoundClient Absent(Client cli) => new(cli, false, string.Empty, string.Empty, "not found on PATH or in the user's bin folder");
}

/// <summary>
/// Finds <c>claude</c>, <c>codex</c> and <c>agy</c> on THIS side and asks each its version (v2 E3.S6 t1) — an absent CLI is reported absent,
/// never guessed at.
/// </summary>
/// <remarks>
/// <para>Reuses what the reviewers already decided (reuse-first): <see cref="ExecutableResolver.OnPosixPath"/> on POSIX — an executable on the PATH wins — and
/// <see cref="ExecutableResolver.Resolve(string, bool, string?, Func{string, bool})"/> on Windows, where the npm shims are <c>.cmd</c> files and a bare name
/// cannot start. The user's own bin folder is the fallback for a PATH that has none, because an editor-started process has a PATH without it
/// (<c>AntigravityRuntime.InstalledExecutableFor</c>, measured 2026-10-02).</para>
/// <para>The file probe, the environment and the launcher are injected; the version question is bounded by <see cref="VersionTimeout"/> and a CLI that
/// does not answer is still FOUND — its file is there — with an empty version.</para>
/// </remarks>
internal sealed class ClientFinder(InstallHost host, Func<string, bool> isInstalled, IProcessLauncher launcher, TimeSpan? versionTimeout = null)
{
    /// <summary>How long a <c>--version</c> may take.</summary>
    internal static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Where <paramref name="cli"/>'s file is on this side, or empty — nothing is started to find it.</summary>
    internal string PathOf(Client cli)
    {
        var word = cli.Word();
        var onPath = host.IsWindows
            ? ExecutableResolver.Resolve(word, isWindows: true, host.PathVariable, isInstalled) is var resolved && !resolved.Equals(word, StringComparison.Ordinal) ? resolved : string.Empty
            : ExecutableResolver.OnPosixPath(word, host.PathVariable, isInstalled);

        return onPath.Length > 0 ? onPath : Fallback(word);
    }

    private string Fallback(string word)
    {
        var candidates = host.IsWindows
            ? new[] { host.Join(host.LocalBin, word + ".exe"), host.LocalAppData.Length > 0 && word == "agy" ? host.Join(host.LocalAppData, "agy", "bin", "agy.exe") : string.Empty }
            : [host.Join(host.LocalBin, word)];

        return candidates.FirstOrDefault(candidate => candidate.Length > 0 && host.HasHome && isInstalled(candidate)) ?? string.Empty;
    }

    /// <summary>The CLIs of <paramref name="wanted"/>, found or absent, in the order given.</summary>
    internal async Task<IReadOnlyList<FoundClient>> FindAsync(IEnumerable<Client> wanted, CancellationToken ct)
    {
        var found = new List<FoundClient>();
        foreach (var cli in wanted)
        {
            var path = PathOf(cli);
            found.Add(path.Length == 0 ? FoundClient.Absent(cli) : await WithVersionAsync(cli, path, ct));
        }

        return found;
    }

    /// <summary>What <paramref name="path"/> says to <c>--version</c>, first line, or empty — also for the binary to register, which is not one of the three clients.</summary>
    internal async Task<string> VersionOfAsync(string path, CancellationToken ct) => (await AskVersionAsync(path, ct)).Version;

    private async Task<FoundClient> WithVersionAsync(Client cli, string path, CancellationToken ct)
    {
        var asked = await AskVersionAsync(path, ct);

        return new FoundClient(cli, true, path, asked.Version, asked.Reason);
    }

    private sealed record VersionAnswer(string Version, string Reason);

    private async Task<VersionAnswer> AskVersionAsync(string path, CancellationToken ct)
    {
        try
        {
            var request = new ProcessRequest(path, ["--version"], WorkingDirectory())
            {
                Environment = host.VendorEnvironment,
                Timeout = versionTimeout ?? VersionTimeout,
            };
            var answer = await launcher.RunAsync(request, ct);

            return answer is { TimedOut: false, ExitCode: 0 }
                ? new VersionAnswer(FirstLine(answer.StdOut), string.Empty)
                : new VersionAnswer(string.Empty, answer.TimedOut ? "--version did not answer in time" : $"--version exited {answer.ExitCode}");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new VersionAnswer(string.Empty, $"--version could not start: {e.Message}");
        }
    }

    private string WorkingDirectory() => host.HasHome && Directory.Exists(host.Home) ? host.Home : AppContext.BaseDirectory;

    /// <summary>The first non-empty line, trimmed and cut: a version is one short line, and a banner is not.</summary>
    internal static string FirstLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        return line.Length > 80 ? line[..80] : line;
    }
}
