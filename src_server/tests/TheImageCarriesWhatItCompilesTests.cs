using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The container image's build context carries every file the compiler is told to embed.
/// </summary>
/// <remarks>
/// <para><b>`server-v0.6.0` published six binaries and no image.</b> The role catalog had moved to
/// <c>shared/builtin-roles.json</c> and <c>CoaiMcp.Core.csproj</c> embeds it from outside its own
/// directory; the Dockerfile copies one folder per PROJECT, so nothing copied <c>shared/</c> and the
/// publish inside the image failed on <c>CS1566: Error reading resource</c>. Everything else about
/// that release was green — the six platform builds, the whole server suite — because the
/// binaries build from a checkout that has the file and only the image builds from a curated
/// subset of it.</para>
/// <para>That is the shape worth guarding: the Dockerfile's copy list is written per project, and a
/// file embedded from outside a project belongs to no project's line. The next one added to
/// <c>shared/</c> would have been missed the same way, and again the evidence would arrive as a
/// failed tag rather than a failed check.</para>
/// <para>It reads the two files rather than building an image, because building one needs a daemon
/// this suite does not have — and the defect was never in the Docker build, it was in what the
/// build was given.</para>
/// </remarks>
public sealed class TheImageCarriesWhatItCompilesTests
{
    [Fact]
    public void EveryFileTheProjectsEmbedFromOutsideThemselves_IsCopiedIntoTheImage()
    {
        var dockerfile = File.ReadAllText(Path.Combine(RepoRoot(), "src_server", "Dockerfile"));

        var embedded = Directory
            .EnumerateFiles(RepoRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(NotUnderBuildOutput)
            .SelectMany(project => File.ReadLines(project))
            .Select(OutsideFolderEmbeddedIn)
            .Where(folder => folder.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        embedded.Should().NotBeEmpty(
            "this guard is worthless if it stops finding the resources it is about — "
          + "shared/builtin-roles.json is the one it was written for");

        foreach (var folder in embedded)
        {
            dockerfile.Should().Contain(
                $"COPY {folder}/ {folder}/",
                $"'{folder}' holds a file a project EMBEDS, and the image's copy list is written per "
              + "project, so nothing else will carry it");
        }
    }

    /// <summary>
    /// The folder an <c>EmbeddedResource</c> reaches OUT of its project to read, or empty.
    /// </summary>
    /// <remarks>
    /// Only the escaping ones matter: a resource inside the project's own directory arrives with
    /// that project's folder, which the Dockerfile already copies by name.
    /// </remarks>
    private static string OutsideFolderEmbeddedIn(string line)
    {
        if (!line.Contains("<EmbeddedResource", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var include = line.Split("Include=\"", 2, StringSplitOptions.None) is [_, var rest]
            ? rest.Split('"', 2)[0].Replace('\\', '/')
            : string.Empty;

        // `../../shared/builtin-roles.json` → `shared`: the first segment that is not a step upward.
        return include.StartsWith("../", StringComparison.Ordinal)
            ? include.Split('/').FirstOrDefault(part => part is not (".." or "")) ?? string.Empty
            : string.Empty;
    }

    private static bool NotUnderBuildOutput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src_vs_code")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("the repository root was not found from the test binary");
    }
}
