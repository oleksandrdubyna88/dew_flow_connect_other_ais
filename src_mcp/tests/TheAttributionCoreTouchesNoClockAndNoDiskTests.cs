using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The attribution core is pure: no clock, no disk, no environment, no process, no randomness (E4 §2). "No I/O" is only enforced in
/// the core by <c>ArchitectureTests</c> for process and network assemblies; the file system and the clock live inside the core library
/// set, so this scan holds the <c>Authors</c> folder to the rest.
/// </summary>
public sealed class TheAttributionCoreTouchesNoClockAndNoDiskTests
{
    private const string Folder = "src_mcp/core/Authors/";

    private static readonly string[] Forbidden =
    [
        "DateTime.Now", "DateTime.UtcNow", "DateTime.Today", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider",
        "TimeZoneInfo.Local", "Stopwatch", "Process.", "File.", "Directory.", "Environment.", "Guid.NewGuid", "new Random",
    ];

    private static IEnumerable<string> AuthorsFiles() =>
        ProductionSources.Files().Where(file => file.StartsWith(Folder, StringComparison.Ordinal));

    /// <summary>The positive half: a scanner that reads nothing passes for ever (testing.md, A structural test that matches nothing).</summary>
    [Fact]
    public void TheScannerSeesTheAuthorsFolder() => AuthorsFiles().Should().NotBeEmpty();

    [Fact]
    public void NoAuthorsSourceNamesTheClockTheDiskOrTheEnvironment()
    {
        var found = AuthorsFiles()
            .SelectMany(file => Forbidden.Where(word => ProductionSources.CodeOf(file).Contains(word, StringComparison.Ordinal)).Select(word => file + " names " + word))
            .ToList();

        found.Should().BeEmpty("a pure function reads nothing but its arguments");
    }
}
