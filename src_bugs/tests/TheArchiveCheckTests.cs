using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The release's archive check, RUN, against archives built to be wrong on purpose.
/// </summary>
/// <remarks>
/// <para><b>Release-only shell is shell nothing executes until the day it matters.</b> This check
/// used to be six lines inside `release.yml`, and it had a hole that could not have been found
/// there: it looked for the wanted name anywhere in an entry, so `coai-bugs.dbg` satisfied the
/// search for `coai-bugs`. An archive carrying the debug symbols and NOT the executable passed the
/// one check whose entire job is to prove the executable is in the archive — and the failure would
/// have arrived as a host downloading an artefact with nothing to run.</para>
/// <para>So the condition is a file, and this suite executes it. The reviewer asked for exactly
/// this: "Extract this validation into a script or function. Execute it with valid and invalid tar
/// listings in the ordinary suite." (CodeRabbit, #328.)</para>
/// </remarks>
public sealed class TheArchiveCheckTests
{
    /// <summary>What `release.yml` asks for, spelled the way it spells it.</summary>
    private static readonly string[] WhatTheReleaseWants =
        ["coai-bugs", "*e_sqlite3*", "DEPLOY.md", "nginx-coai-bugs.conf"];

    [Fact]
    public void AnArchiveWithEverythingInItPasses()
    {
        var archive = Archive("coai-bugs", "coai-bugs.dbg", "libe_sqlite3.so", "DEPLOY.md", "nginx-coai-bugs.conf");

        var (code, error) = Check(archive, WhatTheReleaseWants);

        code.Should().Be(0, "this archive carries every file the deploy needs, and said: {0}", error);
    }

    /// <summary>The defect, as a test: debug symbols are not an executable.</summary>
    /// <remarks>
    /// This is the archive the old inline check passed. Building it and watching the loop answer
    /// `found coai-bugs`, with no `coai-bugs` in the archive, is what turned a reviewer's
    /// "a valid shell pattern can fail on real output" into a defect with a name.
    /// </remarks>
    [Fact]
    public void DebugSymbolsDoNotStandInForTheBinary()
    {
        var archive = Archive("coai-bugs.dbg", "libe_sqlite3.so", "DEPLOY.md", "nginx-coai-bugs.conf");

        var (code, error) = Check(archive, WhatTheReleaseWants);

        code.Should().Be(1, "there is no coai-bugs in this archive, only a file whose name contains it");
        error.Should().Contain("does not carry coai-bugs");
    }

    /// <summary>The library that this server is nothing without.</summary>
    /// <remarks>
    /// `mcp-v0.18.1` shipped an executable with no `e_sqlite3` beside it. THIS server reads its key
    /// table on every request, so the same omission answers 401 to everybody.
    /// </remarks>
    [Fact]
    public void AMissingNativeLibraryIsRefused()
    {
        var archive = Archive("coai-bugs", "DEPLOY.md", "nginx-coai-bugs.conf");

        var (code, error) = Check(archive, WhatTheReleaseWants);

        code.Should().Be(1);
        error.Should().Contain("does not carry *e_sqlite3*");
    }

    /// <summary>Either spelling of the library satisfies it, because both rids ship one of them.</summary>
    [Theory]
    [InlineData("libe_sqlite3.so")]
    [InlineData("e_sqlite3.so")]
    public void EitherNameForTheNativeLibraryIsAccepted(string spelling)
    {
        var archive = Archive("coai-bugs", spelling, "DEPLOY.md", "nginx-coai-bugs.conf");

        var (code, _) = Check(archive, WhatTheReleaseWants);

        code.Should().Be(0);
    }

    /// <summary>The notes travel WITH the binary, and an archive without them is not shippable.</summary>
    [Fact]
    public void TheDeployNotesAreNotOptional()
    {
        var archive = Archive("coai-bugs", "libe_sqlite3.so", "nginx-coai-bugs.conf");

        var (code, error) = Check(archive, WhatTheReleaseWants);

        code.Should().Be(1, "an operator with the binary and not the notes runs a server that records every contributor");
        error.Should().Contain("does not carry DEPLOY.md");
    }

    /// <summary>A name that merely CONTAINS what was asked for is not what was asked for.</summary>
    [Fact]
    public void AnExactNameIsExactInBothDirections()
    {
        var archive = Archive("not-DEPLOY.md.bak", "coai-bugs", "libe_sqlite3.so", "nginx-coai-bugs.conf");

        var (code, _) = Check(archive, WhatTheReleaseWants);

        code.Should().Be(1);
    }

    /// <summary>Called wrongly is its own answer, never a quiet pass.</summary>
    [Fact]
    public void AnArchiveThatIsNotThereIsNotAPass()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.tar.gz");

        var (code, _) = Check(missing, WhatTheReleaseWants);

        code.Should().Be(2, "a check that cannot look must not answer 'everything is there'");
    }

    /// <summary>A `.tar.gz` holding one directory of empty files, the way the release packages one.</summary>
    private static string Archive(params string[] names)
    {
        const string Directory = "coai-bugs-0.1.0-linux-x64";
        var work = System.IO.Directory.CreateTempSubdirectory("archive-check-");

        // The staging tree and the archive are SEPARATE: writing the `.tar.gz` inside the directory
        // being walked means the writer reads the file it is still writing.
        var tree = System.IO.Directory.CreateDirectory(Path.Combine(work.FullName, "tree"));
        var staged = System.IO.Directory.CreateDirectory(Path.Combine(tree.FullName, Directory));
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(staged.FullName, name), string.Empty);
        }

        var archive = Path.Combine(work.FullName, $"{Directory}.tar.gz");
        using (var file = File.Create(archive))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            TarFile.CreateFromDirectory(tree.FullName, gzip, includeBaseDirectory: false);
        }

        return archive;
    }

    /// <summary>Run the real script the real way, and report what it said.</summary>
    private static (int Code, string Error) Check(string archive, params string[] wanted)
    {
        var script = Path.Combine(Repository(), ".github", "scripts", "archive-carries.sh");
        File.Exists(script).Should().BeTrue("{0} is what the release runs", script);

        // FROM the archive's own directory, naming it relatively — which is how `release.yml` calls
        // it (`"$NAME.tar.gz"`, in the workspace) and, on Windows, the only way that works: GNU tar
        // reads `C:/x/y.tar.gz` as a REMOTE `host:path` and answers "Cannot connect to C:".
        var start = new ProcessStartInfo("sh")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(archive)!,
        };
        start.ArgumentList.Add(Posix(script));
        start.ArgumentList.Add(Path.GetFileName(archive));
        foreach (var pattern in wanted)
        {
            start.ArgumentList.Add(pattern);
        }

        using var process = StartOrExplain(start);
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
    private static string Posix(string path) => path.Replace('\\', '/');

    /// <summary>
    /// A POSIX shell, or a decision about whose machine this is.
    /// </summary>
    /// <remarks>
    /// CI runs this suite on `ubuntu-latest`, where `sh` is always there — so on CI a missing shell
    /// is a broken job and says so. A Windows checkout without git's `sh` on PATH skips instead,
    /// because the alternative is a suite nobody can run locally. The shape is
    /// `StageRulesTests.RequireTheMount`'s, and for its reason: a skip that also applies to CI is a
    /// test that can quietly stop testing.
    /// </remarks>
    private static Process StartOrExplain(ProcessStartInfo start)
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
                Assert.Fail("the release's archive check needs a POSIX shell and CI is the authoritative run");
            }

            Assert.Skip("the release's archive check needs `sh` on PATH; git for Windows provides one");
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
