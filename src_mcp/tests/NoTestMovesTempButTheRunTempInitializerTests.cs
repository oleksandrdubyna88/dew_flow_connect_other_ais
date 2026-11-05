using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The never-toggled proof of the per-run temp folder (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, E2's cornerstone area; judge J7): no test source
/// writes <c>TEMP</c>, <c>TMP</c> or <c>TMPDIR</c> of the PROCESS except <see cref="RunTemp"/>'s initializer (once, before any test) and
/// <c>AVibeHomesNeverLiveUnderTempTests</c> (in <c>fakecli-env</c>; it restores what it found, which is the run folder).
/// </summary>
/// <remarks>
/// <para>Why it matters: <c>Path.GetTempPath()</c> and every child's temp path are read from those variables at call time, so a write in the middle of a run moves what every
/// other class sees — the whole point of setting them ONCE. A source scan on <see cref="TestSourceIndex.Files"/> (comments gone, string contents kept, because the names ARE
/// literals): a file is a writer when it calls <c>SetEnvironmentVariable(</c> with one of the three names as its literal first argument, OR calls it and spells all three names as
/// literals (the loop-over-an-array form <c>AVibeHomesNeverLiveUnderTempTests</c> uses).</para>
/// <para><b>Limits:</b> a write whose name is built at run time (<c>"TE" + "MP"</c>) or passed through a helper is not seen; a child's environment given through a request
/// (<c>["TEMP"] = …</c>) is not a process write and is not flagged. The scan cannot go quiet: with an empty allow-list it must find <c>AVibeHomesNeverLiveUnderTempTests.cs</c>, and a
/// synthetic source with each spelling is named. Three files are on the allow-list by name besides the two writers, each for a stated reason (see <c>Allowed</c>): two spell the pattern in synthetic sources, and <c>ChildEnvironmentTests</c> lists the three names as allowlist data beside a write of its own variable — a heuristic that tells that from a loop would be more fragile than the exemption.</para>
/// </remarks>
public sealed class NoTestMovesTempButTheRunTempInitializerTests
{
    private static readonly Regex DirectWrite = new(
        @"SetEnvironmentVariable\s*\(\s*""(?:TEMP|TMP|TMPDIR)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex AnyWrite = new(@"SetEnvironmentVariable\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly string[] AllThree = ["\"TEMP\"", "\"TMP\"", "\"TMPDIR\""];

    /// <summary>The files that write the process's temp variables, by name, minus <paramref name="allowed"/>.</summary>
    private static List<string> Writers(IEnumerable<SourceFile> files, IReadOnlySet<string> allowed) =>
    [
        .. files
            .Where(f => !allowed.Contains(f.File) && (DirectWrite.IsMatch(f.Text) || (AnyWrite.IsMatch(f.Text) && AllThree.All(name => f.Text.Contains(name, StringComparison.Ordinal)))))
            .Select(f => f.File),
    ];

    private static readonly HashSet<string> Allowed = [
        "RunTemp.cs",
        "AVibeHomesNeverLiveUnderTempTests.cs",
        "ChildEnvironmentTests.cs", // sets ONE variable of its own (Name); the three names there are allowlist data it checks a child against
        "EveryClassThatSetsTheProcessEnvironmentTests.cs", // its synthetic sources spell the pattern
        nameof(NoTestMovesTempButTheRunTempInitializerTests) + ".cs", // likewise
    ];

    [Fact]
    public void NoTestSourceWritesTheProcessTempVariables_ButTheInitializerAndTheOneRestoringClass()
    {
        string.Join("; ", Writers(TestSourceIndex.Files, Allowed)).Should().BeEmpty(
            "Path.GetTempPath() and every child's temp path read these variables at call time: a write in the middle of a run moves what every other class sees");
    }

    [Fact]
    public void TheScan_StillSeesTheClassItKnowsMovesTemp()
    {
        Writers(TestSourceIndex.Files, new HashSet<string>()).Should().Contain("AVibeHomesNeverLiveUnderTempTests.cs", "it moves all three, in a loop, and restores them");
    }

    [Fact]
    public void ASourceThatWritesATempVariable_IsNamed_WhateverTheSpelling_AndAChildsRequestOrACommentIsNot()
    {
        var files = new[]
        {
            TestSourceIndex.ParseFile("Direct.cs", "class A { void B() { Environment.SetEnvironmentVariable(\"TEMP\", x); } }"),
            TestSourceIndex.ParseFile("Loop.cs", "class A { string[] N = [\"TEMP\", \"TMP\", \"TMPDIR\"]; void B() { foreach (var n in N) { Environment.SetEnvironmentVariable(n, x); } } }"),
            TestSourceIndex.ParseFile("Child.cs", "class A { object R = new { Environment = new Dictionary<string, string?> { [\"TEMP\"] = x, [\"TMP\"] = x, [\"TMPDIR\"] = x } }; }"),
            TestSourceIndex.ParseFile("Other.cs", "class A { void B() { Environment.SetEnvironmentVariable(\"FAKECLI_MODE\", x); } }"),
            TestSourceIndex.ParseFile("Talks.cs", "class A { /* Environment.SetEnvironmentVariable(\"TMP\", x) */ // Environment.SetEnvironmentVariable(\"TEMP\", x)\n }"),
            TestSourceIndex.ParseFile("Allowed.cs", "class A { void B() { Environment.SetEnvironmentVariable(\"TMPDIR\", x); } }"),
        };

        Writers(files, new HashSet<string> { "Allowed.cs" }).Should().BeEquivalentTo(["Direct.cs", "Loop.cs"]);
    }
}
