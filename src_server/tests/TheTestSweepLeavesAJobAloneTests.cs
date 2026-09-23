using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// A running job's working directory is never a test run's to sweep.
/// </summary>
/// <remarks>
/// <para>Both test runners sweep <c>coai-*</c> directories older than ten minutes from the machine's
/// temp folder before they start, and a job's directory — <c>coai-server-job-</c> — sits there for as
/// long as a vendor takes to answer. <c>shared/temp-sweep.json</c> names it as the product's own; this
/// holds that list against what this program actually makes, so a new working directory that nobody
/// adds to it is red here rather than deleted under a live job on a developer's machine.</para>
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
        var literal = new Regex(@"CreateTempSubdirectory\(\s*""(coai-[a-z-]+)""\s*\)");
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
