using System.Formats.Tar;
using System.IO.Compression;
using CoaiMcp.Tests;
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
        var missing = Path.Combine(TempDir.Root, $"absent-{Guid.NewGuid():N}.tar.gz");

        var (code, _) = Check(missing, WhatTheReleaseWants);

        code.Should().Be(2, "a check that cannot look must not answer 'everything is there'");
    }

    // ---------------------------------------------------------------------------------------------
    // THE ZIP. The Windows `coai-mcp` package is a `.zip` made AFTER signing, by the shared signing
    // workflow, so the product check runs on that final file and must read it the way it reads a
    // `.tar.gz`: same basename match, exact by default. Each case is an archive built to be wrong.
    // ---------------------------------------------------------------------------------------------

    /// <summary>What the Windows package must carry: the binary and the SQLite library beside it.</summary>
    private static readonly string[] WhatTheWindowsPackageWants = ["coai-mcp.exe", "*e_sqlite3*"];

    [Fact]
    public void AZipWithEverythingInItPasses()
    {
        var archive = Zip("coai-mcp.exe", "e_sqlite3.dll", "tree-sitter.dll");

        var (code, error) = Check(archive, WhatTheWindowsPackageWants);

        code.Should().Be(0, "this zip carries the binary and the library, and said: {0}", error);
    }

    [Fact]
    public void AZipWithoutTheBinaryIsRefused()
    {
        var archive = Zip("e_sqlite3.dll", "tree-sitter.dll");

        var (code, error) = Check(archive, WhatTheWindowsPackageWants);

        code.Should().Be(1, "a zip that signed everything but the binary is not a package");
        error.Should().Contain("does not carry coai-mcp.exe");
    }

    /// <summary>The #328 hole, now for a zip: a name that CONTAINS the wanted one is not the wanted one.</summary>
    [Fact]
    public void DebugSymbolsDoNotStandInForTheBinaryInAZip()
    {
        var archive = Zip("coai-mcp.exe.dbg", "e_sqlite3.dll");

        var (code, error) = Check(archive, WhatTheWindowsPackageWants);

        code.Should().Be(1, "there is no coai-mcp.exe here, only a file whose name contains it");
        error.Should().Contain("does not carry coai-mcp.exe");
    }

    [Fact]
    public void AZipWithoutTheSqliteLibraryIsRefused()
    {
        var archive = Zip("coai-mcp.exe", "tree-sitter.dll");

        var (code, error) = Check(archive, WhatTheWindowsPackageWants);

        code.Should().Be(1);
        error.Should().Contain("does not carry *e_sqlite3*");
    }

    /// <summary>A file that is named `.zip` and is not one cannot be looked into, and that is not a pass.</summary>
    [Fact]
    public void AFileThatIsNotAZipIsNotAPass()
    {
        using var work = TempDir.For("archive-check-");
        var archive = Path.Combine(work.Path, "coai-mcp-0.1.0-win-x64.zip");
        File.WriteAllText(archive, "this is not a zip");

        var (code, _) = Check(archive, WhatTheWindowsPackageWants);

        code.Should().Be(2, "a check that cannot list the archive must not answer 'everything is there'");
    }

    // ---------------------------------------------------------------------------------------------
    // THE LAYOUT. `archive-carries.sh` matches BASENAMES, and its contract stays that (coai-bugs and
    // the tar check use it), so a Windows zip with the binary and the library one directory too deep,
    // or at the root with no package folder, passes it. The loader looks beside the binary, so the
    // package is whole only if both sit directly in `coai-mcp-<v>-<rid>/`. `final-zip-layout.sh`
    // asks that, and derives the folder from the zip's own name. (CodeRabbit, #944.)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AZipWithBothFilesAtThePackageTopLevelHasTheRightLayout()
    {
        var archive = ZipOf("coai-mcp-0.1.0-win-x64", "coai-mcp.exe", "e_sqlite3.dll", "tree-sitter.dll");

        var (code, error) = Layout(archive);

        code.Should().Be(0, "the binary and the library sit directly in the package folder, and it said: {0}", error);
    }

    /// <summary>The basename check passes this zip, and that is the point of having a second check.</summary>
    [Fact]
    public void FilesOneLevelTooDeepPassTheBasenameCheckAndFailTheLayout()
    {
        var archive = ZipOf("coai-mcp-0.1.0-win-x64", "bin/coai-mcp.exe", "bin/e_sqlite3.dll");

        Check(archive, WhatTheWindowsPackageWants).Code.Should().Be(0, "a basename match cannot see depth");
        var (code, error) = Layout(archive);

        code.Should().Be(1, "a library one directory away from the binary is not where the loader looks");
        error.Should().Contain("coai-mcp-0.1.0-win-x64/coai-mcp.exe");
    }

    [Fact]
    public void FilesAtTheRootWithoutThePackageFolderFailTheLayout()
    {
        var flat = FlatZip("coai-mcp-0.1.0-win-x64", "coai-mcp.exe", "e_sqlite3.dll");

        Check(flat, WhatTheWindowsPackageWants).Code.Should().Be(0, "a basename match cannot see a missing folder");
        var (code, _) = Layout(flat);

        code.Should().Be(1, "the shared workflow packs ONE top-level folder named for the package");
    }

    [Fact]
    public void ATopLevelLibraryWithoutTheBinaryFailsTheLayout()
    {
        var archive = ZipOf("coai-mcp-0.1.0-win-x64", "e_sqlite3.dll", "bin/coai-mcp.exe");

        var (code, error) = Layout(archive);

        code.Should().Be(1);
        error.Should().Contain("coai-mcp.exe");
    }

    [Fact]
    public void ATopLevelBinaryWithoutTheLibraryFailsTheLayout()
    {
        var archive = ZipOf("coai-mcp-0.1.0-win-x64", "coai-mcp.exe", "bin/e_sqlite3.dll");

        var (code, error) = Layout(archive);

        code.Should().Be(1);
        error.Should().Contain("e_sqlite3");
    }

    /// <summary>The folder is read from the NAME, so a zip whose folder is another package's is wrong.</summary>
    [Fact]
    public void AFolderThatIsNotNamedForTheZipFailsTheLayout()
    {
        var archive = ZipOf("coai-mcp-0.1.0-win-x64", "coai-mcp.exe", "e_sqlite3.dll");
        var renamed = Path.Combine(Path.GetDirectoryName(archive)!, "coai-mcp-0.2.0-win-x64.zip");
        File.Move(archive, renamed);

        var (code, _) = Layout(renamed);

        code.Should().Be(1, "coai-mcp-0.2.0-win-x64/ is what this zip's name promises, and it is not there");
    }

    /// <summary>A name that is not `coai-mcp-&lt;v&gt;-&lt;rid&gt;.zip` gives no folder to look in: called wrongly, never a pass.</summary>
    [Fact]
    public void AZipWhoseNameGivesNoPackageFolderIsNotAPass()
    {
        var archive = ZipOf("coai-mcp-0.1.0-win-x64", "coai-mcp.exe", "e_sqlite3.dll");
        var renamed = Path.Combine(Path.GetDirectoryName(archive)!, "something-else.zip");
        File.Move(archive, renamed);

        var (code, _) = Layout(renamed);

        code.Should().Be(2);
    }

    [Fact]
    public void AZipThatIsNotThereOrNotAZipIsNotAPass()
    {
        using var work = TempDir.For("archive-check-");
        var junk = Path.Combine(work.Path, "coai-mcp-0.1.0-win-x64.zip");
        File.WriteAllText(junk, "this is not a zip");

        Layout(junk).Code.Should().Be(2, "a check that cannot list the archive must not answer 'the layout is right'");
        Layout(Path.Combine(work.Path, "coai-mcp-9.9.9-win-x64.zip")).Code.Should().Be(2);
    }

    /// <summary>A `.zip` of one named folder holding empty files; a name with a slash is a file in a subfolder.</summary>
    private static string ZipOf(string directory, params string[] names)
    {
        var work = TempDir.For("archive-check-");
        var tree = System.IO.Directory.CreateDirectory(Path.Combine(work.Path, "tree"));
        var staged = System.IO.Directory.CreateDirectory(Path.Combine(tree.FullName, directory));
        foreach (var name in names)
        {
            var path = Path.Combine(staged.FullName, name.Replace('/', Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);
        }

        var archive = Path.Combine(work.Path, $"{directory}.zip");
        ZipFile.CreateFromDirectory(tree.FullName, archive, CompressionLevel.Fastest, includeBaseDirectory: false);

        return archive;
    }

    /// <summary>A `.zip` named like the package's, with the files at its ROOT: the package folder is missing.</summary>
    private static string FlatZip(string name, params string[] files)
    {
        var work = TempDir.For("archive-check-");
        var tree = System.IO.Directory.CreateDirectory(Path.Combine(work.Path, "tree"));
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(tree.FullName, file), string.Empty);
        }

        var archive = Path.Combine(work.Path, $"{name}.zip");
        ZipFile.CreateFromDirectory(tree.FullName, archive, CompressionLevel.Fastest, includeBaseDirectory: false);

        return archive;
    }

    /// <summary>`final-zip-layout.sh` the way `release.yml` runs it: relative zip name, from the zip's folder.</summary>
    private static (int Code, string Error) Layout(string archive) =>
        ShellScript.Run("final-zip-layout.sh", Path.GetDirectoryName(archive)!, Path.GetFileName(archive));

    /// <summary>A `.zip` holding one directory of empty files, the way the signing workflow packs one.</summary>
    private static string Zip(params string[] names)
    {
        const string Directory = "coai-mcp-0.1.0-win-x64";
        var work = TempDir.For("archive-check-");

        // Staging tree and archive are separate, for the same reason as in <see cref="Archive"/>.
        var tree = System.IO.Directory.CreateDirectory(Path.Combine(work.Path, "tree"));
        var staged = System.IO.Directory.CreateDirectory(Path.Combine(tree.FullName, Directory));
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(staged.FullName, name), string.Empty);
        }

        var archive = Path.Combine(work.Path, $"{Directory}.zip");
        ZipFile.CreateFromDirectory(tree.FullName, archive, CompressionLevel.Fastest, includeBaseDirectory: false);

        return archive;
    }

    /// <summary>A `.tar.gz` holding one directory of empty files, the way the release packages one.</summary>
    private static string Archive(params string[] names)
    {
        const string Directory = "coai-bugs-0.1.0-linux-x64";
        var work = TempDir.For("archive-check-");

        // The staging tree and the archive are SEPARATE: writing the `.tar.gz` inside the directory
        // being walked means the writer reads the file it is still writing.
        var tree = System.IO.Directory.CreateDirectory(Path.Combine(work.Path, "tree"));
        var staged = System.IO.Directory.CreateDirectory(Path.Combine(tree.FullName, Directory));
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(staged.FullName, name), string.Empty);
        }

        var archive = Path.Combine(work.Path, $"{Directory}.tar.gz");
        using (var file = File.Create(archive))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            TarFile.CreateFromDirectory(tree.FullName, gzip, includeBaseDirectory: false);
        }

        return archive;
    }

    /// <summary>Run the real script the real way, and report what it said.</summary>
    /// <remarks>
    /// FROM the archive's own directory, naming it relatively — which is how `release.yml` calls it
    /// (`"$NAME.tar.gz"`, in the workspace) and, on Windows, the only way that works: GNU tar reads
    /// `C:/x/y.tar.gz` as a REMOTE `host:path` and answers "Cannot connect to C:".
    /// </remarks>
    private static (int Code, string Error) Check(string archive, params string[] wanted)
    {
        var arguments = new List<string> { Path.GetFileName(archive) };
        arguments.AddRange(wanted);

        return ShellScript.Run("archive-carries.sh", Path.GetDirectoryName(archive)!, [.. arguments]);
    }

}
