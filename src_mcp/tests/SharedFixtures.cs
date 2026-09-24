namespace CoaiMcp.Tests;

/// <summary>
/// The repository's <c>shared/</c> folder, found from a test assembly that does not know where it is.
/// </summary>
/// <remarks>
/// <para>Two suites already read a file from there — <c>data-side-vectors.json</c> and
/// <c>credential-words.json</c> — and every story of the server-notices plan adds another. Each one
/// was about to walk up from <c>AppContext.BaseDirectory</c> with its own copy of the same loop,
/// which is the duplication <c>reuse-first.md</c> is about: four copies drift, and the one that
/// drifts is the one that silently stops finding the file and starts asserting nothing.</para>
/// <para>It throws rather than returning empty. A fixture that cannot find what it compares against
/// must not let the test pass.</para>
/// </remarks>
internal static class SharedFixtures
{
    /// <summary>The text of <c>shared/&lt;name&gt;</c>, or a refusal naming what was not found.</summary>
    internal static string Text(string name) => File.ReadAllText(PathOf(name));

    /// <summary>
    /// Where <c>shared/&lt;name&gt;</c> is — a file or a folder — or a refusal naming what was not found.
    /// </summary>
    /// <remarks>A folder too since issue #467: <c>shared/commands/</c> is listed, not read.</remarks>
    internal static string PathOf(string name)
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Path.Exists(Path.Combine(here.FullName, "shared", name)))
        {
            here = here.Parent;
        }

        return here is null
            ? throw new FileNotFoundException(
                $"shared/{name} was not found above {AppContext.BaseDirectory}. It is what both "
                + "halves of the product are held to, so a test that cannot read it asserts nothing.")
            : Path.Combine(here.FullName, "shared", name);
    }
}
