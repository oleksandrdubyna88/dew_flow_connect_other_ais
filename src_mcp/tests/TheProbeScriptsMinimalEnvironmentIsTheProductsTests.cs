using System.Text.RegularExpressions;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The measurement scripts launch Vibe and Kimi in the SAME minimal environment the product does: <c>MINIMAL_WINDOWS</c>
/// in <c>scripts/lib/vibe.mjs</c> is the set <see cref="ProcessEnvironment.MinimalFor"/> answers for Windows.
/// </summary>
/// <remarks>
/// <para>The script's list is a copy of a C# list (a Node script cannot load the assembly), and a copy nothing checks is
/// two lists the day one of them moves — every confinement cell would then measure a launch the product no longer makes
/// (epic 2's code round, codex: "enumerate, never retype; one live check compares the two sides"). This is the one live
/// check. <c>scripts/lib/kimi.mjs</c> and every other script import the list from <c>vibe.mjs</c>; none retypes it.</para>
/// <para>The parse fails LOUDLY rather than going quiet (the reason <see cref="NothingReadsAnotherProgramsSourceTests"/>
/// exists): a declaration it cannot find, or one that yields no names, is a failure with the file named, never an empty
/// list compared with nothing.</para>
/// </remarks>
public sealed class TheProbeScriptsMinimalEnvironmentIsTheProductsTests
{
    private const string Declaration = "export const MINIMAL_WINDOWS";

    /// <summary>The names in the script's <c>MINIMAL_WINDOWS</c> array literal, or a failure naming what was not found.</summary>
    internal static IReadOnlyList<string> NamesIn(string script, string where)
    {
        var at = script.IndexOf(Declaration, StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, $"{where} must declare `{Declaration} = [ … ];` — the list the probes launch with");
        var open = script.IndexOf('[', at);
        var close = script.IndexOf("];", open, StringComparison.Ordinal);
        (open > at && close > open).Should().BeTrue($"{where}'s {Declaration} must be one array literal");
        var names = Regex.Matches(script[open..close], "'([^']+)'").Select(match => match.Groups[1].Value).ToList();
        names.Should().NotBeEmpty($"{where}'s {Declaration} must name at least one variable");

        return names;
    }

    [Fact]
    public void TheScriptsList_IsTheProductsWindowsList()
    {
        // reads-another-program: scripts/lib/vibe.mjs is a measurement harness, not one of the three programs; its
        // MINIMAL_WINDOWS must equal ProcessEnvironment.MinimalWindows, and a missing or empty list fails loudly above.
        var path = Path.Combine(RepoPaths.Root(), "scripts", "lib", "vibe.mjs");
        var names = NamesIn(File.ReadAllText(path), "scripts/lib/vibe.mjs");

        names.Should().OnlyHaveUniqueItems();
        names.Should().BeEquivalentTo(ProcessEnvironment.MinimalFor(isWindows: true),
            "the probes measure the launch the product makes — change scripts/lib/vibe.mjs and ProcessEnvironment.cs together");
    }

    [Fact]
    public void ADroppedName_IsSeen_AndAMissingDeclaration_IsAFailure_NotAnEmptyList()
    {
        const string Doctored = "export const MINIMAL_WINDOWS = ['PATH', 'PATHEXT'];\n";
        NamesIn(Doctored, "doctored").Should().Equal("PATH", "PATHEXT");

        var missing = () => NamesIn("export const OTHER = ['PATH'];\n", "doctored");
        missing.Should().Throw<Exception>().WithMessage("*must declare*");

        var empty = () => NamesIn("export const MINIMAL_WINDOWS = [];\n", "doctored");
        empty.Should().Throw<Exception>().WithMessage("*at least one*");
    }
}
