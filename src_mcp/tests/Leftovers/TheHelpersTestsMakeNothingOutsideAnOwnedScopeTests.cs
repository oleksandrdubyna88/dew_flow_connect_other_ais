using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// No test of the leftovers helper asks the machine for a temp path (E5 S3, must-not-copy row 11). In coai a raw temp call inside a test lands in the
/// run folder and vanishes with it, so a run can never see one: only a read of the source can. A helper test makes everything through
/// <see cref="DarwinLayout.FreshOwnedScope"/> or an owned folder. The repository-wide guard of S6 supersedes this check; until then it is the only one.
/// </summary>
public sealed class TheHelpersTestsMakeNothingOutsideAnOwnedScopeTests
{
    /// <summary>The temp APIs, each spelled in two pieces so this file does not match its own patterns.</summary>
    private static readonly Regex[] TempApis =
    [
        Api("GetTemp" + "Path"),
        Api("Create" + "TempSubdirectory"),
        Api("GetTemp" + "FileName"),
        new Regex(@"GetEnvironmentVariable\(\s*""(TEMP|TMP|TMPDIR)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
    ];

    private static Regex Api(string name) => new(@"\b" + name + @"\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The offending lines of the given source files, as <c>file:line</c>.</summary>
    internal static IReadOnlyList<string> Offenders(IReadOnlyDictionary<string, string> sources) =>
        [.. sources.SelectMany(file => file.Value.Split('\n')
            .Select((text, index) => (Where: $"{file.Key}:{index + 1}", Text: text))
            .Where(line => TempApis.Any(api => api.IsMatch(line.Text)))
            .Select(line => line.Where))];

    private static Dictionary<string, string> TheHelpersTests() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src_mcp", "tests", "Leftovers"), "*.cs")
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllText);

    [Fact]
    public void NoHelperTestAsksTheMachineForATempPath()
    {
        var sources = TheHelpersTests();

        sources.Count.Should().BeGreaterThanOrEqualTo(13, "the scan must have read the helper's tests, not nothing");
        Offenders(sources).Should().BeEmpty("a helper test makes its folders through an owned scope, never a raw temp call");
    }

    [Fact]
    public void TheScan_SeesEachTempApi_AndPassesAnOwnedScope()
    {
        Offenders(new Dictionary<string, string>
        {
            ["a.cs"] = "var p = System.IO.Path." + "GetTemp" + "Path();",
            ["b.cs"] = "Directory." + "Create" + "TempSubdirectory(\"x\");",
            ["c.cs"] = "Path." + "GetTemp" + "FileName();",
            ["d.cs"] = "Environment.GetEnvironmentVariable(\"TMP" + "DIR\");",
            ["e.cs"] = "using var scope = DarwinLayout.FreshOwnedScope();",
        }).Should().Equal("a.cs:1", "b.cs:1", "c.cs:1", "d.cs:1");
    }
}
