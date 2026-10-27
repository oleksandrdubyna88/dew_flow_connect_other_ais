using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The fixture cutter pins the answer of a kept write call, and the fixture guard checks that every write call has its answer:
/// both must mean the same tools. <c>WRITE_TOOLS</c> in <c>scripts/lib/transcript-sanitise.mjs</c> is the set
/// <see cref="ACallerTranscriptFixtureCarriesNoTextTests.WriteTools"/> names.
/// </summary>
/// <remarks>
/// <para>The script's list is a copy (a Node script cannot load this assembly), and two lists nothing compares drift: a tool
/// added to the cutter only is a call the guard never checks, and a tool added to the guard only is a call whose answer the
/// cutter drops and the guard then refuses (the E1 fixtures' code round, vibe and codex, architecture). This is the one live
/// check, shaped like <see cref="TheProbeScriptsMinimalEnvironmentIsTheProductsTests"/>.</para>
/// <para>The parse fails LOUDLY rather than going quiet: a declaration it cannot find, or one that yields no names, is a failure
/// with the file named, never an empty list compared with nothing.</para>
/// </remarks>
public sealed class TheCuttersWriteToolsAreTheGuardsTests
{
    private const string Declaration = "const WRITE_TOOLS = new Set(";

    /// <summary>The names in the script's <c>WRITE_TOOLS</c> set literal, or a failure naming what was not found.</summary>
    internal static IReadOnlyList<string> NamesIn(string script, string where)
    {
        var at = script.IndexOf(Declaration, StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, $"{where} must declare `{Declaration}[ … ]);` — the tools whose answers the cutter pins");
        var open = script.IndexOf('[', at);
        var close = script.IndexOf("])", open, StringComparison.Ordinal);
        (open > at && close > open).Should().BeTrue($"{where}'s WRITE_TOOLS must be one array literal inside new Set(...)");
        var names = Regex.Matches(script[open..close], "'([^']+)'").Select(match => match.Groups[1].Value).ToList();
        names.Should().NotBeEmpty($"{where}'s WRITE_TOOLS must name at least one tool");

        return names;
    }

    [Fact]
    public void TheCuttersList_IsTheGuardsList()
    {
        // reads-another-program: scripts/lib/transcript-sanitise.mjs is the fixture cutter, not one of the three programs; its
        // WRITE_TOOLS must equal the guard's WriteTools, and a missing or empty list fails loudly above.
        var path = Path.Combine(RepoPaths.Root(), "scripts", "lib", "transcript-sanitise.mjs");
        var names = NamesIn(File.ReadAllText(path), "scripts/lib/transcript-sanitise.mjs");

        names.Should().OnlyHaveUniqueItems();
        names.Should().BeEquivalentTo(ACallerTranscriptFixtureCarriesNoTextTests.WriteTools,
            "the cutter pins the answers of exactly the calls the guard checks — change transcript-sanitise.mjs and the guard together");
    }

    [Fact]
    public void ADroppedName_IsSeen_AndAMissingDeclaration_IsAFailure_NotAnEmptyList()
    {
        const string Doctored = "const WRITE_TOOLS = new Set(['Write', 'Edit']);\n";
        NamesIn(Doctored, "doctored").Should().Equal("Write", "Edit");

        var missing = () => NamesIn("const OTHER_TOOLS = new Set(['Write']);\n", "doctored");
        missing.Should().Throw<Exception>().WithMessage("*must declare*");

        var empty = () => NamesIn("const WRITE_TOOLS = new Set([]);\n", "doctored");
        empty.Should().Throw<Exception>().WithMessage("*at least one*");
    }
}
