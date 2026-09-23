using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// Runs one of this repository's shell scripts, the way the thing that owns it runs it.
/// </summary>
/// <remarks>
/// <para>Extracted when the SECOND release check got a test. The first one's harness — find the
/// checkout, convert the path for a POSIX shell, start `sh` or explain why it could not — is not
/// about archives, and a second copy of it would have started drifting from the first the moment
/// either learned something.</para>
/// <para>Everything here is about the shell rather than about any particular script, which is why it
/// takes the script's name and its arguments and answers with the exit code and what it said.</para>
/// <para><b>It was <c>ReleaseScript</c> until 2026-09-23</b>, and stopped being only the release's
/// when `deploy/bugs/helper-protocol.sh` — a DEPLOY check — needed exactly this harness. Then
/// `install-env.sh`, the root helper itself, needed it with a stdin and a PATH of its own
/// (<see cref="Fed"/>); the name followed what the class does.</para>
/// </remarks>
internal static class ShellScript
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

    /// <summary>
    /// Run a checkout script with <paramref name="stdin"/> as its whole input and
    /// <paramref name="pathFirst"/> searched before the machine's own PATH.
    /// </summary>
    /// <remarks>
    /// For a script whose job is to read what arrives and act on the system — `install-env.sh`, which
    /// runs as root and writes under `/etc`. Putting a directory of stand-ins for `install`, `chown`
    /// and `mv` first on PATH runs THE REAL SCRIPT, unmodified, with the side effects landing where a
    /// test can read them; a test-only mode in a root helper would be one more thing root executes.
    /// Answers with what went to stdout too, because a helper that printed the secret is a failure.
    /// </remarks>
    internal static ShellRun Fed(string relativePath, string stdin, string pathFirst) =>
        Launch(
            Path.Combine(Repository(), relativePath.Replace('/', Path.DirectorySeparatorChar)),
            relativePath,
            Repository(),
            [],
            new ScriptInput(stdin, pathFirst));

    /// <summary>The shell part, which is the same whoever owns the script.</summary>
    private static (int Code, string Error) Start(
        string script, string what, string workingDirectory, string[] arguments)
    {
        var run = Launch(script, what, workingDirectory, arguments, ScriptInput.Nothing);

        return (run.Code, run.Error);
    }

    private static ShellRun Launch(
        string script, string what, string workingDirectory, string[] arguments, ScriptInput input)
    {
        File.Exists(script).Should().BeTrue("{0} is what this checkout runs", script);

        using var process = StartOrExplain(StartInfo(script, workingDirectory, arguments, input), what);

        // Both streams are read BEFORE anything is written, and at once: a script that fills one pipe
        // while this reads the other would wait on this reader for ever, and one that writes before it
        // reads would block a write that nobody was draining. (Code round, gemini.)
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        Feed(process, input);

        if (!process.WaitForExit(milliseconds: 30_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{what} did not finish in 30 seconds and was killed");
        }

        return new ShellRun(process.ExitCode, error.GetAwaiter().GetResult(), output.GetAwaiter().GetResult());
    }

    /// <summary>Hands the script its whole stdin, if it has one, and closes it.</summary>
    /// <remarks>
    /// A script is allowed to stop reading — a refusal on the first line is exactly that — and the
    /// write then meets a closed pipe. That is the script's answer, not the harness's failure: its exit
    /// code and what it said are what the test reads. (Code round, gemini; red first as
    /// <c>IOException: The pipe is being closed</c>.)
    /// </remarks>
    private static void Feed(Process process, ScriptInput input)
    {
        if (input.Stdin is not { } stdin)
        {
            return;
        }

        try
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The script stopped reading; what it said about why is in its streams.
        }
    }

    private static ProcessStartInfo StartInfo(
        string script, string workingDirectory, string[] arguments, ScriptInput input)
    {
        var utf8 = new System.Text.UTF8Encoding(false);
        var start = new ProcessStartInfo("sh")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = input.Stdin is not null,
            StandardInputEncoding = input.Stdin is null ? null : utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        start.ArgumentList.Add(Posix(script));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (input.PathFirst.Length > 0)
        {
            start.Environment["PATH"] = input.PathFirst + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }

        return start;
    }

    /// <summary>What a script is handed besides its arguments: its stdin, and what goes first on PATH.</summary>
    private sealed record ScriptInput(string? Stdin, string PathFirst)
    {
        public static readonly ScriptInput Nothing = new(null, string.Empty);
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

/// <summary>What one run of a script came to: its exit code and both of its streams.</summary>
internal sealed record ShellRun(int Code, string Error, string Output);
