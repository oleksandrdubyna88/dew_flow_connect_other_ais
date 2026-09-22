using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// Runs one of this repository's shell checks, the way the thing that owns it runs it.
/// </summary>
/// <remarks>
/// <para>Extracted when the SECOND release check got a test. The first one's harness — find the
/// checkout, convert the path for a POSIX shell, start `sh` or explain why it could not — is not
/// about archives, and a second copy of it would have started drifting from the first the moment
/// either learned something.</para>
/// <para>Everything here is about the shell rather than about any particular check, which is why it
/// takes the script's name and its arguments and answers with the exit code and what it said.</para>
/// <para><b>It stopped being only the release's in 2026-09.</b> `deploy/bugs/helper-protocol.sh` is
/// a DEPLOY check and needed exactly this harness; the third copy was written before this comment
/// was, which is the whole argument for the rule that caught it. The class keeps its name for now
/// and the name is now slightly wrong — see <see cref="FromCheckout"/>.</para>
/// </remarks>
internal static class ReleaseScript
{
    /// <summary>Run a script from `.github/scripts`, from <paramref name="workingDirectory"/>.</summary>
    /// <remarks>
    /// The working directory matters: `release.yml` calls these with RELATIVE paths, from the
    /// workspace, and on Windows that is also the only thing that works — GNU tar reads
    /// `C:/x/y.tar.gz` as a REMOTE `host:path` and answers "Cannot connect to C:".
    /// </remarks>
    internal static (int Code, string Error) Run(string name, string workingDirectory, params string[] arguments) =>
        Start(Path.Combine(Repository(), ".github", "scripts", name), name, workingDirectory, arguments);

    /// <summary>Run a script named by its path RELATIVE TO THE CHECKOUT, from the checkout.</summary>
    /// <remarks>
    /// <para>For the scripts that are not the release's. `deploy/bugs/` holds two that a test drives
    /// — the key picker and the helper-protocol check — and neither is reachable by a name under
    /// `.github/scripts`. Widening the existing harness was the cheap move; a `DeployScript` beside
    /// it would have been a second implementation of "start `sh` and say why you could not", which
    /// is the one thing this file exists to have only once.</para>
    /// <para>These scripts take absolute paths as arguments and read nothing relative, so the
    /// working directory is the checkout rather than a caller's choice.</para>
    /// </remarks>
    internal static (int Code, string Error) FromCheckout(string relativePath, params string[] arguments) =>
        Start(
            Path.Combine(Repository(), relativePath.Replace('/', Path.DirectorySeparatorChar)),
            relativePath,
            Repository(),
            arguments);

    /// <summary>The shell part, which is the same whoever owns the script.</summary>
    private static (int Code, string Error) Start(
        string script, string what, string workingDirectory, string[] arguments)
    {
        File.Exists(script).Should().BeTrue("{0} is what this checkout runs", script);

        var start = new ProcessStartInfo("sh")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        start.ArgumentList.Add(Posix(script));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = StartOrExplain(start, what);
        var error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit(milliseconds: 30_000).Should().BeTrue("the check should not hang");

        return (process.ExitCode, error);
    }

    /// <summary>A path a POSIX shell will accept, which a Windows one is not.</summary>
    /// <remarks>
    /// `sh` from git for Windows reads `C:/x/y` and not `C:\x\y` — a backslash is its escape
    /// character, so the path arrives mangled and the script answers 2, "that is not a file". On
    /// Linux there is nothing to convert and this is the identity.
    /// </remarks>
    internal static string Posix(string path) => path.Replace('\\', '/');

    /// <summary>A POSIX shell, or a decision about whose machine this is.</summary>
    /// <remarks>
    /// CI runs this suite on `ubuntu-latest`, where `sh` is always there — so on CI a missing shell
    /// is a broken job and says so. A Windows checkout without git's `sh` on PATH skips instead,
    /// because the alternative is a suite nobody can run locally. The shape is
    /// `StageRulesTests.RequireTheMount`'s, and for its reason: a skip that also applies to CI is a
    /// test that can quietly stop testing.
    /// </remarks>
    private static Process StartOrExplain(ProcessStartInfo start, string name)
    {
        try
        {
            return Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            if (Environment.GetEnvironmentVariable("CI") is { Length: > 0 } ci
                && !ci.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail($"{name} needs a POSIX shell and CI is the authoritative run");
            }

            Assert.Skip($"{name} needs `sh` on PATH; git for Windows provides one");
            throw;
        }
    }

    /// <summary>The checkout this test binary was built inside.</summary>
    private static string Repository()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".github", "scripts")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("no checkout above this test binary carries .github/scripts");
    }
}
