using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// No test holds two programs level by reading the other one's SOURCE.
/// </summary>
/// <remarks>
/// <para>Two of them did, for a good reason: the panel's picker is a claim about what the server
/// will run, and a test that recomputes the answer with the panel's own function is worth nothing —
/// a defect this repository shipped for a day. The cure was a regular expression over
/// <c>PromptCatalog.cs</c> in one direction and over <c>prompts.ts</c> in the other.</para>
/// <para><b>A regular expression over source goes QUIET rather than red.</b> It breaks on a
/// reformat, it is blind to a field it was not taught, and when it stops matching it returns an
/// empty list that every assertion below passes over. Both are gone: each half asserts its own
/// loader against <c>shared/builtin-roles.json</c>, a file neither half owns. This is the guard that
/// says so, because the shape is tempting and it will be reached for again. (codex, story C2's plan
/// round, which asked for exactly this check.)</para>
/// <para>It reads the repository rather than the build output, and it is written to name the file
/// and the line it objects to, because "somewhere in the test suite" is not an instruction.</para>
/// </remarks>
public sealed class NothingReadsAnotherProgramsSourceTests
{
    /// <summary>The other program, as each side would have to spell it to read it.</summary>
    private static readonly string[] TheOtherProgram = ["src_vs_code", "src_mcp", "src_server"];

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

    /// <summary>
    /// A line that only TALKS about something. Both languages here mark one the same way, and the
    /// doc-comment forms (<c>///</c>, <c>*</c>) are the ones these files actually use.
    /// </summary>
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

    /// <summary>Every test file of either half, by path.</summary>
    private static IEnumerable<string> TestFiles()
    {
        var root = RepoRoot();
        foreach (var (dir, pattern) in ((string, string)[])
                 [(Path.Combine("src_mcp", "tests"), "*.cs"),
                  (Path.Combine("src_server", "tests"), "*.cs"),
                  (Path.Combine("src_vs_code", "src", "test"), "*.ts")])
        {
            var path = Path.Combine(root, dir);
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
                {
                    yield return file;
                }
            }
        }
    }

    [Fact]
    public void NoTest_ReachesIntoTheOtherHalfsSourceFiles()
    {
        // A path to the other half's source, in a line that also opens a file. Naming the half in a
        // COMMENT is how these tests explain themselves and must stay allowed; reading it is what
        // must not come back.
        var offenders = new List<string>();
        foreach (var file in TestFiles())
        {
            var mine = file.Contains($"{Path.DirectorySeparatorChar}src_vs_code{Path.DirectorySeparatorChar}")
                ? "src_vs_code"
                : "src_mcp";
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var reads = !IsComment(line)
                    && (line.Contains("readFileSync") || line.Contains("File.ReadAllText") || line.Contains("File.ReadAllLines"));
                if (reads && TheOtherProgram.Any(other => other != mine && line.Contains($"'{other}'")))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "a test that parses the other program's source goes quiet rather than red — assert your "
            + "own loader against shared/builtin-roles.json instead");
    }

    [Fact]
    public void TheRetiredCatalog_IsGone_FromSourceAsWellAsFromTheBuild()
    {
        // The class compiles nowhere, so a reference in a .cs would already be a red build — but a
        // reference in a generator, a script or a settings file would not be, and those are exactly
        // where a retired name survives. Two exemptions, for the same reason: `research/` is not
        // scanned at all, and a COMMENT is not scanned either. Both record what the system WAS as
        // well as what it is, and a history that may not name the thing it is about is not history.
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var dir in (string[])["src_mcp", "src_server", "src_vs_code", "shared"])
        {
            var path = Path.Combine(root, dir);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}out{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}")
                    || Path.GetExtension(file) is not (".cs" or ".ts" or ".mjs" or ".json"))
                {
                    continue;
                }

                if (File.ReadAllLines(file).Any(l => !IsComment(l) && l.Contains(Retired)))
                {
                    offenders.Add(Path.GetRelativePath(root, file));
                }
            }
        }

        offenders.Should().BeEmpty($"{Retired} was deleted; the seed is the only catalog now");
    }
}
