using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every mode of <c>coai-mcp</c> says what it does with a NAMED data folder that is missing
/// (research/PLAN_names_and_a_missing_root.md): it is a writer the dispatch refuses (<c>Program.WriterModes</c>), a mode the
/// reader test runs against a missing root, serve (<c>Program.ServeRefusal</c>), or a mode whose code never reaches the data
/// directory, named below with why.
/// </summary>
/// <remarks>
/// The code gate's third round: <c>WriterModes</c> is a hand-kept list, so a NEW mode that writes under the data directory
/// would run unrefused and make the folder. This makes a new <c>Startup</c> value red until somebody decides which of the
/// four it is — the server-side twin of the extension's <c>everyServerModeTheExtensionLaunches.test.ts</c>, which only sees
/// the modes the extension launches.
/// </remarks>
public sealed class EveryModeIsClassifiedForAMissingNamedRootTests
{
    /// <summary>Modes whose code never opens, reads or writes the data directory, measured by reading it — and why.</summary>
    private static readonly IReadOnlyDictionary<Program.Startup, string> NeverReachesTheDataDirectory = new Dictionary<Program.Startup, string>
    {
        [Program.Startup.AskLocal] = "a reviewer shim the server launches: reads --prompt-file, writes --out (Program.AskLocalAsync)",
        [Program.Startup.AskRemote] = "a reviewer shim the server launches: talks to a Team server, writes --out (AskRemote.cs)",
        [Program.Startup.AskApi] = "a reviewer shim the server launches: calls an endpoint, writes --out (AskApiMode.cs)",
        [Program.Startup.AgyHook] = "the Antigravity tool-use hook: stdin to a decision on stdout (AntigravityReadOnly)",
        [Program.Startup.Normalize] = "reads --in, writes --out (NormalizeMode.cs)",
        [Program.Startup.Outline] = "reads one source file, prints its outline (OutlineMode.cs)",
        [Program.Startup.Help] = "prints the usage text",
        [Program.Startup.Usage] = "an unknown argument: prints why and exits 64",
    };

    /// <summary>The flags the reader test runs against a missing root, read from its source — every literal it passes.</summary>
    private static IReadOnlySet<Program.Startup> HeldByTheReaderTest()
    {
        var source = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src_mcp", "tests", "AReaderModeNeverMakesTheDataDirectoryTests.cs"));

        return Regex.Matches(source, "\"(--[a-z-]+)\"").Select(match => Program.Classify([match.Groups[1].Value])).ToHashSet();
    }

    [Fact]
    public void EveryMode_IsAWriter_AReaderTheTestRuns_Serve_OrNamedHereWithWhy()
    {
        var readers = HeldByTheReaderTest();
        var unclassified = Enum.GetValues<Program.Startup>()
            .Where(mode => !Program.WriterModes.Contains(mode)
                && !readers.Contains(mode)
                && mode != Program.Startup.Serve
                && !NeverReachesTheDataDirectory.ContainsKey(mode))
            .ToList();

        unclassified.Should().BeEmpty(
            "a mode that is neither refused as a writer, nor run by the reader test against a missing root, nor serve, nor "
            + "named here with why, could make a missing named data folder unseen — classify it");
    }

    [Fact]
    public void NoMode_IsBothAWriterAndAReaderOrNamedAsNeverReachingIt()
    {
        var readers = HeldByTheReaderTest();

        Program.WriterModes.Where(mode => readers.Contains(mode) || NeverReachesTheDataDirectory.ContainsKey(mode))
            .Should().BeEmpty("a writer the dispatch refuses cannot also be a mode that makes nothing");
    }
}
