using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// No test holds two programs level by reading the other one's SOURCE.
/// </summary>
/// <remarks>
/// <para>Two of them did, for a good reason: the panel's picker is a claim about what the server
/// will run, and a test that recomputes the answer with the panel's own function is worth nothing —
/// a defect this repository shipped for a day. The cure was a regular expression over the server's
/// catalog source in one direction and over <c>prompts.ts</c> in the other.</para>
/// <para><b>A regular expression over source goes QUIET rather than red.</b> It breaks on a
/// reformat, it is blind to a field it was not taught, and when it stops matching it returns an
/// empty list that every assertion below passes over. Both are gone: each half asserts its own
/// loader against <c>shared/builtin-roles.json</c>, a file neither half owns. This is the guard that
/// says so, because the shape is tempting and it will be reached for again. (codex, story C2's plan
/// round, which asked for exactly this check.)</para>
/// <para>Its first draft had the defect it exists to prevent, and its own gate round said so: it
/// matched only SINGLE-quoted path literals, which C# cannot have at all — a multi-character
/// single-quoted literal is a char and does not compile — so no C# test reading the extension's
/// source could ever have tripped it. It also called every file outside <c>src_vs_code</c> an MCP
/// file, so a Team-server test reading the MCP's source was compared against the wrong owner and
/// passed. Both are fixed below, and the teeth check plants a reader in each language.</para>
/// </remarks>
public sealed class NothingReadsAnotherProgramsSourceTests
{
    /// <summary>The three programs, by the directory each one lives in.</summary>
    private static readonly string[] Programs = ["src_vs_code", "src_mcp", "src_server"];

    /// <summary>Directories whose contents are nobody's source.</summary>
    private static readonly string[] NotSource = ["node_modules", "bin", "obj", "out", "dist", ".git"];

    /// <summary>
    /// The retired class's name, assembled rather than written.
    /// </summary>
    /// <remarks>
    /// A guard that scans for a word cannot hold that word, or it reports itself — which it did on
    /// its first run, and which is the cheapest possible demonstration that the scan works. Spelling
    /// it in two halves is the ordinary shape for a self-referencing check; exempting this file by
    /// name would also stop the guard noticing a real reintroduction inside it.
    /// </remarks>
    private const string Retired = "Prompt" + "Catalog";

    /// <summary>Anything that opens a file, in either language.</summary>
    private static readonly string[] Reads =
    [
        "File.ReadAllText", "File.ReadAllLines", "File.ReadAllBytes", "File.OpenRead", "File.OpenText",
        "readFileSync", "readFile", "createReadStream", "openSync",
    ];

    /// <summary>
    /// A line that only TALKS about something.
    /// </summary>
    /// <remarks>
    /// Both languages mark one the same way, and <c>///</c> is caught by the <c>//</c> test rather
    /// than needing its own. A comment naming a retired class is history, and history that may not
    /// name the thing it is about is not history.
    /// </remarks>
    private static bool IsComment(string line) =>
        line.TrimStart() is var start
        && (start.StartsWith("//", StringComparison.Ordinal)
            || start.StartsWith("*", StringComparison.Ordinal)
            || start.StartsWith("/*", StringComparison.Ordinal));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src_vs_code")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("the repository root was not found from the test binary");
    }

    /// <summary>
    /// Every file under a directory, with the noise pruned BEFORE it is walked.
    /// </summary>
    /// <remarks>
    /// A filter applied AFTER the walk still walks <c>node_modules</c> in full —
    /// tens of thousands of files on a machine where the extension's dependencies are installed, and
    /// seconds of disk on every run of this suite. This descends by hand so it can decline to.
    /// (gemini, story C2's code round.)
    /// </remarks>
    private static IEnumerable<string> SourceFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (NotSource.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in SourceFiles(child))
            {
                yield return file;
            }
        }
    }

    /// <summary>Which program a path belongs to, or empty for one outside all of them.</summary>
    /// <remarks>
    /// Asked of every root rather than assumed. The first draft read "not the extension, therefore
    /// the MCP", which made every Team-server test claim an ownership it does not have: a server
    /// test reading the MCP's source compared <c>src_mcp</c> with <c>src_mcp</c> and was waved
    /// through, while its own internal reads were flagged. (codex and gemini, C2's code round.)
    /// </remarks>
    private static string ProgramOf(string path) =>
        Programs.FirstOrDefault(p => path.Contains($"{Path.DirectorySeparatorChar}{p}{Path.DirectorySeparatorChar}"))
        ?? string.Empty;

    /// <summary>
    /// Whether this line names that program as a PATH SEGMENT.
    /// </summary>
    /// <remarks>
    /// Quoted any of the three ways these two languages quote, or spelled inside a path with
    /// separators. The first draft asked for <c>'src_mcp'</c> alone.
    /// </remarks>
    private static bool Names(string line, string program) =>
        line.Contains($"\"{program}\"", StringComparison.Ordinal)
        || line.Contains($"'{program}'", StringComparison.Ordinal)
        || line.Contains($"`{program}`", StringComparison.Ordinal)
        || line.Contains($"/{program}/", StringComparison.Ordinal)
        || line.Contains($"\\{program}\\", StringComparison.Ordinal);

    /// <summary>Every test file of every program, with the program that owns it.</summary>
    private static IEnumerable<(string File, string Mine)> TestFiles()
    {
        var root = RepoRoot();

        return from program in Programs
               from file in SourceFiles(Path.Combine(root, program))
               where (Path.GetExtension(file) is ".cs" or ".ts" or ".mjs")
                     && IsUnderATestFolder(file)
               select (file, ProgramOf(file));
    }

    /// <summary>A file inside a folder called <c>test</c> or <c>tests</c>, at any depth.</summary>
    private static bool IsUnderATestFolder(string file)
    {
        var dir = Path.GetDirectoryName(file);
        while (dir is not null)
        {
            if (Path.GetFileName(dir) is "test" or "tests")
            {
                return true;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return false;
    }

    [Fact]
    public void NoTest_ReachesIntoAnotherProgramsSourceFiles()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var (file, mine) in TestFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (IsComment(line) || !Reads.Any(r => line.Contains(r, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (Programs.Any(other => other != mine && Names(line, other)))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "a test that parses another program's source goes quiet rather than red — assert your "
            + "own loader against shared/builtin-roles.json instead");
    }

    [Fact]
    public void TheRetiredCatalog_IsGone_FromSourceAsWellAsFromTheBuild()
    {
        // The class compiles nowhere, so a reference in a .cs would already be a red build — but a
        // reference in a generator, a script or a settings file would not be, and those are exactly
        // where a retired name survives. `research/` is not scanned at all, for the same reason
        // comments are exempt: both record what the system WAS as well as what it is.
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var dir in (string[])["src_mcp", "src_server", "src_vs_code", "shared"])
        {
            foreach (var file in SourceFiles(Path.Combine(root, dir)))
            {
                if (Path.GetExtension(file) is not (".cs" or ".ts" or ".mjs" or ".json"))
                {
                    continue;
                }

                if (File.ReadAllLines(file).Any(l => !IsComment(l) && l.Contains(Retired, StringComparison.Ordinal)))
                {
                    offenders.Add(Path.GetRelativePath(root, file));
                }
            }
        }

        offenders.Should().BeEmpty($"{Retired} was deleted; the seed is the only catalog now");
    }
}
