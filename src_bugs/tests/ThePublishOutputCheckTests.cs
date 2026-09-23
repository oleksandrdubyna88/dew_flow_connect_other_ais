using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The release's publish-output check, RUN, against directories built to be wrong on purpose.
/// </summary>
/// <remarks>
/// <para><b>Release-only shell is shell nothing executes until the day it matters.</b> This check
/// was four lines inside `release.yml`, three times over, and the copies had already drifted:</para>
/// <code>NATIVE=$(ls out/*e_sqlite3* 2>/dev/null | head -1); if [ -z "$NATIVE" ]; then …</code>
/// <para>`ls … | head -1` answers the same empty string whether the glob matched nothing or the
/// directory could not be read, and it TRUNCATES a name containing a newline — measured: with a file
/// called `a\nb_e_sqlite3.so` it set NATIVE to `out/a`, and the release would have shipped without
/// the library the step had just said it verified.</para>
/// <para>So the condition is a file, and this suite executes it. The reviewer asked for exactly
/// this: "Add hermetic tests that run on ordinary pushes and cover these failure paths before
/// merging." (CodeRabbit, #370 — the same move `archive-carries.sh` made after #328.)</para>
/// </remarks>
public sealed class ThePublishOutputCheckTests
{
    /// <summary>What the mcp job asks of its publish output, spelled the way it spells it.</summary>
    private static readonly string[] WhatTheMcpJobWants = ["*e_sqlite3*", "*tree-sitter*"];

    [Fact]
    public void OutputWithEverythingInItPasses()
    {
        var dir = Output("coai-mcp", "libe_sqlite3.so", "libtree-sitter-c-sharp.so");

        var (code, error) = Check(dir, WhatTheMcpJobWants);

        code.Should().Be(0, "this output carries everything the package needs, and said: {0}", error);
    }

    /// <summary>The omission that shipped: `mcp-v0.18.1` had no `e_sqlite3` beside the binary.</summary>
    [Fact]
    public void AMissingNativeLibraryIsRefused()
    {
        var dir = Output("coai-mcp", "libtree-sitter-c-sharp.so");

        var (code, error) = Check(dir, WhatTheMcpJobWants);

        code.Should().Be(1, "the server would ship unable to open its own database");
        error.Should().Contain("does not carry *e_sqlite3*");
    }

    /// <summary>The grammars `--normalize` reaches by P/Invoke, exactly as the database reaches sqlite.</summary>
    [Fact]
    public void MissingGrammarsAreRefused()
    {
        var dir = Output("coai-mcp", "libe_sqlite3.so");

        var (code, error) = Check(dir, WhatTheMcpJobWants);

        code.Should().Be(1, "--normalize would throw DllNotFoundException on the shipped binary");
        error.Should().Contain("does not carry *tree-sitter*");
    }

    /// <summary>Either spelling of the library satisfies it, because both rids ship one of them.</summary>
    [Theory]
    [InlineData("libe_sqlite3.so")]
    [InlineData("e_sqlite3.so")]
    [InlineData("e_sqlite3.dll")]
    public void EitherNameForTheNativeLibraryIsAccepted(string spelling)
    {
        var dir = Output("coai-mcp", spelling, "tree-sitter-json.dll");

        var (code, _) = Check(dir, WhatTheMcpJobWants);

        code.Should().Be(0);
    }

    /// <summary>
    /// THE DEFECT THIS REPLACED, as a test: a name with a newline in it.
    /// </summary>
    /// <remarks>
    /// `ls out/*e_sqlite3* | head -1` takes the first LINE, so this file arrived as `out/a` — a path
    /// that does not exist — and the copy that followed it would have failed or, worse, copied
    /// nothing while the step reported the library verified. The check answers about the file, not
    /// about a line of `ls` output, so it passes.
    /// </remarks>
    [Fact]
    public void ANameWithANewlineInItIsStillTheFile()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("NTFS refuses a newline in a filename; the defect this pins is a POSIX one");
        }

        var dir = Output("coai-mcp", "a\nb_e_sqlite3.so", "libtree-sitter-json.so");

        var (code, error) = Check(dir, WhatTheMcpJobWants);

        code.Should().Be(0, "the library IS there, whatever `ls | head -1` would have made of it: {0}", error);
    }

    /// <summary>A directory is not a file, and a glob that matches only one has matched nothing.</summary>
    [Fact]
    public void ADirectoryDoesNotStandInForTheLibrary()
    {
        var dir = Output("coai-mcp", "libtree-sitter-json.so");
        Directory.CreateDirectory(Path.Combine(dir, "libe_sqlite3.so"));

        var (code, error) = Check(dir, WhatTheMcpJobWants);

        code.Should().Be(1, "nothing can be copied out of a directory named like the library");
        error.Should().Contain("does not carry *e_sqlite3*");
    }

    /// <summary>An empty publish output is the loudest case, and it must not pass quietly.</summary>
    [Fact]
    public void AnEmptyOutputIsRefused()
    {
        var dir = Output();

        var (code, error) = Check(dir, WhatTheMcpJobWants);

        code.Should().Be(1);
        error.Should().Contain("does not carry *e_sqlite3*");
        error.Should().Contain("does not carry *tree-sitter*", "both are reported, not just the first");
    }

    /// <summary>Called wrongly is its own answer, never a quiet pass.</summary>
    [Fact]
    public void ADirectoryThatIsNotThereIsNotAPass()
    {
        var absent = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}");

        var (code, _) = Check(absent, WhatTheMcpJobWants);

        code.Should().Be(2, "a check that cannot look must not answer 'everything is there'");
    }

    /// <summary>Nothing to look for is a caller's mistake, not an empty success.</summary>
    [Fact]
    public void NoPatternsIsACallersMistake()
    {
        var dir = Output("coai-mcp");

        var (code, _) = Check(dir);

        code.Should().Be(2);
    }

    /// <summary>A directory of empty files, the way `dotnet publish` leaves one.</summary>
    private static string Output(params string[] names)
    {
        var work = Directory.CreateTempSubdirectory("publish-output-check-");
        var output = Directory.CreateDirectory(Path.Combine(work.FullName, "out"));
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(output.FullName, name), string.Empty);
        }

        return output.FullName;
    }

    /// <summary>Run the real script the real way: from the workspace, naming the directory relatively.</summary>
    private static (int Code, string Error) Check(string directory, params string[] wanted)
    {
        var parent = Path.GetDirectoryName(directory)!;
        var arguments = new List<string> { Path.GetFileName(directory) };
        arguments.AddRange(wanted);

        return ShellScript.Run("publish-output-carries.sh", parent, [.. arguments]);
    }
}
