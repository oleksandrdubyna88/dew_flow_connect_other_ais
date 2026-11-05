using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An assertion that says "never under TEMP" compares against the MACHINE's temp folder, <see cref="RunTemp.Real"/> — never against
/// <c>Path.GetTempPath()</c>, which is the run's own folder (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, D2/J2, scenario S5b).
/// </summary>
/// <remarks>
/// <para>Under the per-run temp folder, <c>Path.GetTempPath()</c> is a folder deep inside the machine's. A guard that compares a
/// <c>LocalApplicationData</c> path against it stays GREEN and can no longer notice a root that moved into the real temp folder — a test that is
/// right for the wrong reason.</para>
/// <para>A source scan on <see cref="TestSourceIndex"/> (comments and string contents stripped): the argument of every
/// <c>NotStartWith(</c> / <c>NotContain(</c> in the test tree that holds <c>GetTempPath()</c> and not <c>RunTemp.Real</c> is an offender, named
/// with its file and line. <b>Its limits:</b> only those two assertions; a path put into a local first (<c>var temp = Path.GetTempPath();</c>) and
/// asserted from the local is NOT seen; the line comes from the raw text and names the line that holds <c>GetTempPath()</c> beside the assertion.
/// It cannot go quiet: it must still find the two guards the plan names.</para>
/// </remarks>
public sealed class TheNeverUnderTempGuardsTests
{
    private static readonly Regex NeverAssertion = new(@"\.\s*(?:NotStartWith|NotContain)\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex TheRunsTemp = new(@"\bGetTempPath\s*\(\s*\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex TheRealTemp = new(@"\bRunTemp\s*\.\s*Real\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>One "never under temp" assertion, and which temp folder its argument means.</summary>
    private sealed record Guard(string File, string Class, bool MeansTheRealTemp);

    /// <summary>The text between the parenthesis that opens at <paramref name="open"/> and the one that closes it.</summary>
    private static string ArgumentAt(string code, int open)
    {
        var depth = 0;
        for (var at = open; at < code.Length; at++)
        {
            depth += code[at] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return code[(open + 1)..at];
            }
        }

        return code[(open + 1)..];
    }

    private static IEnumerable<Guard> GuardsAboutTemp(IEnumerable<TestClass> classes) =>
        classes
            .Where(c => !c.IsNested)
            .SelectMany(c => NeverAssertion.Matches(c.Code)
                .Select(m => ArgumentAt(c.Code, m.Index + m.Length - 1))
                .Where(argument => TheRunsTemp.IsMatch(argument) || TheRealTemp.IsMatch(argument))
                .Select(argument => new Guard(c.File, c.Name, TheRealTemp.IsMatch(argument))));

    private static List<string> Offenders(IEnumerable<TestClass> classes) =>
        [.. GuardsAboutTemp(classes).Where(g => !g.MeansTheRealTemp).Select(g => $"{g.File}: {g.Class}").Distinct()];

    /// <summary>Every <c>file:line</c> of a file that holds <c>GetTempPath()</c> on or right after a line with a never-assertion.</summary>
    private static IEnumerable<string> LinesOf(string file)
    {
        var path = Directory.EnumerateFiles(Path.Combine(RepoPaths.Root(), "src_mcp", "tests"), file, SearchOption.AllDirectories).First();
        var lines = File.ReadAllLines(path);

        return Enumerable.Range(0, lines.Length)
            .Where(i => NeverAssertion.IsMatch(lines[i]) && lines.Skip(i).Take(3).Any(TheRunsTemp.IsMatch))
            .Select(i => $"{file}:{i + 1}");
    }

    [Fact]
    public void TheNeverUnderTempGuards_CompareAgainstTheRealTemp()
    {
        var offenders = Offenders(TestSourceIndex.Classes);

        string.Join("; ", offenders.SelectMany(o => LinesOf(o.Split(':')[0]))).Should().BeEmpty(
            "under the run's own temp folder Path.GetTempPath() is a folder INSIDE the machine's: a 'never under temp' guard must compare against RunTemp.Real, or it stays green and blind");
        offenders.Should().BeEmpty();
    }

    [Fact]
    public void TheScan_StillSeesTheTwoGuardsItKnowsAreThere()
    {
        var real = GuardsAboutTemp(TestSourceIndex.Classes).Where(g => g.MeansTheRealTemp).Select(g => g.Class).ToList();

        real.Should().Contain(["AntigravityReadOnlyTests", "KimiCredentialsLockTests"], "the two guards of D2/J2 compare against RunTemp.Real, and the scan must find them");
    }

    [Fact]
    public void AGuardOnThePathOfTheRun_IsAnOffender_AndAGuardOnTheRealTempIsNot_WhateverTheWrapping()
    {
        var classes = TestSourceIndex.Parse("Synthetic.cs", """
            public sealed class Blind { void A() => Path.Should().NotStartWith(Path.GetTempPath()); }
            public sealed class BlindToo { void A() => Path.Should().NotContain(
                Path.GetTempPath().TrimEnd('/'), "why"); }
            public sealed class Sees { void A() => Path.Should().NotStartWith(RunTemp.Real); }
            public sealed class Unrelated { void A() => Path.Should().NotStartWith(Elsewhere()); }
            public sealed class Talks { void A() { var s = ".NotStartWith(Path.GetTempPath())"; /* x.NotStartWith(Path.GetTempPath()) */ } }
            """);

        Offenders(classes).Should().BeEquivalentTo(["Synthetic.cs: Blind", "Synthetic.cs: BlindToo"]);
    }
}
