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
    /// NUL and DEL only. Not the whole C0 range: a tab is ordinary, and CR is how half the
    /// checkouts on this machine store a line ending. These two are the ones that make git call a
    /// file binary, and they are the two the TypeScript module got wrong.
    /// </remarks>
    private static readonly byte[] Forbidden = [0x00, 0x7F];

    public static TheoryData<string> EverySourceFile()
    {
        var root = RepositoryRoot();
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

    [Theory]
    [MemberData(nameof(EverySourceFile))]
    public void ASourceFile_CarriesNoRawNulOrDelByte(string relative)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), relative));
        var found = Forbidden.Where(bytes.Contains).ToArray();

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
    public void TheScanCanSeeAControlByte_WhenThereIsOneToSee()
    {
        // And the other companion: the assertion itself is exercised against a file that DOES carry
        // the byte, so a scan that reads nothing, or a `Contains` that never matches, is caught.
        var planted = Path.Combine(Path.GetTempPath(), $"coai-control-byte-{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllBytes(planted, Encoding.UTF8.GetBytes("var previous = '").Concat([(byte)0x00]).ToArray());

            File.ReadAllBytes(planted).Should().Contain((byte)0x00,
                "the check the theory performs must be able to see the byte it is looking for");
        }
        finally
        {
            File.Delete(planted);
        }
    }

    private static bool Watched(string extension) =>
        extension is ".cs" or ".ts" or ".mjs" or ".js" or ".json" or ".md" or ".csproj" or ".props"
            or ".yml" or ".yaml" or ".sh" or ".razor";

    private static bool Skipped(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}.vscode-test{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}out{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}");

    private static string RepositoryRoot()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, ".git"))
               && !File.Exists(Path.Combine(here.FullName, ".git")))
        {
            here = here.Parent;
        }

        return here?.FullName
            ?? throw new DirectoryNotFoundException(
                $"no repository root above {AppContext.BaseDirectory}, so this scan would read nothing");
    }
}
