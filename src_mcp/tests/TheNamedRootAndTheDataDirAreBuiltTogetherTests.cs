using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>PanelSettings.NamedDataRoot</c> — what every writer checks before it writes (research/PLAN_names_and_a_missing_root.md)
/// — and <c>PanelSettings.DataDir</c> — where it then writes — are two init properties, and they agree only because ONE
/// place builds a settings object from the environment and fills both from it (<c>PanelSettings.WithCatalog</c>).
/// </summary>
/// <remarks>
/// The code round's finding (E1 of the names plan): a second construction, or a <c>with</c> that moves <c>DataDir</c>,
/// would check one root and write to another — a missing named folder created after all, with the check passing. Nothing
/// in <c>src_mcp/src</c> does either today; this pins it, so the day one appears is a red test, not a created folder. A
/// test may still build a settings object by hand — it is not production, and it states its own data directory.
/// </remarks>
public sealed class TheNamedRootAndTheDataDirAreBuiltTogetherTests
{
    private static IEnumerable<(string File, string Text)> Sources()
    {
        var root = Path.Combine(RepoPaths.Root(), "src_mcp", "src");

        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => (Path.GetRelativePath(root, file), File.ReadAllText(file)));
    }

    [Fact]
    public void ASettingsObject_IsBuiltInOnePlace_ThePanelSettingsFile()
    {
        var builders = Sources()
            .Where(source => Regex.IsMatch(source.Text, @"\bnew\s+PanelSettings\b"))
            .Select(source => source.File)
            .ToList();

        builders.Should().Equal(
            [Path.Combine("Server", "PanelSettings.cs")],
            "the named root and the data directory are filled together only where the settings are built from the "
            + "environment — a second construction can set one without the other");
    }

    [Fact]
    public void NoWithExpression_MovesTheDataDirectoryOrTheNamedRoot()
    {
        var movers = Sources()
            .Where(source => Regex.IsMatch(source.Text, @"\bwith\s*\{[^}]*\b(DataDir|NamedDataRoot)\s*=", RegexOptions.Singleline))
            .Select(source => source.File)
            .ToList();

        movers.Should().BeEmpty(
            "a `with` that moves DataDir leaves NamedDataRoot naming the old root, so the check reads one folder and the "
            + "writer writes another");
    }
}
