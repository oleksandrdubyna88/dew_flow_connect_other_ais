using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The container image runs a vendor installer only once it has been fetched whole and looks like a script.
/// </summary>
/// <remarks>
/// <para><b>`server-v0.11.0`'s amd64 image failed on one bad answer</b> (release run 38075423539, 2026-10-10):
/// <c>curl -fsSL https://antigravity.google/cli/install.sh | bash</c> handed bash something that was not a script —
/// <c>syntax error near unexpected token `)'</c>, most likely an HTML page — while the arm64 image built from the same
/// URL in the same run. A pipe cannot look before it runs, and cannot try again. <c>src_server/install-cli.sh</c> fetches
/// to a file, bounds the fetch, accepts it only behind a shebang, retries, and says what it got instead.</para>
/// <para>The behaviour runs the REAL script with bash against <c>file://</c> URLs — where no bash can be found (a
/// Windows box without Git for Windows) that half reports itself skipped rather than passing.</para>
/// </remarks>
public sealed class TheImageChecksEachInstallerTests : IDisposable
{
    private static readonly string[] Installers =
    [
        "https://chatgpt.com/codex/install.sh",
        "https://claude.ai/install.sh",
        "https://antigravity.google/cli/install.sh",
    ];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-install-cli-" + Guid.NewGuid().ToString("N"));

    public TheImageChecksEachInstallerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory a stray handle still holds is the OS's to clean.
        }
    }

    [Fact]
    public void NoInstallerIsPipedIntoAShell_EachGoesThroughTheCheckedScript()
    {
        var dockerfile = File.ReadAllText(Path.Combine(RepoRoot(), "src_server", "Dockerfile"));

        dockerfile.Should().NotMatchRegex(@"curl[^\n]*\|\s*(sh|bash)\b",
            "a pipe runs whatever arrived — an HTML page included — and cannot try again");
        foreach (var url in Installers)
        {
            dockerfile.Should().MatchRegex($@"install-cli\.sh\s+{System.Text.RegularExpressions.Regex.Escape(url)}\s+(sh|bash)",
                $"{url} must be fetched, checked and only then run");
        }

        dockerfile.Should().Contain("COPY src_server/install-cli.sh", "the image must carry the script it runs");
    }

    [Fact]
    public void APageIsRefused_TriedAgain_AndNamed_NeverRun()
    {
        var page = Write("page.html", "<!doctype html>\n<html><body>Not here</body></html>\n");

        var (code, stderr) = Run(FileUrl(page), "sh");

        code.Should().NotBe(0, "a persistent bad answer must still fail the build");
        stderr.Should().Contain("did not answer with a script", "the build log must say what went wrong");
        stderr.Should().Contain("<!doctype html>", "and show what arrived instead");
        stderr.Split('\n').Count(line => line.Contains("did not answer with a script", StringComparison.Ordinal))
            .Should().Be(3, "a transient bad answer is what the retry is for");
    }

    [Fact]
    public void AScriptIsRun_WithTheShellNamed()
    {
        var mark = Path.Combine(_dir, "installed.mark");
        var script = Write("good.sh", $"#!/bin/sh\necho installed > '{Posix(mark)}'\n");

        var (code, stderr) = Run(FileUrl(script), "sh");

        code.Should().Be(0, stderr);
        File.Exists(mark).Should().BeTrue("the installer that arrived whole and looked like a script was run");
        stderr.Should().Contain("fetching", "a build log shows the fetch BEFORE it can stall for minutes (the code round)")
            .And.Contain("attempt 1 of 3");
    }

    [Fact]
    public void AFetchThatFails_IsTriedAgain_AndThenFailsTheBuild()
    {
        var (code, stderr) = Run(FileUrl(Path.Combine(_dir, "missing.sh")), "sh");

        code.Should().NotBe(0);
        stderr.Split('\n').Count(line => line.Contains("did not answer with a script", StringComparison.Ordinal))
            .Should().Be(3);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal));

        return path;
    }

    /// <summary>Runs the real <c>install-cli.sh</c> with bash, no pause between attempts.</summary>
    private static (int Code, string StdErr) Run(string url, string shell)
    {
        var bash = Bash();
        Assert.SkipWhen(bash.Length == 0, "no bash on this machine — the script's behaviour runs on CI's Linux and macOS legs");
        var start = new ProcessStartInfo(bash)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Posix(Path.Combine(RepoRoot(), "src_server", "install-cli.sh")));
        start.ArgumentList.Add(url);
        start.ArgumentList.Add(shell);
        start.Environment["INSTALL_CLI_PAUSE"] = "0";
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(60)).Should().BeTrue("the script must not hang");

        return (process.ExitCode, stderr.Result);
    }

    private static string Bash() => ShellScripts.Bash();

    private static string Posix(string path) => ShellScripts.Posix(path);

    private static string FileUrl(string path) => "file:///" + Posix(path).TrimStart('/');

    private static string RepoRoot() => ShellScripts.RepoRoot();
}
