using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// No source file in this repository carries a raw NUL or DEL byte.
/// </summary>
/// <remarks>
/// <para><b>This is a scar, twice over.</b> <c>notifications.ts</c> carries a comment about spelling
/// the printable range: <i>"the class has to be SPELLED, and the first attempt at spelling it put a
/// real NUL byte in this file."</i> It then says the same module did it AGAIN — a DEL escape in the
/// source and a NUL escape in its test both reached disk as raw bytes, <i>"which is how it was
/// noticed"</i>, because git stopped treating the files as text.</para>
/// <para>On 2026-09-21 this repository did it a THIRD time, in the C# port of that very module:
/// <c>CredentialWords.cs</c> shipped to <c>main</c> with one 0x00 byte inside
/// <c>var previous = '\0';</c>, and <c>git ls-files --eol</c> reported <c>i/-text</c> — git had been
/// treating a security-relevant source file as BINARY, with no diff, no blame and no three-way
/// merge. A subagent porting the file found it; nothing in the suite could.</para>
/// <para>Twice was a lesson written in a comment. A comment is not a check, and the third time is
/// what makes this a test. It is cheap — it reads the files a compile already reads — and it fails
/// on the day the byte arrives rather than on the day somebody happens to run <c>cat -A</c>.</para>
/// </remarks>
public sealed class NoSourceFileCarriesAControlByteTests
{
    /// <summary>
    /// Every byte a source file may not contain, and why each one is on the list.
    /// </summary>
    /// <remarks>
    /// <para>EVERY C0 byte except the three that mean something — tab, line feed, carriage return —
    /// and DEL. It was NUL and DEL alone, on the reasoning that those are the two that make git call
    /// a file binary. <b>The fourth occurrence proved the reasoning too narrow.</b> On 2026-09-21 a
    /// regex escape written in a comment reached <c>.github/workflows/ci.yml</c> as byte
    /// <c>0x08</c> — git is perfectly happy with it and YAML is not. The workflow stopped parsing,
    /// so its three REQUIRED checks never reported at all, and the pull request sat blocked for
    /// hours with every check a person could SEE passing. A byte that breaks a parser is worse than
    /// one that breaks a diff, because nothing says so.</para>
    /// <para>Tab, LF and CR are excluded because they are ordinary text — CR is how half the
    /// checkouts on this machine store a line ending. Everything else in the range is a byte nobody
    /// types on purpose, and each one of them has now cost this repository something.</para>
    /// </remarks>
    private static readonly byte[] Forbidden =
    [
        .. Enumerable.Range(0, 32).Select(b => (byte)b).Where(b => b is not (0x09 or 0x0A or 0x0D)),
        0x7F,
    ];

    public static TheoryData<string> EverySourceFile() => FilesUnder(RepositoryRoot());

    /// <summary>The same walk, over any root — which is what lets a test point it at a known file.</summary>
    internal static TheoryData<string> FilesUnder(string root)
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            // Everything a person writes, and nothing a tool generates: `bin`, `obj` and
            // `node_modules` hold real binaries by the thousand, and a test that walked them would
            // be slow AND wrong.
            if (Skipped(file) || !Watched(Path.GetExtension(file)))
            {
                continue;
            }

            data.Add(Path.GetRelativePath(root, file));
        }

        return data;
    }

    /// <summary>The check itself, over any root, so the companion below runs the real one.</summary>
    internal static IReadOnlyList<byte> ForbiddenBytesIn(string root, string relative) =>
        [.. Forbidden.Where(File.ReadAllBytes(Path.Combine(root, relative)).Contains)];

    [Theory]
    [MemberData(nameof(EverySourceFile))]
    public void ASourceFile_CarriesNoRawNulOrDelByte(string relative)
    {
        var found = ForbiddenBytesIn(RepositoryRoot(), relative);

        found.Should().BeEmpty(
            "{0} carries a raw {1} byte. An escape was written as source and reached disk as the "
            + "BYTE — git then treats the file as binary (no diff, no blame, no three-way merge) "
            + "and nothing else in this suite would say so. Spell it by NUMBER, or restructure so "
            + "no sentinel character is needed at all.",
            relative,
            string.Join(" and ", found.Select(b => $"0x{b:X2}")));
    }

    [Fact]
    public void TheScanFindsFilesAtAll_SoAWalkThatMatchesNothingCannotPass()
    {
        // The companion, because a structural scan that matches nothing passes for ever. If a path
        // change ever makes the walk start above the repository or below it, this is what says so
        // rather than a theory with zero cases quietly reporting success.
        var files = EverySourceFile();

        files.Count.Should().BeGreaterThan(200, "the scan found almost no source files, so it is "
            + "looking in the wrong place and the theory above is asserting nothing");
    }

    [Fact]
    public void TheScanItselfFindsAPlantedByte_NotACopyOfItsCheck()
    {
        // THE COMPANION, and a reviewer was right that the first version was not one. It wrote a
        // temporary file and then read it with its own `ReadAllBytes(...).Contains(...)` — a copy of
        // the check, not the check — so the scan could have stopped enumerating files entirely and
        // this would still have passed. It drives the REAL walk over a tree it plants now: the file
        // has to be found by `FilesUnder` and refused by `ForbiddenBytesIn`, which are the two
        // halves the theory above is made of.
        var root = Directory.CreateTempSubdirectory("coai-control-byte-").FullName;
        try
        {
            var clean = Path.Combine(root, "Ordinary.cs");
            var dirty = Path.Combine(root, "Planted.cs");
            // The byte the OLD list missed, in the file type it missed it in: a workflow, where it
            // costs a parser rather than a diff.
            var workflow = Path.Combine(root, "planted.yml");
            File.WriteAllText(clean, "var previous = 'a';");
            File.WriteAllBytes(dirty, [.. Encoding.UTF8.GetBytes("var previous = '"), 0x00]);
            File.WriteAllBytes(workflow, [.. Encoding.UTF8.GetBytes("# the escape was written as "), 0x08]);

            var found = FilesUnder(root);

            found.Should().HaveCount(3, "the walk must find all three files it was given");
            ForbiddenBytesIn(root, "Planted.cs").Should().Equal([(byte)0x00],
                "the scan must SEE the byte in a file that carries one");
            ForbiddenBytesIn(root, "planted.yml").Should().Equal([(byte)0x08],
                "and the one that got past the old list, in the file type it got past it in");
            ForbiddenBytesIn(root, "Ordinary.cs").Should().BeEmpty(
                "and must not report one in a file that does not — a check that answered yes to "
                + "everything would pass the line above and mean nothing");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool Watched(string extension) =>
        extension is ".cs" or ".ts" or ".mjs" or ".js" or ".json" or ".md" or ".csproj" or ".props"
            or ".yml" or ".yaml" or ".sh" or ".razor";

    /// <summary>Directories nobody writes by hand — build output, dependencies, the git store.</summary>
    private static readonly string[] NotWritten =
        ["bin", "obj", "node_modules", ".git", ".vscode-test", "out", "dist"];

    /// <summary>
    /// Whether a path runs through a directory nobody writes by hand.
    /// </summary>
    /// <remarks>
    /// One predicate over a named list rather than a chain of seven <c>||</c>, which put this method
    /// past the cyclomatic ceiling the C# rules set at four. (CodeRabbit, on the pull request.)
    /// </remarks>
    private static bool Skipped(string file) =>
        NotWritten.Any(directory => file.Contains(
            $"{Path.DirectorySeparatorChar}{directory}{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// The checkout this test assembly was built from — <see cref="ProductionSources"/>'s, not a
    /// second copy.
    /// </summary>
    /// <remarks>
    /// It used to live here and the shared scanner called it, which made a utility depend on a
    /// particular test class existing. The code round was right that this is an ownership the name
    /// does not admit to; the walk moved to the scanner and this is the one caller that kept its
    /// name. (Story 2.1's code round, codex.)
    /// </remarks>
    internal static string RepositoryRoot() => ProductionSources.RepositoryRoot();
}
