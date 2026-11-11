using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// A running job's working directory is never a test run's to sweep.
/// </summary>
/// <remarks>
/// <para>No test runner sweeps a <c>coai-*</c> name (E5 S2-A); the list is what the one-off clean-up refuses. A job's directory —
/// <c>coai-server-job-</c> — sits in the machine's temp folder for as long as a vendor takes to answer. <c>shared/temp-sweep.json</c>
/// names it as the product's own; this holds that list against what this program actually makes, so a new working directory that nobody
/// adds to it is red here rather than offered to a person as a leftover beside a live job on a developer's machine.</para>
/// <para>A scan of this program's own source that cannot go quiet: it must find the prefix it knows
/// is there, so a reformat that stops it matching is red, not an empty list. coai-mcp's suite and the
/// extension hold their own halves.</para>
/// </remarks>
public sealed class TheTestSweepLeavesAJobAloneTests
{
    [Fact]
    public void EveryTempDirectoryThisProgramMakes_IsNamedInTheSharedRule()
    {
        var program = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        // A scan pattern, not a call: the API name is data here, written with a tilde that is removed at run time, as PlantedLines does (src_mcp/tests/Leftovers/LeftoversBaselineTests.cs).
        var literal = new Regex(@"Create~TempSubdirectory\(\s*""(coai-[a-z-]+)""\s*\)".Replace("~", string.Empty, StringComparison.Ordinal));
        var made = Directory.EnumerateFiles(Path.Combine(program, "src"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => literal.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        using var rule = JsonDocument.Parse(File.ReadAllText(Path.Combine(program, "..", "shared", "temp-sweep.json")));
        var neverSwept = rule.RootElement.GetProperty("neverSwept").EnumerateArray().Select(e => e.GetString()).ToList();

        made.Should().Contain("coai-server-job-", "the scan must still see what it knows is there");
        neverSwept.Should().Contain(made, "a job's directory is the server's, swept on the server's clock");
    }
}
